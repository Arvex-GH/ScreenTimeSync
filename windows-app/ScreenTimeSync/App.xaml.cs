using System.Windows;
using ScreenTimeSync.Services;

namespace ScreenTimeSync;

public partial class App : System.Windows.Application
{
    private TrackingService? _tracker;
    private BluetoothService? _bluetooth;
    private StorageService? _storage;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Initialize core services
        _storage = new StorageService();
        _storage.InitializeAsync().ConfigureAwait(false);
        
        _tracker = new TrackingService(_storage);
        _bluetooth = new BluetoothService(_tracker, _storage);

        // Start tracking + BLE advertising
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
