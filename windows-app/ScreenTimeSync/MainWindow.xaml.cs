using System.Windows;
using System.Windows.Input;
using ScreenTimeSync.Services;
using ScreenTimeSync.ViewModels;

namespace ScreenTimeSync;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // VM will be injected via App.xaml.cs in real run; fallback for designer
        try
        {
            var storage = new StorageService();
            var tracker = new TrackingService(storage);
            DataContext = new DashboardViewModel(tracker);
        }
        catch {}
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object s, RoutedEventArgs e) => Close();
}
