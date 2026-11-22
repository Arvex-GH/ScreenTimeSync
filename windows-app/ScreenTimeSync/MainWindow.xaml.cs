using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ScreenTimeSync.Models;
using ScreenTimeSync.ViewModels;

namespace ScreenTimeSync;

public partial class MainWindow : Window
{
    // Hard-coded admin password (intentional, per owner request).
    private const string AdminPassword = "akiriro";

    private readonly ObservableCollection<AdminEditRow> _editRows = new();

    /// <summary>Set only by the tray Exit action / app shutdown. Otherwise
    /// closing the window just hides it so tracking keeps running.</summary>
    public bool ForceClose { get; set; }

    public MainWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += (_, e) =>
        {
            if (ForceClose) return;
            e.Cancel = true;
            Hide();
        };
    }

    /// <summary>Bring the dashboard back (tray click, second launch, ...).</summary>
    public void ShowDashboard()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private DashboardViewModel ViewModel => (DashboardViewModel)DataContext;

    // ---- Admin drawer (hidden; opened only with Ctrl+Shift+P) ----

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Editor mode captures Enter/Esc while visible (save / cancel).
        if (AdminEditor.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Enter) { e.Handled = true; CommitEditor(); }
            else if (e.Key == Key.Escape) { e.Handled = true; CancelEditor(); }
            return;
        }

        if (e.Key == System.Windows.Input.Key.P &&
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
            (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            e.Handled = true;
            if (AdminDrawer.Visibility == Visibility.Visible)
                CloseAdminDrawer();
            else
                OpenAdminDrawer();
        }
    }

    private void OpenAdminDrawer()
    {
        AdminLocked.Visibility = Visibility.Visible;
        AdminUnlocked.Visibility = Visibility.Collapsed;
        AdminEditor.Visibility = Visibility.Collapsed;
        AdminError.Visibility = Visibility.Collapsed;
        EditorError.Visibility = Visibility.Collapsed;
        AdminPasswordBox.Password = "";
        AdminDrawer.Visibility = Visibility.Visible;
        AdminPasswordBox.Focus();
    }

    private void AdminClose_Click(object sender, RoutedEventArgs e) => CloseAdminDrawer();

    private void AdminBackdrop_Click(object sender, MouseButtonEventArgs e) => CloseAdminDrawer();

    private void CloseAdminDrawer()
    {
        AdminDrawer.Visibility = Visibility.Collapsed;
        AdminEditor.Visibility = Visibility.Collapsed;
        AdminPasswordBox.Password = "";
    }

    private void AdminPassword_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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

    // ---- Editor mode (exact time editing) ----

    private void EditorMode_Click(object sender, RoutedEventArgs e) => EnterEditorMode();

    private void EditorAddRow_Click(object sender, RoutedEventArgs e) =>
        _editRows.Add(new AdminEditRow { Category = "Other", Hours = "0", Minutes = "0" });

    private void EditorRemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AdminEditRow row })
            _editRows.Remove(row);
    }

    private void EditorCancel_Click(object sender, RoutedEventArgs e) => CancelEditor();

    private void EditorSave_Click(object sender, RoutedEventArgs e) => CommitEditor();

    private void EnterEditorMode()
    {
        _editRows.Clear();
        foreach (var snap in ViewModel.SnapshotApps().Select(AdminEditRow.FromModel))
            _editRows.Add(snap);
        EditAppsList.ItemsSource = _editRows;

        var total = ViewModel.CurrentTotalTime;
        EditTotalHours.Text = ((int)total.TotalHours).ToString();
        EditTotalMinutes.Text = total.Minutes.ToString("D2");

        EditorError.Visibility = Visibility.Collapsed;
        AdminLocked.Visibility = Visibility.Collapsed;
        AdminUnlocked.Visibility = Visibility.Collapsed;
        AdminEditor.Visibility = Visibility.Visible;
        EditTotalHours.Focus();
        EditTotalHours.SelectAll();
    }

    private void CancelEditor()
    {
        // Discard edits — rows are rebuilt from the tracker on the next entry.
        EditorError.Visibility = Visibility.Collapsed;
        AdminEditor.Visibility = Visibility.Collapsed;
        AdminUnlocked.Visibility = Visibility.Visible;
    }

    private void CommitEditor()
    {
        // ---- Total time ----
        if (!TryParsePart(EditTotalHours.Text, out var totalHours) ||
            !TryParsePart(EditTotalMinutes.Text, out var totalMinutes))
        { ShowEditorError("Total time must be whole numbers (0-24 h / 0-59 m)."); return; }
        if (totalMinutes > 59) { ShowEditorError("Total minutes must be 0-59."); return; }
        if (totalHours > 24 || (totalHours == 24 && totalMinutes > 0))
        { ShowEditorError("Total time cannot exceed 24 h."); return; }
        var newTotal = TimeSpan.FromHours(totalHours) + TimeSpan.FromMinutes(totalMinutes);

        // ---- Per-app rows ----
        var edited = new List<AppUsageModel>();
        var seenProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rowNumber = 0;
        foreach (var row in _editRows)
        {
            rowNumber++;
            var label = string.IsNullOrWhiteSpace(row.AppName) ? $"row {rowNumber}" : $"\"{row.AppName.Trim()}\"";

            if (!TryParsePart(row.Hours, out var hours) || !TryParsePart(row.Minutes, out var minutes))
            { ShowEditorError($"Hours/minutes for {label} must be whole numbers (0-24 / 0-59)."); return; }
            if (minutes > 59) { ShowEditorError($"Minutes for {label} must be 0-59."); return; }
            if (hours > 24 || hours * 60L + minutes > 24 * 60)
            { ShowEditorError($"Time for {label} cannot exceed 24 h."); return; }

            var process = (row.ProcessName ?? "").Trim().ToLowerInvariant();
            if (process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) process = process[..^4];
            if (string.IsNullOrEmpty(process))
                process = new string((row.AppName ?? "").Trim().ToLowerInvariant()
                    .Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrEmpty(process))
            { ShowEditorError($"Row {rowNumber} needs at least an app name or a process name."); return; }
            if (!seenProcesses.Add(process))
            { ShowEditorError($"Duplicate process \"{process}\" — each app can appear only once."); return; }

            edited.Add(row.ToModel(process, TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes)));
        }

        ViewModel.ApplyAdminEdits(edited, newTotal);
        ViewModel.ExportStatusText = $"Saved: total {(int)newTotal.TotalHours}h {newTotal.Minutes:D2}m, {edited.Count} app(s).";
        EditorError.Visibility = Visibility.Collapsed;
        AdminEditor.Visibility = Visibility.Collapsed;
        AdminUnlocked.Visibility = Visibility.Visible;
    }

    private void ShowEditorError(string message)
    {
        EditorError.Text = message;
        EditorError.Visibility = Visibility.Visible;
    }

    private static bool TryParsePart(string? text, out int value)
    {
        value = 0;
        if (int.TryParse((text ?? "").Trim(), out var v) && v >= 0)
        {
            value = v;
            return true;
        }
        return false;
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
