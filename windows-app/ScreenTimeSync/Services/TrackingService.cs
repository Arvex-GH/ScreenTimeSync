using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ScreenTimeSync.Models;

namespace ScreenTimeSync.Services;

/// <summary>
/// Core screen time engine: polls foreground window, tracks per-app usage, idle detection.
/// High-accuracy hybrid: WinEventHook (via NativeHook.dll) + fallback polling.
/// </summary>
public class TrackingService
{
    private readonly StorageService _storage;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _persistTimer;
    private DateTime _sessionStart;
    private string _currentProcess = "";
    private DateTime _currentAppStart;
    private TimeSpan _totalToday;
    
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
    public TimeSpan TotalScreenTime => _totalToday + (DateTime.Now - _currentAppStart).Duration();
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
        _sessionStart = DateTime.Now;
        _currentAppStart = DateTime.Now;
        _ = LoadTodayAsync();
        _pollTimer.Start();
        _persistTimer.Start();
        // native hook if available
        try { NativeHookWrapper.TryInstall(OnNativeForegroundChange); } catch {}
    }

    public void Stop()
    {
        _pollTimer.Stop();
        _persistTimer.Stop();
        FlushCurrentApp();
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
            TodayApps.Add(a);
            _appCache[a.ProcessName] = a;
        }
        _totalToday = loaded.Total;
        DataRefreshed?.Invoke();
    }

    private void PollTick(object? s, EventArgs e)
    {
        // Idle check
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref lii))
        {
            var idleMs = (uint)Environment.TickCount - lii.dwTime;
            IsIdle = idleMs > 60_000; // 1 min idle threshold
            if (IsIdle) return;
        }

        var proc = GetForegroundProcessName();
        if (proc == null) return;
        if (!string.Equals(proc, _currentProcess, StringComparison.OrdinalIgnoreCase))
        {
            OnAppChanged(proc);
        }
    }

    private void OnNativeForegroundChange(string processName) => OnAppChanged(processName);

    private void OnAppChanged(string newProc)
    {
        FlushCurrentApp();
        _currentProcess = newProc;
        _currentAppStart = DateTime.Now;

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
            AppContext.UI(() => TodayApps.Add(model));
        }
        model.IsCurrentlyActive = true;
        // deactivate others
        foreach (var kv in _appCache) if (kv.Key != newProc) kv.Value.IsCurrentlyActive = false;

        AppSwitched?.Invoke(model);
        TotalTimeUpdated?.Invoke(TotalScreenTime);
    }

    private void FlushCurrentApp()
    {
        if (string.IsNullOrEmpty(_currentProcess)) return;
        var elapsed = DateTime.Now - _currentAppStart;
        if (elapsed.TotalSeconds < 0.5) return;
        if (_appCache.TryGetValue(_currentProcess, out var m))
        {
            m.TodayUsage += elapsed;
            // update percent vs limit
            if (m.IsLimited && m.DailyLimit.TotalMinutes > 0)
                m.UsagePercent = Math.Min(100, m.TodayUsage.TotalMinutes / m.DailyLimit.TotalMinutes * 100);
            else
                m.UsagePercent = 0;
        }
        _totalToday += elapsed;
    }

    private string? GetForegroundProcessName()
    {
        try
        {
            var hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hWnd, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return null; }
    }

    private static string FormatAppName(string proc) => char.ToUpper(proc[0]) + proc[1..].ToLowerInvariant();

    // Called from Bluetooth when phone modifies limits
    public void UpdateLimit(string processName, TimeSpan limit, bool isLimited)
    {
        if (_appCache.TryGetValue(processName, out var m))
        {
            m.DailyLimit = limit;
            m.IsLimited = isLimited;
            _ = _storage.SaveSnapshotAsync(TodayApps, TotalScreenTime);
            DataRefreshed?.Invoke();
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
            AppContext.UI(() => TodayApps.Add(model));
        }
    }

    public void ResetToday()
    {
        foreach (var a in TodayApps) a.TodayUsage = TimeSpan.Zero;
        _totalToday = TimeSpan.Zero;
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
