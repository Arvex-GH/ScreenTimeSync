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

    [ObservableProperty] private string _totalTimeText = "0h 0m";
    [ObservableProperty] private string _currentApp = "—";
    [ObservableProperty] private string _bluetoothStatus = "● Advertising as ScreenTimeSync-PC";
    [ObservableProperty] private bool _isIdle;
    [ObservableProperty] private ObservableCollection<AppUsageModel> _apps = new();
    [ObservableProperty] private ObservableCollection<ISeries> _chartSeries = new();
    [ObservableProperty] private string _todayDate = DateTime.Now.ToString("dddd, MMMM d");

    public DashboardViewModel(TrackingService tracker)
    {
        _tracker = tracker;
        _tracker.AppSwitched += m => CurrentApp = m.AppName;
        _tracker.TotalTimeUpdated += t => TotalTimeText = $"{(int)t.TotalHours}h {t.Minutes:D2}m";
        _tracker.DataRefreshed += Refresh;

        Apps = new ObservableCollection<AppUsageModel>(_tracker.TodayApps);
        RefreshChart();

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => {
            TotalTimeText = $"{(int)_tracker.TotalScreenTime.TotalHours}h {_tracker.TotalScreenTime.Minutes:D2}m";
            IsIdle = _tracker.IsIdle;
        };
        _clock.Start();
    }

    void Refresh()
    {
        Apps = new ObservableCollection<AppUsageModel>(_tracker.TodayApps.OrderByDescending(a => a.TodayUsage));
        RefreshChart();
    }

    void RefreshChart()
    {
        var top = _tracker.TodayApps.OrderByDescending(a => a.TodayUsage).Take(5).ToList();
        if (!top.Any()) return;
        ChartSeries = new ObservableCollection<ISeries>(
            top.Select(a => new PieSeries<double>
            {
                Values = new[] { Math.Max(1, a.TodayUsage.TotalMinutes) },
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
        Refresh();
    }

    [RelayCommand]
    void SortByTime() => Apps = new ObservableCollection<AppUsageModel>(Apps.OrderByDescending(a => a.TodayUsage));

    [RelayCommand]
    void SortByName() => Apps = new ObservableCollection<AppUsageModel>(Apps.OrderBy(a => a.AppName));
}
