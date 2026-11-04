using System.Threading;
using System.Windows;
using ScreenTimeSync.Services;
using ScreenTimeSync.ViewModels;

namespace ScreenTimeSync;

public partial class App : System.Windows.Application
{
    private TrackingService? _tracker;
    private BluetoothService? _bluetooth;
    private StorageService? _storage;
    private MainWindow? _window;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showWindowEvent;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance: a second launch (desktop shortcut, Start menu)
        // just brings the already-tracking instance's dashboard to front.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "ScreenTimeSync.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting("ScreenTimeSync.ShowDashboard");
                signal.Set();
            }
            catch { }
            Shutdown();
            return;
        }
        ListenForShowRequests();

        var startInBackground = e.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));

        // Start counting at every logon from now on (HKCU Run -> --background).
        AutostartService.EnsureRegistered();

        // ONE shared set of services for the whole app. The window, its
        // view model and the Bluetooth service must all use the same
        // TrackingService instance, otherwise the UI binds to a tracker
        // that was never started.
        _storage = new StorageService();
        await _storage.InitializeAsync();

        _tracker = new TrackingService(_storage);
        _bluetooth = new BluetoothService(_tracker, _storage);

        _window = new MainWindow(new DashboardViewModel(_tracker));
        MainWindow = _window;
        // Closing the window hides it to the tray (tracking continues);
        // only tray -> Exit really stops the app.
        if (!startInBackground)
            _window.Show();

        // Start tracking (loads today's totals first) + BLE advertising
        _tracker.Start();
        _ = _bluetooth.StartAdvertisingAsync();

        // System tray integration (also the way back to the dashboard)
        TrayIconService.Initialize();
    }

    private void ListenForShowRequests()
    {
        _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "ScreenTimeSync.ShowDashboard");
        var thread = new Thread(() =>
        {
            while (true)
            {
                try { _showWindowEvent.WaitOne(); }
                catch { return; }
                Dispatcher.Invoke(() => (_window ?? MainWindow as MainWindow)?.ShowDashboard());
            }
        })
        { IsBackground = true, Name = "ShowDashboardListener" };
        thread.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tracker?.Stop();
        _bluetooth?.Stop();
        TrayIconService.Dispose();
        _storage?.Dispose();
        _showWindowEvent?.Dispose();
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
