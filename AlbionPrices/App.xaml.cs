using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace AlbionPrices;

public partial class App : Application
{
    private NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _notifyIcon = new NotifyIcon
        {
            Icon = Helpers.IconHelper.CreateTrayIcon(),
            Visible = true,
            Text = "AlbionPrices - Ctrl+D para abrir"
        };

        _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Show", null, (s, e) => ShowMainWindow());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());
        _notifyIcon.ContextMenuStrip = contextMenu;

        CreateMainWindow();
    }

    private void CreateMainWindow()
    {
        _mainWindow = new MainWindow();
        _mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        _mainWindow.ShowInTaskbar = false;
        // Show with Opacity=0 so the Loaded event fires (registers the hotkey),
        // then hide immediately. Without Show(), the HWND never exists and Loaded never fires.
        _mainWindow.Opacity = 0;
        _mainWindow.Show();
        _mainWindow.Hide();
        _mainWindow.Opacity = 1;

        _mainWindow.SetNotifyIcon(_notifyIcon!);
    }

    public void ShowMainWindow()
    {
        if (_mainWindow == null) return;
        _mainWindow.ShowCentered();
    }

    public void ExitApplication()
    {
        _notifyIcon?.Dispose();
        _mainWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        base.OnExit(e);
    }
}