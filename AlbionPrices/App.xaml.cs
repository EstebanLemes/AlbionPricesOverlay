using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AlbionPrices.Helpers;
using AlbionPrices.Models;
using AlbionPrices.Services;

namespace AlbionPrices;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;

    public UpdateService?        UpdateService    { get; private set; }
    public RealtimePriceService? RealtimeService  { get; private set; }
    public GameInfoService?      GameInfoService  { get; private set; }
    public AlbionApiService?     AlbionApiService { get; private set; }
    public LocalHistoryService?  HistoryService   { get; private set; }
    public WatchlistService?     WatchlistService { get; private set; }
    public AppSettings           Settings         { get; private set; } = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            Settings         = AppSettings.Load();
            UpdateService    = new UpdateService("EstebanLemes", "AlbionPricesOverlay");
            RealtimeService  = new RealtimePriceService();
            GameInfoService  = new GameInfoService { Region = Settings.Region };
            AlbionApiService = new AlbionApiService { Region = Settings.Region };
            HistoryService   = new LocalHistoryService();
            WatchlistService = new WatchlistService();
            _ = RealtimeService.ConnectAsync();

            _trayIcon = new TrayIcon
            {
                Icon        = IconHelper.CreateTrayIcon(),
                ToolTipText = "AlbionPrices — Ctrl+D para abrir",
                IsVisible   = true,
            };
            var menu     = new NativeMenu();
            var showItem = new NativeMenuItem("Mostrar");
            showItem.Click += (_, _) => ShowMainWindow();
            var exitItem = new NativeMenuItem("Salir");
            exitItem.Click += (_, _) => ExitApplication();
            menu.Items.Add(showItem);
            menu.Items.Add(exitItem);
            _trayIcon.Menu    = menu;
            _trayIcon.Clicked += (_, _) => ShowMainWindow();

            CreateMainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateMainWindow()
    {
        _mainWindow = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar         = false,
        };
        _mainWindow.Show();
        _mainWindow.Hide();
    }

    public void ChangeRegion(ServerRegion region)
    {
        Settings.Region = region;
        Settings.Save();
        if (GameInfoService  != null) GameInfoService.Region  = region;
        if (AlbionApiService != null) AlbionApiService.Region = region;
    }

    public void ShowMainWindow() => _mainWindow?.ShowCentered();

    public void ExitApplication()
    {
        _trayIcon?.Dispose();
        _mainWindow?.Close();
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
