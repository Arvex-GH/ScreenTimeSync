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
        _icon.ContextMenuStrip.Items.Add("Open Dashboard", null, (_, _) => System.Windows.Application.Current.MainWindow?.Show());
        _icon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());
        _icon.DoubleClick += (_, _) => System.Windows.Application.Current.MainWindow?.Show();
    }
}
