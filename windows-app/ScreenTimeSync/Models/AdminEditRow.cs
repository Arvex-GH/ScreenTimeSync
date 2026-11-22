using CommunityToolkit.Mvvm.ComponentModel;

namespace ScreenTimeSync.Models;

/// <summary>
/// Editable copy of one app row used by the admin time-editor.
/// Hours/minutes are kept as raw strings so the UI can show validation
/// errors instead of silently dropping bad input.
/// </summary>
public partial class AdminEditRow : ObservableObject
{
    [ObservableProperty] private string _appName = "";
    [ObservableProperty] private string _processName = "";
    [ObservableProperty] private string _category = "Other";
    [ObservableProperty] private string _hours = "0";
    [ObservableProperty] private string _minutes = "0";

    /// <summary>Original tracked entry this row was created from (null for newly added rows).</summary>
    public AppUsageModel? Source { get; set; }

    public static AdminEditRow FromModel(AppUsageModel m) => new()
    {
        AppName = m.AppName,
        ProcessName = m.ProcessName,
        Category = string.IsNullOrWhiteSpace(m.Category) ? "Other" : m.Category,
        Hours = ((int)m.TodayUsage.TotalHours).ToString(),
        Minutes = m.TodayUsage.Minutes.ToString(),
        Source = m
    };

    /// <summary>Build a tracked model from this row. Caller must have validated inputs already.</summary>
    public AppUsageModel ToModel(string resolvedProcess, TimeSpan usage) => new()
    {
        AppName = string.IsNullOrWhiteSpace(AppName)
            ? (resolvedProcess.Length > 0 ? char.ToUpperInvariant(resolvedProcess[0]) + resolvedProcess[1..] : "App")
            : AppName.Trim(),
        ProcessName = resolvedProcess,
        Category = string.IsNullOrWhiteSpace(Category) ? "Other" : Category.Trim(),
        TodayUsage = usage,
        DailyLimit = Source?.DailyLimit ?? TimeSpan.FromHours(2),
        IsLimited = Source?.IsLimited ?? false,
        WeekUsage = Source?.WeekUsage ?? TimeSpan.Zero,
        ExePath = Source?.ExePath ?? "",
        IconBase64 = Source?.IconBase64 ?? "",
        IsCurrentlyActive = false
    };
}
