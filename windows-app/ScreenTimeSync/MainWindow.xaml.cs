using System.Windows;
using System.Windows.Input;
using ScreenTimeSync.ViewModels;

namespace ScreenTimeSync;

public partial class MainWindow : Window
{
    // Hard-coded admin password (intentional, per owner request).
    private const string AdminPassword = "akiriro";

    public MainWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    // ---- Admin drawer ----

    private void AdminOpen_Click(object sender, RoutedEventArgs e)
    {
        AdminLocked.Visibility = Visibility.Visible;
        AdminUnlocked.Visibility = Visibility.Collapsed;
        AdminError.Visibility = Visibility.Collapsed;
        AdminPasswordBox.Password = "";
        AdminDrawer.Visibility = Visibility.Visible;
        AdminPasswordBox.Focus();
    }

    private void AdminClose_Click(object sender, RoutedEventArgs e) => CloseAdminDrawer();

    private void AdminBackdrop_Click(object sender, MouseButtonEventArgs e) => CloseAdminDrawer();

    private void CloseAdminDrawer()
    {
        AdminDrawer.Visibility = Visibility.Collapsed;
        AdminPasswordBox.Password = "";
    }

    private void AdminPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryUnlockAdmin();
    }

    private void AdminUnlock_Click(object sender, RoutedEventArgs e) => TryUnlockAdmin();

    private void TryUnlockAdmin()
    {
        if (AdminPasswordBox.Password == AdminPassword)
        {
            AdminError.Visibility = Visibility.Collapsed;
            AdminLocked.Visibility = Visibility.Collapsed;
            AdminUnlocked.Visibility = Visibility.Visible;
            AdminPasswordBox.Password = "";
        }
        else
        {
            AdminError.Visibility = Visibility.Visible;
            AdminPasswordBox.Password = "";
            AdminPasswordBox.Focus();
        }
    }

    // ---- Title bar ----

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object s, RoutedEventArgs e) => Close();
}
