using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using AlbionPrices.Models;
using AlbionPrices.Services;
using Application = System.Windows.Application;

namespace AlbionPrices;

public partial class App : Application
{
    private NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private UpdateService? _updateService;
    private RealtimePriceService? _realtimeService;
    private GameInfoService? _gameInfoService;
    private AlbionApiService? _albionApiService;
    private LocalHistoryService? _historyService;

    public AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = AppSettings.Load();

        _updateService    = new UpdateService("EstebanLemes", "AlbionPricesOverlay");
        _realtimeService  = new RealtimePriceService();
        _gameInfoService  = new GameInfoService { Region = Settings.Region };
        _albionApiService = new AlbionApiService { Region = Settings.Region };
        _historyService   = new LocalHistoryService();
        _ = _realtimeService.ConnectAsync();

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

    public UpdateService?        UpdateService    => _updateService;
    public RealtimePriceService? RealtimeService  => _realtimeService;
    public GameInfoService?      GameInfoService  => _gameInfoService;
    public AlbionApiService?     AlbionApiService => _albionApiService;
    public LocalHistoryService?  HistoryService   => _historyService;

    public void ChangeRegion(ServerRegion region)
    {
        Settings.Region = region;
        Settings.Save();
        if (_gameInfoService  != null) _gameInfoService.Region  = region;
        if (_albionApiService != null) _albionApiService.Region = region;
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
