using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ScreenTimeSync.Models;

namespace ScreenTimeSync.Services;

/// <summary>
/// Core screen time engine: polls foreground window, tracks per-app usage, idle detection.
///
/// Accounting model (tick-credit): every poll (~800 ms) credits the elapsed time
/// since the previous poll to the then-foreground app and to today's total.
/// There is no "current segment start" timestamp to leak into the total, idle
/// time is never credited, and gaps (sleep/hibernate/lock) are capped.
/// </summary>
public class TrackingService
{
    private const int IdleThresholdMs = 60_000;
    private static readonly TimeSpan MaxCreditPerTick = TimeSpan.FromSeconds(2);

    private readonly StorageService _storage;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _persistTimer;
    private string _currentProcess = "";
    private TimeSpan _totalToday;
    private DateTime _lastTick;
    private bool _loaded;

    // Idle detection
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] static extern bool GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    public event Action<AppUsageModel>? AppSwitched;
    public event Action<TimeSpan>? TotalTimeUpdated;
    public event Action? DataRefreshed;

    public List<AppUsageModel> TodayApps { get; } = [];
    /// <summary>Total active (non-idle) time credited today. Only ever grows by credited ticks.</summary>
    public TimeSpan TotalScreenTime => _totalToday;
    public bool IsIdle { get; private set; }
    public string CurrentAppName => _currentProcess;

    private readonly Dictionary<string, AppUsageModel> _appCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _categoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"]="Browser", ["msedge"]="Browser", ["firefox"]="Browser", ["opera"]="Browser", ["brave"]="Browser",
        ["code"]="Development", ["devenv"]="Development", ["rider"]="Development", ["idea64"]="Development",
        ["photoshop"]="Productivity", ["figma"]="Productivity", ["notion"]="Productivity", ["excel"]="Productivity", ["winword"]="Productivity",
        ["spotify"]="Media", ["vlc"]="Media", ["youtube"]="Media",
        ["discord"]="Social", ["slack"]="Social", ["teams"]="Social", ["telegram"]="Social", ["whatsapp"]="Social",
        ["steam"]="Gaming", ["valorant"]="Gaming", ["minecraft"]="Gaming", ["league"]="Gaming"
    };

    public TrackingService(StorageService storage)
    {
        _storage = storage;
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _pollTimer.Tick += PollTick;
        _persistTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _persistTimer.Tick += (_, _) => _ = _storage.SaveSnapshotAsync(TodayApps, TotalScreenTime);
    }

    public void Start()
    {
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        // Load today's persisted totals BEFORE crediting anything, otherwise
        // the first ticks would be overwritten by the loaded snapshot.
        await LoadTodayAsync();
        _loaded = true;
        _lastTick = DateTime.Now;
        _pollTimer.Start();
        _persistTimer.Start();
        // native hook if available (falls back to polling)
        try { NativeHookWrapper.TryInstall(OnNativeForegroundChange); } catch {}
    }

    public void Stop()
    {
        _pollTimer.Stop();
        _persistTimer.Stop();
        _ = _storage.SaveSnapshotAsync(TodayApps, TotalScreenTime);
        NativeHookWrapper.Uninstall();
    }

    private async Task LoadTodayAsync()
    {
        var loaded = await _storage.LoadTodayAsync();
        TodayApps.Clear();
        _appCache.Clear();
        foreach (var a in loaded.Apps)
        {
            // Guard against corrupt/implausible persisted values (e.g. from
            // earlier broken builds): a single day cannot exceed 24 h.
            if (a.TodayUsage < TimeSpan.Zero) a.TodayUsage = TimeSpan.Zero;
            if (a.TodayUsage > TimeSpan.FromHours(24)) a.TodayUsage = TimeSpan.FromHours(24);
            UpdatePercent(a);
            TodayApps.Add(a);
            _appCache[a.ProcessName] = a;
        }
        // Total is derived, not stored: every credited minute belongs to an
        // app, so the total is exactly the sum of per-app usage. (Older
        // builds persisted a total that double-counted the live segment.)
        _totalToday = TimeSpan.FromMinutes(TodayApps.Sum(a => a.TodayUsage.TotalMinutes));
        DataRefreshed?.Invoke();
        TotalTimeUpdated?.Invoke(_totalToday);
    }

    private void PollTick(object? s, EventArgs e)
    {
        if (!_loaded) return;
        var now = DateTime.Now;
        var delta = now - _lastTick;
        _lastTick = now;
        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        if (delta > MaxCreditPerTick) delta = MaxCreditPerTick; // sleep / hibernate / lock gap

        // Idle check: no input for >60 s -> credit nothing.
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref lii))
        {
            var idleMs = (uint)Environment.TickCount - lii.dwTime;
            IsIdle = idleMs > IdleThresholdMs;
            if (IsIdle)
            {
                foreach (var kv in _appCache) kv.Value.IsCurrentlyActive = false;
                return;
            }
        }

        var proc = GetForegroundProcessName();
        if (string.IsNullOrEmpty(proc)) return;

        if (!string.Equals(proc, _currentProcess, StringComparison.OrdinalIgnoreCase))
            OnAppChanged(proc);

        // Credit this tick to the foreground app and to today's total.
        if (_appCache.TryGetValue(proc, out var model) && delta > TimeSpan.Zero)
        {
            model.TodayUsage += delta;
            _totalToday += delta;
            UpdatePercent(model);
            TotalTimeUpdated?.Invoke(_totalToday);
        }
    }

    private static void UpdatePercent(AppUsageModel m)
    {
        m.UsagePercent = m.IsLimited && m.DailyLimit.TotalMinutes > 0
            ? Math.Min(100, m.TodayUsage.TotalMinutes / m.DailyLimit.TotalMinutes * 100)
            : 0;
    }

    private void OnNativeForegroundChange(string processName)
    {
        // The poll timer remains authoritative for accounting; this only
        // makes the UI react instantly to a switch.
        if (!string.IsNullOrEmpty(processName) &&
            !string.Equals(processName, _currentProcess, StringComparison.OrdinalIgnoreCase))
            OnAppChanged(processName);
    }

    private void OnAppChanged(string newProc)
    {
        _currentProcess = newProc;

        if (!_appCache.TryGetValue(newProc, out var model))
        {
            model = new AppUsageModel
            {
                AppName = FormatAppName(newProc),
                ProcessName = newProc,
                Category = _categoryMap.GetValueOrDefault(newProc.ToLowerInvariant(), "Other"),
                TodayUsage = TimeSpan.Zero
            };
            _appCache[newProc] = model;
            var m = model;
            AppContext.UI(() => { if (!TodayApps.Contains(m)) TodayApps.Add(m); });
        }
        foreach (var kv in _appCache) kv.Value.IsCurrentlyActive = kv.Key.Equals(newProc, StringComparison.OrdinalIgnoreCase);

        AppSwitched?.Invoke(model);
        DataRefreshed?.Invoke();
    }

    private string? GetForegroundProcessName()
    {
        try
        {
            var hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return null;
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return null; }
    }

    private static string FormatAppName(string proc) =>
        string.IsNullOrEmpty(proc) ? proc : char.ToUpper(proc[0]) + proc[1..];

    // Called from Bluetooth when phone modifies limits
    public void UpdateLimit(string processName, TimeSpan limit, bool isLimited)
    {
        if (string.IsNullOrEmpty(processName)) return;
        if (_appCache.TryGetValue(processName, out var m))
        {
            m.DailyLimit = limit;
            m.IsLimited = isLimited;
            UpdatePercent(m);
        }
        else
        {
            var model = new AppUsageModel
            {
                ProcessName = processName,
                AppName = FormatAppName(processName),
                DailyLimit = limit,
                IsLimited = isLimited,
                Category = _categoryMap.GetValueOrDefault(processName.ToLowerInvariant(), "Other")
            };
            _appCache[processName] = model;
            AppContext.UI(() => { if (!TodayApps.Contains(model)) TodayApps.Add(model); });
        }
        _ = _storage.SaveSnapshotAsync(TodayApps, TotalScreenTime);
        DataRefreshed?.Invoke();
    }

    public void ResetToday()
    {
        foreach (var a in TodayApps) { a.TodayUsage = TimeSpan.Zero; UpdatePercent(a); }
        _totalToday = TimeSpan.Zero;
        TotalTimeUpdated?.Invoke(_totalToday);
        _ = _storage.SaveSnapshotAsync(TodayApps, TotalScreenTime);
        DataRefreshed?.Invoke();
    }
}

internal static class AppContext
{
    public static void UI(Action a) => System.Windows.Application.Current?.Dispatcher.Invoke(a);
}

internal static class NativeHookWrapper
{
    [DllImport("NativeHook.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern bool InstallHook(int threadId);
    [DllImport("NativeHook.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern void UninstallHook();

    public static bool TryInstall(Action<string> callback) => false; // fallback to polling if DLL not present
    public static void Uninstall() { try { UninstallHook(); } catch {} }
}
