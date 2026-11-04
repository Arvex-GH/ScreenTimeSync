using System.Windows;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenTimeSync.Services;

public static class TrayIconService
{
    private static NotifyIcon? _icon;

    public static void Initialize()
    {
        _icon = new NotifyIcon
        {
            Text = "ScreenTimeSync — tracking active",
            Visible = true,
            Icon = SystemIcons.Application,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _icon.ContextMenuStrip.Items.Add("Open Dashboard", null, (_, _) => ShowDashboard());
        _icon.ContextMenuStrip.Items.Add("Exit (stop tracking)", null, (_, _) => ExitApp());
        _icon.DoubleClick += (_, _) => ShowDashboard();
    }

    private static void ShowDashboard()
    {
        if (System.Windows.Application.Current.MainWindow is MainWindow window)
            window.ShowDashboard();
        else
            System.Windows.Application.Current.MainWindow?.Show();
    }

    private static void ExitApp()
    {
        // The one explicit way to really stop tracking: tray -> Exit.
        if (System.Windows.Application.Current.MainWindow is MainWindow window)
            window.ForceClose = true;
        _icon!.Visible = false;
        System.Windows.Application.Current.Shutdown();
    }

    public static void Dispose()
    {
        if (_icon != null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}
