using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using ScreenTimeSync.Models;
using ScreenTimeSync.Services;

namespace ScreenTimeSync.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly TrackingService _tracker;
    private readonly DispatcherTimer _clock;
    private int _tickCount;

    [ObservableProperty] private string _totalTimeText = "0h 00m";
    [ObservableProperty] private string _currentApp = "—";
    [ObservableProperty] private string _bluetoothStatus = "● Advertising as ScreenTimeSync-PC";
    [ObservableProperty] private bool _isIdle;
    [ObservableProperty] private string _activityText = "active now";
    [ObservableProperty] private ObservableCollection<AppUsageModel> _apps = new();
    [ObservableProperty] private ObservableCollection<ISeries> _chartSeries = new();
    [ObservableProperty] private string _todayDate = DateTime.Now.ToString("dddd, MMMM d");
    [ObservableProperty] private string _mostUsedText = "—";
    [ObservableProperty] private string _productivityText = "—";

    public DashboardViewModel(TrackingService tracker)
    {
        _tracker = tracker;
        _tracker.AppSwitched += m =>
        {
            CurrentApp = m.AppName;
            EnsureAppListed(m);
        };
        _tracker.TotalTimeUpdated += t =>
        {
            TotalTimeText = FormatTotal(t);
            // Re-sort + refresh the chart roughly every 12 s, not on every tick.
            if (++_tickCount % 15 == 0) Refresh(refreshChart: true);
        };
        _tracker.DataRefreshed += () => Refresh(refreshChart: true);

        Apps = new ObservableCollection<AppUsageModel>(_tracker.TodayApps.OrderByDescending(a => a.TodayUsage));
        Refresh(refreshChart: true);

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => {
            TotalTimeText = FormatTotal(_tracker.TotalScreenTime);
            IsIdle = _tracker.IsIdle;
            ActivityText = _tracker.IsIdle ? "idle - not counting" : "active now";
            if (_tracker.IsIdle) CurrentApp = "Idle";
            else if (!string.IsNullOrEmpty(_tracker.CurrentAppName))
                CurrentApp = _tracker.TodayApps.FirstOrDefault(a => a.ProcessName.Equals(_tracker.CurrentAppName, StringComparison.OrdinalIgnoreCase))?.AppName ?? CurrentApp;
        };
        _clock.Start();
    }

    private static string FormatTotal(TimeSpan t) => $"{(int)t.TotalHours}h {t.Minutes:D2}m";

    private void EnsureAppListed(AppUsageModel m)
    {
        if (!Apps.Contains(m)) Apps.Insert(0, m);
    }

    void Refresh(bool refreshChart)
    {
        var sorted = _tracker.TodayApps.OrderByDescending(a => a.TodayUsage).ToList();
        // Rebuild only when membership/order actually changed, to avoid UI churn.
        if (Apps.Count != sorted.Count || !Apps.SequenceEqual(sorted))
            Apps = new ObservableCollection<AppUsageModel>(sorted);
        UpdateSummary(sorted);
        if (refreshChart) RefreshChart(sorted);
    }

    void UpdateSummary(List<AppUsageModel> sorted)
    {
        var top = sorted.FirstOrDefault(a => a.TodayUsage > TimeSpan.Zero);
        MostUsedText = top == null ? "—" : $"{top.AppName} • {(int)top.TodayUsage.TotalHours}h {top.TodayUsage.Minutes:D2}m";

        var totalMin = sorted.Sum(a => a.TodayUsage.TotalMinutes);
        if (totalMin <= 0) { ProductivityText = "—"; return; }
        var productiveMin = sorted
            .Where(a => a.Category is "Productivity" or "Development")
            .Sum(a => a.TodayUsage.TotalMinutes);
        ProductivityText = $"{(int)Math.Round(productiveMin / totalMin * 100)} / 100";
    }

    void RefreshChart() => RefreshChart(_tracker.TodayApps.OrderByDescending(a => a.TodayUsage).ToList());

    void RefreshChart(List<AppUsageModel> sorted)
    {
        var top = sorted.Where(a => a.TodayUsage > TimeSpan.Zero).Take(5).ToList();
        if (!top.Any()) { ChartSeries = new ObservableCollection<ISeries>(); return; }
        ChartSeries = new ObservableCollection<ISeries>(
            top.Select(a => new PieSeries<double>
            {
                Values = new[] { Math.Max(0.1, a.TodayUsage.TotalMinutes) },
                Name = a.AppName,
                DataLabelsPaint = new SolidColorPaint(SKColors.White),
                DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                DataLabelsFormatter = p => $"{p.Coordinate.PrimaryValue:0}m"
            })
        );
    }

    [RelayCommand]
    void ResetToday()
    {
        _tracker.ResetToday();
        Refresh(refreshChart: true);
    }

    [RelayCommand]
    void SortByTime() => Apps = new ObservableCollection<AppUsageModel>(Apps.OrderByDescending(a => a.TodayUsage));

    [RelayCommand]
    void SortByName() => Apps = new ObservableCollection<AppUsageModel>(Apps.OrderBy(a => a.AppName));
}
