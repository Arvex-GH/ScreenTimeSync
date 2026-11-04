using System.Windows;
using ScreenTimeSync.Services;
using ScreenTimeSync.ViewModels;

namespace ScreenTimeSync;

public partial class App : System.Windows.Application
{
    private TrackingService? _tracker;
    private BluetoothService? _bluetooth;
    private StorageService? _storage;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ONE shared set of services for the whole app. The window, its
        // view model and the Bluetooth service must all use the same
        // TrackingService instance, otherwise the UI binds to a tracker
        // that was never started.
        _storage = new StorageService();
        await _storage.InitializeAsync();

        _tracker = new TrackingService(_storage);
        _bluetooth = new BluetoothService(_tracker, _storage);

        var window = new MainWindow(new DashboardViewModel(_tracker));
        MainWindow = window;
        window.Show();

        // Start tracking (loads today's totals first) + BLE advertising
        _tracker.Start();
        _ = _bluetooth.StartAdvertisingAsync();

        // System tray integration
        TrayIconService.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tracker?.Stop();
        _bluetooth?.Stop();
        _storage?.Dispose();
        base.OnExit(e);
    }
}
