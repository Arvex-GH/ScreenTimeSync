using Microsoft.Win32;

namespace ScreenTimeSync.Services;

/// <summary>
/// Registers ScreenTimeSync to start automatically when the user logs on
/// (HKCU Run key), launched with --background so it tracks from boot
/// without popping up a window. Idempotent: called on every startup so
/// the registered path always matches the current install location.
/// </summary>
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScreenTimeSync";

    public static void EnsureRegistered()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            // Only register the real app exe (not e.g. `dotnet run` during dev).
            if (string.IsNullOrEmpty(exePath) ||
                !exePath.EndsWith("ScreenTimeSync.exe", StringComparison.OrdinalIgnoreCase))
                return;

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;
            var desired = $"\"{exePath}\" --background";
            var current = key.GetValue(ValueName) as string;
            if (!string.Equals(current, desired, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, desired, RegistryValueKind.String);
        }
        catch { /* autostart must never break startup */ }
    }
}
