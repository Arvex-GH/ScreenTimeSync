using System.Windows;
using System.Windows.Input;
using ScreenTimeSync.ViewModels;

namespace ScreenTimeSync;

public partial class MainWindow : Window
{
    public MainWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object s, RoutedEventArgs e) => Close();
}
