using CommunityToolkit.Mvvm.ComponentModel;

namespace ScreenTimeSync.Models;

public partial class AppUsageModel : ObservableObject
{
    [ObservableProperty] private string _appName = "";
    [ObservableProperty] private string _processName = "";
    [ObservableProperty] private string _exePath = "";
    [ObservableProperty] private string _iconBase64 = "";
    [ObservableProperty] private TimeSpan _todayUsage;
    [ObservableProperty] private TimeSpan _weekUsage;
    [ObservableProperty] private TimeSpan _dailyLimit = TimeSpan.FromHours(2);
    [ObservableProperty] private bool _isLimited;
    [ObservableProperty] private bool _isCurrentlyActive;
    [ObservableProperty] private double _usagePercent; // 0-100
    [ObservableProperty] private string _category = "Other"; // Productivity, Social, Gaming, Browser, etc.

    // Keep computed display properties in sync when the underlying values change.
    partial void OnTodayUsageChanged(TimeSpan value) => OnPropertyChanged(nameof(DisplayTime));
    partial void OnDailyLimitChanged(TimeSpan value) => OnPropertyChanged(nameof(LimitText));
    partial void OnIsLimitedChanged(bool value) => OnPropertyChanged(nameof(LimitText));
    partial void OnCategoryChanged(string value) => OnPropertyChanged(nameof(CategoryColor));

    public string DisplayTime => $"{(int)TodayUsage.TotalHours}h {TodayUsage.Minutes}m";
    public string LimitText => IsLimited ? $"{(int)DailyLimit.TotalHours}h {DailyLimit.Minutes}m limit" : "No limit";
    public string CategoryColor => Category switch
    {
        "Browser" => "#6C5CE7",
        "Productivity" => "#00B894",
        "Social" => "#E84393",
        "Gaming" => "#FDCB6E",
        "Development" => "#0984E3",
        "Media" => "#E17055",
        _ => "#636E72"
    };
}

public class DailySummary
{
    public DateTime Date { get; set; }
    public TimeSpan TotalScreenTime { get; set; }
    public TimeSpan TotalActiveTime { get; set; } // excluding idle
    public int Unlocks { get; set; }
    public List<AppUsageModel> TopApps { get; set; } = [];
}

public class BluetoothMessage
{
    public string Type { get; set; } = ""; // sync, update_limit, block_app, ping
    public string AppId { get; set; } = "";
    public int LimitMinutes { get; set; }
    public bool Blocked { get; set; }
    public Dictionary<string, object> Payload { get; set; } = new();
}
