using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using AlbionPrices.Services;
using Application = System.Windows.Application;

namespace AlbionPrices;

public partial class App : Application
{
    private NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private UpdateService? _updateService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _updateService = new UpdateService("tu-usuario", "AlbionPricesOverlay");

        _notifyIcon = new NotifyIcon
        {
            Icon = Helpers.IconHelper.CreateTrayIcon(),
            Visible = true,
            Text = "AlbionPrices - Ctrl+D para abrir"
        };

        _notifyIcon.DoubleClick += (s, ev) => ShowMainWindow();

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Show", null, (s, ev) => ShowMainWindow());
        contextMenu.Items.Add("Exit", null, (s, ev) => ExitApplication());
        _notifyIcon.ContextMenuStrip = contextMenu;

        CreateMainWindow();
    }

    private void CreateMainWindow()
    {
        _mainWindow = new MainWindow();
        _mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        _mainWindow.ShowInTaskbar = false;
        _mainWindow.Opacity = 0;
        _mainWindow.Show();
        _mainWindow.Hide();
        _mainWindow.Opacity = 1;

        _mainWindow.SetNotifyIcon(_notifyIcon!);
    }

    public UpdateService? UpdateService => _updateService;

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