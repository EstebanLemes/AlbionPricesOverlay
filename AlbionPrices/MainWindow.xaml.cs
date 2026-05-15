using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AlbionPrices.Helpers;
using AlbionPrices.Models;
using AlbionPrices.Services;

namespace AlbionPrices;

public partial class MainWindow : Window
{
    private readonly AlbionApiService _apiService;
    private readonly ItemDatabase     _itemDatabase;
    private Button[]?  _regionButtons;
    private GlobalHotkey? _hotkey;
    private bool _isLoading;
    private bool _dbLoaded;
    private bool _dialogOpen;
    private DispatcherTimer? _hideTimer;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _playerCts;
    private CancellationTokenSource? _setValueCts;
    private CancellationTokenSource? _statsCts;

    private string? _baseId;
    private int  _currentTier;
    private int  _currentEnchant;
    private int  _currentQuality = 1;
    private Dictionary<int, List<int>> _variants = new();
    private string? _currentItemId;
    private string? _currentItemName;
    private string? _currentStatsUrl;
    private readonly List<SearchSuggestion> _activeSuggestions = new();
    private int _selectedSuggestionIndex = -1;
    private bool _suppressSuggestionRefresh;
    private string _priceInfoTab = "prices";

    private List<PriceHistoryPoint>? _sparklineData;
    private Size _lastSparklineBounds;
    private readonly Dictionary<string, CityPriceViewModel> _cityViewModels = new();
    private readonly Dictionary<string, double> _combatUnitPriceCache = new(StringComparer.OrdinalIgnoreCase);
    private double _currentBestBuyPrice;

    private DispatcherTimer? _watchTimer;
    private int              _watchSecondsLeft;
    private const int        WatchIntervalSeconds = 300;

    private string?               _craftTargetId;
    private string?               _craftTargetName;
    private double                _craftTargetSellPrice;
    private readonly List<CraftMaterial> _craftMaterials = new();

    private Button[]? _modeBtns;
    private Button[]? _calcSubBtns;

    // ── Island / farming state ────────────────────────────────────────────────
    private readonly FarmingGuideService _farmingGuide = new();
    private string?  _newIslandCity;
    private int      _newIslandTier = 4;
    private Button[]? _newIslandCityBtns;
    private Button[]? _newIslandTierBtns;
    private readonly List<(IslandConfig Config, StackPanel RecoPanel, Border Card)> _islandCards = new();

    // ── Refining state ────────────────────────────────────────────────────────
    private string _refineResource = "Mineral";
    private int    _refineTier     = 5;
    private string _refineCity     = "Thetford";
    private bool   _refineFocus    = false;

    private static readonly Dictionary<string, (string Raw, string Refined)> ResourceIds = new()
    {
        ["Mineral"] = ("ORE",   "METALBAR"),
        ["Fibra"]   = ("FIBER", "CLOTH"),
        ["Madera"]  = ("WOOD",  "PLANKS"),
        ["Cuero"]   = ("HIDE",  "LEATHER"),
        ["Piedra"]  = ("ROCK",  "STONEBLOCK"),
    };
    private static readonly Dictionary<string, string> ResourceMatchCity = new()
    {
        ["Mineral"] = "Thetford",
        ["Fibra"]   = "Bridgewatch",
        ["Madera"]  = "Lymhurst",
        ["Cuero"]   = "Martlock",
        ["Piedra"]  = "Fort Sterling",
    };

    // ── Flip scanner state ───────────────────────────────────────────────────
    private bool   _flipScanInitialized;
    private DispatcherTimer? _scanTimer;
    private CancellationTokenSource? _scanCts;
    private string? _flipOrigin;
    private string? _flipDest;
    private string  _flipCategory     = "Todo";
    private bool    _flipPremium      = false;
    private double  _flipTransportPct = 0;
    private bool    _flipJournalExpanded = false;
    private List<PriceApiResponse> _rawScanData    = new();
    private List<FlipOpportunity>  _rawFlipResults = new();
    private ScanCatalogService  _scanCatalog  = new();
    private FlipJournalService  _flipJournal  = new();

    // ── Crafting state ────────────────────────────────────────────────────────
    private bool _craftPremium = false;
    private Button[]? _flipOriginBtns;
    private Button[]? _flipDestBtns;
    private Button[]? _flipCategoryBtns;

    private static readonly string[] FlipCities =
    [
        "Todos", "Thetford", "Lymhurst", "Bridgewatch", "Fort Sterling",
        "Martlock", "Caerleon", "Black M.", "Brecilien",
    ];
    private static readonly string[] FlipCityApiNames =
    [
        null!, "Thetford", "Lymhurst", "Bridgewatch", "Fort Sterling",
        "Martlock", "Caerleon", "Black Market", "Brecilien",
    ];
    private static readonly string[] FlipCategories =
        ["Todo", "Armas", "Armad.", "Acces.", "Recursos", "Encant."];

    private static readonly (int Tier, int Enchant)[] ScanTierCombos = [
        (4, 3), (5, 1), (5, 2), (5, 3), (6, 1), (6, 2), (6, 3), (7, 1), (7, 2),
    ];
    // All enchant levels for the enchanting-opportunity scan
    private static readonly (int Tier, int Enchant)[] ScanEnchantCombos = [
        (4,0),(4,1),(4,2),(4,3),(5,0),(5,1),(5,2),(5,3),
        (6,0),(6,1),(6,2),(6,3),(7,0),(7,1),(7,2),(7,3),(8,0),(8,1),(8,2),(8,3),
    ];

    // ── Enchanting state ──────────────────────────────────────────────────────
    private string? _enchantBaseId;

    // ── Route state ───────────────────────────────────────────────────────────
    private readonly List<(string Id, string Name, int Qty)> _routeItems = new();

    private static readonly Regex TieredItemRegex =
        new(@"^T(\d)_(.+?)(?:@(\d))?$", RegexOptions.Compiled);

    public MainWindow()
    {
        InitializeComponent();
        Icon = IconHelper.CreateWindowIcon();

        _apiService   = (Application.Current as App)?.AlbionApiService ?? new AlbionApiService();
        _itemDatabase = new ItemDatabase();

        Loaded      += MainWindow_Loaded;
        Closed      += MainWindow_Closed;
        Deactivated += MainWindow_Deactivated;
        Activated   += (_, _) => _hideTimer?.Stop();

        SparklineCanvas.LayoutUpdated += (_, _) =>
        {
            var sz = SparklineCanvas.Bounds.Size;
            if (_sparklineData == null || sz == _lastSparklineBounds) return;
            _lastSparklineBounds = sz;
            DrawSparkline(_sparklineData);
        };

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _hideTimer.Tick += HideTimer_Tick;

        var rt = (Application.Current as App)?.RealtimeService;
        if (rt != null)
        {
            rt.PriceUpdated      += OnRealtimePriceUpdated;
            rt.ConnectionChanged += OnRealtimeConnectionChanged;
        }

        Loaded += async (s, e) =>
        {
            _regionButtons = [RegionNABtn, RegionEUBtn, RegionASBtn];
            _modeBtns      = [PriceModeBtn, CraftingModeBtn, WatchModeBtn, PlayerModeBtn, FlipModeBtn, IslandModeBtn];
            _calcSubBtns   = [CraftSubBtn, RefineSubBtn, EnchantSubBtn, RouteSubBtn];
            InitBuildValuationSelector();
            OpenCatalogBtn.Click += OpenCatalog_Click;
            KillsList.PointerReleased += KillEntry_Click;
            DeathsList.PointerReleased += CombatEntry_Click;
            FlipList.PointerReleased += FlipItem_Click;
            InitRefineSelectors();
            InitRoutePanel();
            InitFlipCategorySelectors();
            InitFlipCitySelectors();
            ApplyPersistedFlipSettings();
            var savedRegion = (Application.Current as App)?.Settings.Region
                              ?? ServerRegion.Europe;
            RefreshRegionButtons(savedRegion);

            if (_dbLoaded) return;
            _dbLoaded = true;
            StatusText.Text = "Descargando base de datos...";
            await _itemDatabase.LoadAsync();
            StatusText.Text = _itemDatabase.ItemCount == 0
                ? $"ERROR DB: {_itemDatabase.LoadError}"
                : $"{_itemDatabase.ItemCount:N0} items cargados. Escribi el nombre del item.";

            if (!_scanCatalog.Load() && _itemDatabase.ItemCount > 0)
            {
                _scanCatalog.GenerateDefault(_itemDatabase);
                _scanCatalog.Save();
            }
            _flipJournal.Load();

            RefreshHistoryUI();
            _ = LoadGoldPriceAsync();

            _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _scanTimer.Tick += async (_, _) => await ScanFlipOpportunitiesAsync();
            _scanTimer.Start();

            try
            {
                var updateService = (Application.Current as App)?.UpdateService;
                if (updateService != null)
                {
                    await updateService.CheckForUpdateAsync();
                    if (updateService.IsUpdateAvailable)
                    {
                        UpdateBanner.Text      = $"Nueva version disponible: v{updateService.LatestVersion}";
                        UpdateBanner.IsVisible = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check error: {ex.Message}");
            }
        };
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        _hotkey = new GlobalHotkey(9000);
        if (_hotkey.Register()) _hotkey.HotkeyPressed += OnHotkeyPressed;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _hotkey?.Dispose();
        _watchTimer?.Stop();
        _scanTimer?.Stop();
        _scanCts?.Cancel();
        _statsCts?.Cancel();
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (IsVisible && !_isLoading && !_dialogOpen) _hideTimer?.Start();
    }

    private void HideTimer_Tick(object? sender, EventArgs e)
    {
        _hideTimer?.Stop();
        Hide();
    }

    internal void ShowCentered(bool focusSearch = true)
    {
        ApplyOverlaySize();
        CenterOnPrimaryScreen();

        if (IsVisible && IsLoaded)
        {
            Activate();
            Focus();
            if (focusSearch)
            {
                ItemInput.Focus();
                ItemInput.SelectAll();
            }
            return;
        }

        Show(); Activate(); Focus();
        if (focusSearch)
        {
            ItemInput.Focus();
            ItemInput.SelectAll();
        }
    }

    private void ApplyOverlaySize()
    {
        var screen = Screens.Primary;
        if (screen == null) return;

        var wa = screen.WorkingArea;
        var s  = screen.Scaling;
        Width  = Math.Max(760, Math.Round(wa.Width  / s * 0.80));
        Height = Math.Max(520, Math.Round(wa.Height / s * 0.80));
    }

    private void CenterOnPrimaryScreen()
    {
        var screen = Screens.Primary;
        if (screen == null) return;

        var wa = screen.WorkingArea;
        var s  = screen.Scaling;
        Position = new PixelPoint(
            wa.X + (int)((wa.Width  - Width  * s) / 2),
            wa.Y + (int)((wa.Height - Height * s) / 2));
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        _hideTimer?.Stop();
        Dispatcher.UIThread.InvokeAsync(() => ShowCentered());
    }

    // ── Mode toggle ──────────────────────────────────────────────────────────

    private void SetActiveMode(StackPanel active, Button activeBtn)
    {
        PriceModeContent.IsVisible    = active == PriceModeContent;
        CraftingModeContent.IsVisible = active == CraftingModeContent;
        WatchModeContent.IsVisible    = active == WatchModeContent;
        PlayerModeContent.IsVisible   = active == PlayerModeContent;
        FlipModeContent.IsVisible     = active == FlipModeContent;
        IslandModeContent.IsVisible   = active == IslandModeContent;

        foreach (var btn in _modeBtns ?? [])
        {
            btn.Background = Brushes.Transparent;
            btn.Foreground = new SolidColorBrush(Color.FromRgb(122, 130, 144));
            btn.BorderBrush = Brushes.Transparent;
            btn.FontWeight = FontWeight.Normal;
        }
        activeBtn.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0x75, 0x50));
        activeBtn.Foreground = Brushes.White;
        activeBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 117, 80));
        activeBtn.FontWeight = FontWeight.Bold;

        ApplyOverlaySize();
        CenterOnPrimaryScreen();
    }

    private void SetActiveCalcSub(StackPanel active, Button activeBtn)
    {
        CraftSubPanel.IsVisible  = active == CraftSubPanel;
        RefineSubPanel.IsVisible = active == RefineSubPanel;
        EnchantSubPanel.IsVisible = active == EnchantSubPanel;
        RouteSubPanel.IsVisible  = active == RouteSubPanel;
        foreach (var btn in _calcSubBtns ?? [])
        {
            btn.Background = Brushes.Transparent;
            btn.Foreground = new SolidColorBrush(Color.FromRgb(120, 130, 142));
            btn.FontWeight = FontWeight.Normal;
        }
        activeBtn.Background = new SolidColorBrush(Color.FromRgb(255, 117, 80));
        activeBtn.Foreground = Brushes.White;
        activeBtn.FontWeight = FontWeight.Bold;
        ForceResizeWindow();
    }

    private void CraftSubMode_Click(object?  sender, RoutedEventArgs e) => SetActiveCalcSub(CraftSubPanel,   CraftSubBtn);
    private void RefineSubMode_Click(object? sender, RoutedEventArgs e) => SetActiveCalcSub(RefineSubPanel,  RefineSubBtn);
    private void EnchantSubMode_Click(object? sender, RoutedEventArgs e) => SetActiveCalcSub(EnchantSubPanel, EnchantSubBtn);
    private void RouteSubMode_Click(object?  sender, RoutedEventArgs e) => SetActiveCalcSub(RouteSubPanel,   RouteSubBtn);

    private void PriceMode_Click(object? sender, RoutedEventArgs e)
    {
        SetActiveMode(PriceModeContent, PriceModeBtn);
        (Application.Current as App)?.RealtimeService?.SetItem(_currentItemId);
    }

    private void CraftingMode_Click(object? sender, RoutedEventArgs e)
    {
        SetActiveMode(CraftingModeContent, CraftingModeBtn);
        (Application.Current as App)?.RealtimeService?.SetItem(null);
    }

    private void WatchMode_Click(object? sender, RoutedEventArgs e)
    {
        SetActiveMode(WatchModeContent, WatchModeBtn);
        (Application.Current as App)?.RealtimeService?.SetItem(null);
        EnsureWatchTimerRunning();
        RefreshWatchlistUI();
    }

    private void PlayerMode_Click(object? sender, RoutedEventArgs e)
    {
        SetActiveMode(PlayerModeContent, PlayerModeBtn);
        (Application.Current as App)?.RealtimeService?.SetItem(null);
        PlayerInput.Focus();
    }

    private void IslandMode_Click(object? sender, RoutedEventArgs e)
    {
        SetActiveMode(IslandModeContent, IslandModeBtn);
        (Application.Current as App)?.RealtimeService?.SetItem(null);
        if (!_islandTabInitialized) InitIslandTab();
    }

    private void FlipMode_Click(object? sender, RoutedEventArgs e)
    {
        SetActiveMode(FlipModeContent, FlipModeBtn);
        (Application.Current as App)?.RealtimeService?.SetItem(null);
        if (!_flipScanInitialized)
        {
            _flipScanInitialized = true;
            _ = ScanFlipOpportunitiesAsync();
        }
    }

    private async void ScanNow_Click(object? sender, RoutedEventArgs e) =>
        await ScanFlipOpportunitiesAsync();

    public void OpenCatalog_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                ScanCatalogService.GetFilePath()) { UseShellExecute = true });
        }
        catch { }
    }

    public void FlipItem_Click(object? sender, PointerReleasedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not FlipOpportunity opp) return;

        _flipJournal.Add(new FlipJournalEntry
        {
            Date           = DateTime.UtcNow,
            ItemId         = opp.ItemId,
            ItemName       = opp.ItemName,
            TierLabel      = opp.TierLabel,
            BuyCity        = opp.BuyCity,
            BuyPrice       = opp.BuyPrice,
            SellCity       = opp.SellCity,
            SellPrice      = opp.SellPrice,
            ExpectedProfit = opp.Profit,
        });
        RefreshFlipJournalUI();

        SetActiveMode(PriceModeContent, PriceModeBtn);
        (Application.Current as App)?.RealtimeService?.SetItem(null);
        _ = CheckItemById(opp.ItemId, opp.ItemName, opp.Quality, updateSearchText: false, focusSearch: false);
    }

    // ── Flip scanner ─────────────────────────────────────────────────────────

    private IEnumerable<string> GenerateScanIds()
    {
        var entries = _flipCategory == "Todo"
            ? _scanCatalog.Items
            : _scanCatalog.Items.Where(i => i.Category == _flipCategory);

        var combos = _flipCategory == "Encant." ? ScanEnchantCombos : ScanTierCombos;

        foreach (var entry in entries)
        {
            if (ScanCatalogService.IsFullItemId(entry.Id))
                yield return entry.Id;
            else
                foreach (var (tier, enc) in combos)
                    yield return enc > 0 ? $"T{tier}_{entry.Id}@{enc}" : $"T{tier}_{entry.Id}";
        }
    }

    private async Task ScanFlipOpportunitiesAsync()
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        ScanNowBtn.IsEnabled  = false;
        FlipStatusText.Text      = "Escaneando...";
        FlipStatusText.IsVisible = true;

        try
        {
            var allIds  = GenerateScanIds().ToList();
            var batches = allIds.Chunk(25).ToList();
            var allRaw  = new System.Collections.Concurrent.ConcurrentBag<PriceApiResponse>();
            int done    = 0;

            var sem   = new SemaphoreSlim(4);
            var tasks = batches.Select(async batch =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var rows = await _apiService.GetBatchPricesAsync(batch, ct);
                    if (rows != null)
                        foreach (var r in rows) allRaw.Add(r);

                    var n = Interlocked.Increment(ref done);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        FlipStatusText.Text = $"Escaneando... {n}/{batches.Count}");
                }
                finally { sem.Release(); }
            }).ToList();

            await Task.WhenAll(tasks);
            ct.ThrowIfCancellationRequested();

            _rawScanData    = allRaw.ToList();
            _rawFlipResults = BuildFlipOpportunities(_rawScanData, _flipPremium ? 0.04 : 0.08, _flipTransportPct);
            FlipLastScanText.Text = $"Escaneado: {DateTime.Now:HH:mm}";
            ApplyFlipFilters();
        }
        catch (OperationCanceledException) { }
        finally
        {
            FlipStatusText.IsVisible = false;
            ScanNowBtn.IsEnabled     = true;
        }
    }

    private List<FlipOpportunity> BuildFlipOpportunities(
        List<PriceApiResponse> raw, double taxRate = 0.08, double transportPct = 0)
    {
        var result = new List<FlipOpportunity>();
        var cutoff = DateTime.UtcNow.AddHours(-72);

        // Group by (itemId, quality) so we never mix qualities across cities
        foreach (var group in raw
            .Where(r => r.ItemId != null)
            .GroupBy(r => (r.ItemId!, r.Quality ?? 1)))
        {
            var (itemId, quality) = group.Key;

            // Buy side: sell orders (compro al precio mínimo de venta de otro jugador)
            var buyCandidates = group
                .Where(p => p.City != null
                         && p.SellPriceMin > 0
                         && p.SellPriceMinDate.HasValue
                         && p.SellPriceMinDate.Value.ToUniversalTime() >= cutoff)
                .GroupBy(p => p.City!)
                .Select(g => (
                    City:   g.Key,
                    Price:  g.Min(p => p.SellPriceMin!.Value),
                    Volume: g.Sum(p => p.SellAmount ?? 0)))
                .ToList();

            // Sell side: buy orders (vendo instantáneamente al precio máximo de compra)
            var sellCandidates = group
                .Where(p => p.City != null
                         && p.BuyPriceMax > 0
                         && p.BuyPriceMaxDate.HasValue
                         && p.BuyPriceMaxDate.Value.ToUniversalTime() >= cutoff)
                .GroupBy(p => p.City!)
                .Select(g => (
                    City:   g.Key,
                    Price:  g.Max(p => p.BuyPriceMax!.Value),
                    Volume: g.Sum(p => p.BuyAmount ?? 0)))
                .ToList();

            if (buyCandidates.Count == 0) continue;

            var cheapestBuy  = buyCandidates.MinBy(c => c.Price);
            var totalBuyCost = cheapestBuy.Price * (1 + transportPct / 100.0);

            var m         = TieredItemRegex.Match(itemId);
            var tier      = m.Success ? m.Groups[1].Value : "";
            var enc       = m.Success ? m.Groups[3].Value : "";
            var baseName  = _itemDatabase.GetNameById(itemId)
                         ?? _itemDatabase.GetNameById(itemId.Split('@')[0])
                         ?? itemId;
            var tierLabel = enc.Length > 0 ? $"T{tier}.{enc}" : $"T{tier}";

            // Venta directa: mejor orden de compra en ciudad destino
            var bestInstant = sellCandidates
                .Where(c => c.City != cheapestBuy.City && c.Price / cheapestBuy.Price <= 4.0)
                .OrderByDescending(c => c.Price)
                .FirstOrDefault();
            var instantProfit    = bestInstant.City != null ? bestInstant.Price * (1 - taxRate) - totalBuyCost : 0;
            var instantProfitPct = instantProfit > 0 && cheapestBuy.Price > 0 ? instantProfit / cheapestBuy.Price * 100 : 0;
            // SellPriceMin de referencia en ciudad de venta directa
            var instantOrderRef = bestInstant.City != null
                ? buyCandidates.FirstOrDefault(c => c.City == bestInstant.City).Price
                : 0;

            // Orden de venta: ciudad con SellPriceMin más alto
            var bestOrderDest = buyCandidates
                .Where(c => c.City != cheapestBuy.City && c.Price / cheapestBuy.Price <= 4.0)
                .OrderByDescending(c => c.Price)
                .FirstOrDefault();
            var orderProfit    = bestOrderDest.City != null ? bestOrderDest.Price * (1 - taxRate) - totalBuyCost : 0;
            var orderProfitPct = orderProfit > 0 && cheapestBuy.Price > 0 ? orderProfit / cheapestBuy.Price * 100 : 0;
            // BuyPriceMax de referencia en ciudad de orden de venta
            var orderInstantRef = bestOrderDest.City != null
                ? sellCandidates.FirstOrDefault(c => c.City == bestOrderDest.City).Price
                : 0;

            if (instantProfit <= 0 && orderProfit <= 0) continue;

            result.Add(new FlipOpportunity
            {
                ItemId    = itemId,
                ItemName  = baseName,
                TierLabel = tierLabel,
                Quality   = quality,
                BuyCity   = cheapestBuy.City,
                BuyPrice  = cheapestBuy.Price,
                BuyVolume = cheapestBuy.Volume,

                InstantSellCity     = bestInstant.City ?? "",
                InstantSellPrice    = instantProfit > 0 ? bestInstant.Price  : 0,
                InstantSellOrderRef = instantProfit > 0 ? instantOrderRef    : 0,
                InstantSellVolume   = instantProfit > 0 ? bestInstant.Volume : 0,
                InstantProfit       = instantProfit > 0 ? instantProfit      : 0,
                InstantProfitPct    = instantProfit > 0 ? instantProfitPct   : 0,

                OrderSellCity       = bestOrderDest.City ?? "",
                OrderSellPrice      = orderProfit > 0 ? bestOrderDest.Price  : 0,
                OrderSellInstantRef = orderProfit > 0 ? orderInstantRef      : 0,
                OrderSellVolume     = orderProfit > 0 ? bestOrderDest.Volume : 0,
                OrderProfit         = orderProfit > 0 ? orderProfit          : 0,
                OrderProfitPct      = orderProfit > 0 ? orderProfitPct       : 0,
            });
        }

        return result;
    }

    private void ApplyFlipFilters()
    {
        double minProfit = double.TryParse(MinFlipProfitInput.Text, out var mp) ? mp : 1000;
        var opps = _rawFlipResults
            .Where(o => o.Profit >= minProfit)
            .Where(o => _flipOrigin == null || o.BuyCity  == _flipOrigin)
            .Where(o => _flipDest   == null || o.InstantSellCity == _flipDest || o.OrderSellCity == _flipDest)
            .OrderByDescending(o => o.Profit)
            .Take(60)
            .ToList();

        FlipList.ItemsSource = opps;
        FlipCountText.Text   = $"{opps.Count} resultados";
        SaveFlipSettings();
    }

    private void FlipProfitFilter_Changed(object? sender, Avalonia.Controls.TextChangedEventArgs e) =>
        ApplyFlipFilters();

    private void FlipPremium_Changed(object? sender, RoutedEventArgs e)
    {
        _flipPremium = PremiumCheckBox.IsChecked == true;
        RebuildFromRawScanData();
    }

    private void FlipTransport_Changed(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        _flipTransportPct = double.TryParse(TransportPctInput.Text, out var v) ? Math.Max(0, v) : 0;
        RebuildFromRawScanData();
    }

    private void RebuildFromRawScanData()
    {
        if (_rawScanData.Count > 0)
        {
            _rawFlipResults = BuildFlipOpportunities(
                _rawScanData, _flipPremium ? 0.04 : 0.08, _flipTransportPct);
            ApplyFlipFilters();
        }
        else
            SaveFlipSettings();
    }

    private void SaveFlipSettings()
    {
        var app = Application.Current as App;
        if (app == null) return;
        app.Settings.FlipPremium      = _flipPremium;
        app.Settings.FlipOrigin       = _flipOrigin;
        app.Settings.FlipDest         = _flipDest;
        app.Settings.FlipCategory     = _flipCategory;
        app.Settings.FlipMinProfit    = double.TryParse(MinFlipProfitInput.Text, out var mp) ? mp : 1000;
        app.Settings.FlipTransportPct = _flipTransportPct;
        app.Settings.CraftPremium     = _craftPremium;
        app.Settings.Save();
    }

    private void ApplyPersistedFlipSettings()
    {
        var s = (Application.Current as App)?.Settings;
        if (s == null) return;

        _flipPremium      = s.FlipPremium;
        _flipOrigin       = s.FlipOrigin;
        _flipDest         = s.FlipDest;
        _flipCategory     = s.FlipCategory;
        _flipTransportPct = s.FlipTransportPct;
        _craftPremium     = s.CraftPremium;

        PremiumCheckBox.IsChecked     = _flipPremium;
        MinFlipProfitInput.Text       = s.FlipMinProfit.ToString("0");
        TransportPctInput.Text        = _flipTransportPct > 0 ? _flipTransportPct.ToString("0.#") : "";

        // Highlight category button
        if (_flipCategoryBtns != null)
        {
            var ci = Array.IndexOf(FlipCategories, _flipCategory);
            if (ci >= 0) HighlightCityBtn(_flipCategoryBtns, _flipCategoryBtns[ci]);
        }

        // Highlight origin button
        if (_flipOriginBtns != null && _flipOrigin != null)
        {
            var oi = Array.IndexOf(FlipCityApiNames, _flipOrigin);
            if (oi >= 0) HighlightCityBtn(_flipOriginBtns, _flipOriginBtns[oi]);
        }

        // Highlight dest button
        if (_flipDestBtns != null && _flipDest != null)
        {
            var di = Array.IndexOf(FlipCityApiNames, _flipDest);
            if (di >= 0) HighlightCityBtn(_flipDestBtns, _flipDestBtns[di]);
        }
    }

    private void InitFlipCitySelectors()
    {
        _flipOriginBtns = FlipCities.Select(_ => MakeCityFilterBtn()).ToArray();
        _flipDestBtns   = FlipCities.Select(_ => MakeCityFilterBtn()).ToArray();

        for (var i = 0; i < FlipCities.Length; i++)
        {
            var label   = FlipCities[i];
            var apiName = FlipCityApiNames[i];   // null for "Todos"
            var origBtn = _flipOriginBtns[i];
            var destBtn = _flipDestBtns[i];

            origBtn.Content = label;
            origBtn.Click  += (_, _) =>
            {
                _flipOrigin = apiName;
                HighlightCityBtn(_flipOriginBtns, origBtn);
                ApplyFlipFilters();
            };

            destBtn.Content = label;
            destBtn.Click  += (_, _) =>
            {
                _flipDest = apiName;
                HighlightCityBtn(_flipDestBtns, destBtn);
                ApplyFlipFilters();
            };

            FlipOriginPanel.Children.Add(origBtn);
            FlipDestPanel.Children.Add(destBtn);
        }

        HighlightCityBtn(_flipOriginBtns, _flipOriginBtns[0]);
        HighlightCityBtn(_flipDestBtns,   _flipDestBtns[0]);
    }

    private void InitFlipCategorySelectors()
    {
        _flipCategoryBtns = FlipCategories.Select(_ => MakeCityFilterBtn()).ToArray();

        for (var i = 0; i < FlipCategories.Length; i++)
        {
            var cat = FlipCategories[i];
            var btn = _flipCategoryBtns[i];
            btn.Content = cat;
            btn.Click  += (_, _) =>
            {
                _flipCategory = cat;
                HighlightCityBtn(_flipCategoryBtns, btn);
                SaveFlipSettings();
                // Category change requires a new scan to take effect
                _ = ScanFlipOpportunitiesAsync();
            };
            FlipCategoryPanel.Children.Add(btn);
        }

        HighlightCityBtn(_flipCategoryBtns, _flipCategoryBtns[0]);
    }

    // ── Flip journal ─────────────────────────────────────────────────────────

    private void FlipJournalToggle_Click(object? sender, RoutedEventArgs e)
    {
        _flipJournalExpanded = !_flipJournalExpanded;
        FlipJournalBody.IsVisible    = _flipJournalExpanded;
        FlipJournalToggleBtn.Content = _flipJournalExpanded ? "▲" : "▼";
        if (_flipJournalExpanded) RefreshFlipJournalUI();
    }

    private void FlipJournalClear_Click(object? sender, RoutedEventArgs e)
    {
        _flipJournal.Clear();
        RefreshFlipJournalUI();
    }

    private void RefreshFlipJournalUI()
    {
        if (!_flipJournalExpanded) return;
        FlipJournalList.ItemsSource = null;
        FlipJournalList.ItemsSource = _flipJournal.Entries;
        var total = _flipJournal.TotalProfit;
        FlipJournalTotalText.Text = _flipJournal.Entries.Count > 0
            ? $"{_flipJournal.Entries.Count} trades · total esperado: {(total >= 0 ? "+" : "")}{total:N0}s"
            : "Sin registros aún";
    }

    // ── Crafting premium ──────────────────────────────────────────────────────

    private void CraftPremium_Click(object? sender, RoutedEventArgs e)
    {
        _craftPremium = !_craftPremium;
        CraftPremiumBtn.Content    = _craftPremium ? "Sí" : "No";
        CraftPremiumBtn.Background = _craftPremium
            ? new SolidColorBrush(Color.FromRgb(255, 117, 80))
            : Brushes.Transparent;
        CraftPremiumBtn.Foreground = _craftPremium
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(102, 102, 102));
        CraftTaxLabel.Text = _craftPremium ? "Impuesto (4%):" : "Impuesto (8%):";
        RefreshCraftSummary();
        SaveFlipSettings();
    }

    private static string CityShort(string city) => city switch
    {
        "Fort Sterling" => "Fort St.",
        "Bridgewatch"   => "Bridge.",
        "Brecilien"     => "Brecil.",
        _               => city,
    };

    private static Button MakeCityFilterBtn() => new()
    {
        FontSize        = 8,
        Height          = 16,
        Padding         = new Thickness(4, 0),
        MinHeight       = 0,
        Margin          = new Thickness(0, 0, 2, 2),
        Background      = new SolidColorBrush(Color.FromRgb(30, 30, 35)),
        Foreground      = new SolidColorBrush(Color.FromRgb(120, 120, 130)),
        BorderThickness = new Thickness(1),
        BorderBrush     = new SolidColorBrush(Color.FromRgb(50, 50, 55)),
        CornerRadius    = new CornerRadius(3),
    };

    private static void HighlightCityBtn(Button[] btns, Button active)
    {
        foreach (var b in btns)
        {
            var isActive = b == active;
            b.Background = new SolidColorBrush(isActive
                ? Color.FromRgb(55, 55, 90)
                : Color.FromRgb(30, 30, 35));
            b.Foreground = new SolidColorBrush(isActive
                ? Color.FromRgb(180, 180, 230)
                : Color.FromRgb(120, 120, 130));
        }
    }

    // ── Gold price ───────────────────────────────────────────────────────────

    private async Task LoadGoldPriceAsync()
    {
        var gold = await _apiService.GetGoldPriceAsync();
        if (gold != null)
        {
            GoldPriceText.Text      = $"{gold.Price:N0}";
            GoldPriceRow.IsVisible  = true;
        }
    }

    // ── History & Favorites UI ────────────────────────────────────────────────

    private void RefreshHistoryUI()
    {
        var hist = (Application.Current as App)?.HistoryService;
        if (hist == null) return;

        RecentSearchChips.Children.Clear();
        foreach (var entry in hist.RecentItems)
        {
            var e = entry;
            var chip = MakeChip(e.ItemName, async () => await CheckItemById(e.ItemId, e.ItemName));
            RecentSearchChips.Children.Add(chip);
        }
        RecentSearchesSection.IsVisible = hist.RecentItems.Count > 0;

        FavoriteChips.Children.Clear();
        foreach (var entry in hist.Favorites)
        {
            var e = entry;
            var chip = MakeChip($"★ {e.ItemName}", async () => await CheckItemById(e.ItemId, e.ItemName), starred: true);
            FavoriteChips.Children.Add(chip);
        }
        FavoritesSection.IsVisible = hist.Favorites.Count > 0;
    }

    private void RefreshSearchSuggestions()
    {
        if (_suppressSuggestionRefresh || !_dbLoaded || _itemDatabase.ItemCount == 0)
        {
            HideSearchSuggestions();
            return;
        }

        var query = ItemInput.Text ?? "";
        if (string.IsNullOrWhiteSpace(query))
        {
            HideSearchSuggestions();
            return;
        }

        var normalizedQuery = NormalizeForSearch(query);
        if (normalizedQuery.Length < 2)
        {
            HideSearchSuggestions();
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var suggestions = new List<SearchSuggestion>();
        var hist = (Application.Current as App)?.HistoryService;

        void AddHistoryMatches(IEnumerable<HistoryEntry> entries, string badge, bool starred)
        {
            foreach (var entry in entries)
            {
                var name = NormalizeForSearch(entry.ItemName);
                var id = NormalizeForSearch(entry.ItemId);
                if (!name.Contains(normalizedQuery) && !id.Contains(normalizedQuery)) continue;
                if (!seen.Add(entry.ItemId)) continue;
                suggestions.Add(new SearchSuggestion(entry.ItemId, entry.ItemName, badge, starred));
                if (suggestions.Count >= 6) return;
            }
        }

        if (hist != null)
        {
            AddHistoryMatches(hist.Favorites, "Favorito", starred: true);
            AddHistoryMatches(hist.RecentItems, "Reciente", starred: false);
        }

        foreach (var result in _itemDatabase.SearchDetailed(query, 8))
        {
            if (!seen.Add(result.ItemId)) continue;
            var badge = IsTieredItemId(result.ItemId)
                ? "Elegir tier y encantamiento"
                : result.EnglishName != null && result.EnglishName != result.Name
                    ? result.EnglishName
                    : result.ItemId;
            suggestions.Add(new SearchSuggestion(result.ItemId, result.Name, badge, Starred: false));
            if (suggestions.Count >= 8) break;
        }

        _activeSuggestions.Clear();
        _activeSuggestions.AddRange(suggestions);
        _selectedSuggestionIndex = _activeSuggestions.Count > 0 ? 0 : -1;
        RebuildSearchSuggestionRows();
    }

    private void RebuildSearchSuggestionRows()
    {
        SearchSuggestionRows.Children.Clear();
        SearchSuggestionsPanel.IsVisible = _activeSuggestions.Count > 0;
        if (_activeSuggestions.Count == 0) return;

        for (var i = 0; i < _activeSuggestions.Count; i++)
        {
            var index = i;
            var item = _activeSuggestions[i];
            SearchSuggestionRows.Children.Add(MakeSuggestionButton(item, i == _selectedSuggestionIndex, async () =>
            {
                await ActivateSearchSuggestion(index);
            }));
        }
    }

    private static Button MakeSuggestionButton(SearchSuggestion item, bool selected, Func<Task> onClick)
    {
        var title = new TextBlock
        {
            Text = item.Starred ? $"* {item.ItemName}" : item.ItemName,
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var badge = new TextBlock
        {
            Text = item.Badge,
            Foreground = item.Starred
                ? new SolidColorBrush(Color.FromRgb(255, 215, 0))
                : new SolidColorBrush(Color.FromRgb(135, 135, 140)),
            FontSize = 8,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var content = new StackPanel { Spacing = 1 };
        content.Children.Add(title);
        content.Children.Add(badge);

        var btn = new Button
        {
            Content = content,
            Padding = new Thickness(8, 5),
            MinHeight = 0,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Background = selected
                ? new SolidColorBrush(Color.FromRgb(45, 38, 36))
                : Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(255, 117, 80))
                : Brushes.Transparent,
        };
        btn.Click += async (_, _) => await onClick();
        return btn;
    }

    private async Task ActivateSearchSuggestion(int index)
    {
        if (index < 0 || index >= _activeSuggestions.Count) return;
        var suggestion = _activeSuggestions[index];
        _suppressSuggestionRefresh = true;
        ItemInput.Text = suggestion.ItemName;
        _suppressSuggestionRefresh = false;
        HideSearchSuggestions();
        await CheckItemById(suggestion.ItemId, suggestion.ItemName);
    }

    private void HideSearchSuggestions()
    {
        _activeSuggestions.Clear();
        _selectedSuggestionIndex = -1;
        SearchSuggestionRows.Children.Clear();
        SearchSuggestionsPanel.IsVisible = false;
    }

    private static string NormalizeForSearch(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var formD = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
                System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString()
            .Replace("'", "").Replace(".", "").Replace(",", "")
            .Replace("(", "").Replace(")", "").Replace("-", " ").Replace("_", " ")
            .ToLowerInvariant()
            .Trim();
    }

    private static bool IsTieredItemId(string itemId) =>
        itemId.Length >= 3 && itemId[0] == 'T' && char.IsDigit(itemId[1]) && itemId[2] == '_';

    private static Button MakeChip(string label, Func<Task> onClick, bool starred = false)
    {
        var btn = new Button
        {
            Content         = label,
            FontSize        = 9,
            Height          = 20,
            Padding         = new Thickness(7, 0, 7, 0),
            MinHeight       = 0,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment   = Avalonia.Layout.VerticalAlignment.Center,
            Margin          = new Thickness(0, 0, 4, 4),
            Background      = new SolidColorBrush(Color.FromRgb(30, 30, 35)),
            Foreground      = starred
                ? new SolidColorBrush(Color.FromRgb(255, 215, 0))
                : new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            BorderThickness = new Thickness(1),
            BorderBrush     = starred
                ? new SolidColorBrush(Color.FromArgb(80, 255, 215, 0))
                : new SolidColorBrush(Color.FromRgb(50, 50, 55)),
        };
        btn.Click += async (_, _) => await onClick();
        return btn;
    }

    private void ClearHistory_Click(object? sender, RoutedEventArgs e)
    {
        (Application.Current as App)?.HistoryService?.ClearHistory();
        RefreshHistoryUI();
    }

    private void FavoriteBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentItemId == null || _currentItemName == null) return;
        var hist = (Application.Current as App)?.HistoryService;
        if (hist == null) return;
        var isFav = hist.ToggleFavorite(_currentItemId, _currentItemName);
        FavoriteBtn.Content = isFav ? "★" : "☆";
        RefreshHistoryUI();
    }

    // ── NATS real-time ───────────────────────────────────────────────────────

    private void OnRealtimePriceUpdated(object? sender, PriceApiResponse msg)
    {
        if (msg.City is null) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            if (!_cityViewModels.TryGetValue(msg.City, out var vm)) return;
            if (msg.SellPriceMin > 0) { vm.BuyAt = msg.SellPriceMin.Value; vm.BuyAtDate = msg.SellPriceMinDate ?? DateTime.UtcNow; }
            if (msg.BuyPriceMax  > 0) { vm.SellAt = msg.BuyPriceMax.Value; vm.SellAtDate = msg.BuyPriceMaxDate ?? DateTime.UtcNow; }
        });
    }

    private void OnRealtimeConnectionChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            LiveDot.Fill = connected
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
                : new SolidColorBrush(Color.FromRgb(85, 85, 85));
            ToolTip.SetTip(LiveDot, connected ? "En vivo — datos en tiempo real" : "Sin conexión en tiempo real");
        });
    }

    // ── Price mode ───────────────────────────────────────────────────────────

    private void DisplayPriceInfo(ItemPriceSummary summary, bool setupTierEnchant = true)
    {
        StatusText.IsVisible    = false;
        ItemInfoPanel.IsVisible       = true;
        QualityComparePanel.IsVisible = false;
        SelectPriceInfoTab("prices");
        ForceResizeWindow();

        ItemNameText.Text = summary.ItemName;
        ItemIdText.Text   = summary.ItemId;
        _currentItemId    = summary.ItemId;
        _currentItemName  = summary.ItemName;

        var hist = (Application.Current as App)?.HistoryService;
        FavoriteBtn.Content = hist?.IsFavorite(summary.ItemId) == true ? "★" : "☆";

        ItemIcon.Source = null;
        _ = LoadImageFromUrlAsync(ItemIcon, GameInfoService.GetItemIconUrl(summary.ItemId));

        if (setupTierEnchant)
        {
            SetupTierEnchant(summary.ItemId);
            SetupQualityButtons(summary.ItemId);
        }
        DisplayItemStats(summary.ItemId, summary.ItemName);

        var bestBuy = summary.BestBuyCity;
        if (bestBuy != null)
        {
            BestBuyCityText.Text   = bestBuy.City;
            BestBuyPriceText.Text  = $"{bestBuy.BuyAt:N0}";
            BestBuyVolumeText.Text = bestBuy.SellAmount > 0 ? $"×{bestBuy.SellAmount} órd." : "";
            _currentBestBuyPrice   = bestBuy.BuyAt;
        }

        var bestSell = summary.BestSellOrderCity;
        if (bestSell != null)
        {
            BestSellCityText.Text   = bestSell.City;
            BestSellPriceText.Text  = $"{bestSell.BuyAt:N0}";
            BestSellVolumeText.Text = bestSell.SellAmount > 0 ? $"×{bestSell.SellAmount} órd." : "";
        }

        var bestInstant = summary.BestInstantSellCity;
        if (bestInstant != null)
        {
            BestInstantCityText.Text   = bestInstant.City;
            BestInstantPriceText.Text  = $"{bestInstant.SellAt:N0}";
            BestInstantVolumeText.Text = bestInstant.BuyAmount > 0 ? $"×{bestInstant.BuyAmount} órd." : "";
        }

        var vms = summary.Prices.Select(p => new CityPriceViewModel
        {
            City       = p.City,
            BuyAt      = p.BuyAt,
            BuyAtDate  = p.BuyAtDate,
            SellAt     = p.SellAt,
            SellAtDate = p.SellAtDate,
            SellAmount = p.SellAmount,
            BuyAmount  = p.BuyAmount,
        }).ToList();

        _cityViewModels.Clear();
        foreach (var vm in vms) _cityViewModels[vm.City] = vm;
        CitiesList.ItemsSource = vms;

        DisplayFlipCalculator(summary);
        (Application.Current as App)?.RealtimeService?.SetItem(summary.ItemId);
    }

    private void PriceInfoTab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender == PriceTabBtn) SelectPriceInfoTab("prices");
        else if (sender == StatsTabBtn) SelectPriceInfoTab("stats");
        else if (sender == CraftingTabBtn) SelectPriceInfoTab("crafting");
    }

    private void SelectPriceInfoTab(string tab)
    {
        _priceInfoTab = tab;
        var showPrices = tab == "prices";
        var showStats = tab == "stats";
        var showCrafting = tab == "crafting";

        PriceSummaryPanel.IsVisible = showPrices;
        FlipCalcPanel.IsVisible = showPrices && !string.IsNullOrWhiteSpace(FlipProfitText.Text);
        PriceHistoryPanel.IsVisible = showPrices && _sparklineData is { Count: > 0 };
        MarketCityPanel.IsVisible = showPrices;
        QualityComparePanel.IsVisible = showPrices && QualityComparePanel.IsVisible;

        StatsPanel.IsVisible = showStats;
        CraftingInfoPanel.IsVisible = showCrafting;

        StylePriceInfoTab(PriceTabBtn, showPrices);
        StylePriceInfoTab(StatsTabBtn, showStats);
        StylePriceInfoTab(CraftingTabBtn, showCrafting);
    }

    private static void StylePriceInfoTab(Button button, bool selected)
    {
        button.Background = selected
            ? new SolidColorBrush(Color.FromRgb(255, 117, 80))
            : Brushes.Transparent;
        button.Foreground = selected
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(154, 166, 178));
        button.FontWeight = selected ? FontWeight.Bold : FontWeight.Normal;
    }

    private void DisplayItemStats(string itemId, string itemName)
    {
        _statsCts?.Cancel();
        _statsCts = new CancellationTokenSource();

        var externalName = _itemDatabase.GetEnglishNameById(itemId) ?? itemName;
        var stats = ItemStatsService.BuildSummary(itemId, externalName, _currentQuality);
        _currentStatsUrl = stats.ExternalUrl;

        StatsSubtitleText.Text = $"{stats.Category} · {stats.Slot} · {stats.ItemPowerLabel}";
        StatsLinesList.ItemsSource = stats.Lines;
        BindStatsSections(stats.Sections);
        StatsExternalBtn.IsVisible = !string.IsNullOrWhiteSpace(stats.ExternalUrl);
        StatsPanel.IsVisible = _priceInfoTab == "stats";
        CraftingInfoPanel.IsVisible = _priceInfoTab == "crafting";

        _ = EnrichItemStatsAsync(itemId, stats, _statsCts.Token);
    }

    private async Task EnrichItemStatsAsync(string itemId, ItemStatsSummary baseStats, CancellationToken ct)
    {
        var enriched = await ItemStatsService.EnrichFromAlbionDatabaseAsync(baseStats, ct);
        if (enriched == null || ct.IsCancellationRequested || _currentItemId != itemId) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_currentItemId != itemId) return;
            _currentStatsUrl = enriched.ExternalUrl;
            StatsSubtitleText.Text = $"{enriched.Category} · {enriched.Slot} · {enriched.ItemPowerLabel}";
            StatsLinesList.ItemsSource = enriched.Lines;
            BindStatsSections(enriched.Sections);
            StatsExternalBtn.IsVisible = !string.IsNullOrWhiteSpace(enriched.ExternalUrl);
        });
    }

    private void BindStatsSections(IEnumerable<ItemStatSection> sections)
    {
        var sectionList = sections.ToList();
        var craftingSections = sectionList
            .Where(s => IsCraftingSection(s.Title))
            .ToList();
        var statSections = sectionList
            .Where(s => !IsCraftingSection(s.Title))
            .ToList();

        StatsSectionsList.ItemsSource = statSections;
        CraftingSectionsList.ItemsSource = craftingSections;
        CraftingEmptyText.IsVisible = craftingSections.Count == 0;
    }

    private static bool IsCraftingSection(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        return title.Contains("craft", StringComparison.OrdinalIgnoreCase)
               || title.Contains("receta", StringComparison.OrdinalIgnoreCase)
               || title.Contains("material", StringComparison.OrdinalIgnoreCase);
    }

    private void StatsExternal_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentStatsUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_currentStatsUrl) { UseShellExecute = true });
        }
        catch { }
    }

    // ── Quality comparison ───────────────────────────────────────────────────

    private void CompareQualities_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentItemId == null) return;
        QualityComparePanel.IsVisible = true;
        QCBuyNormal.Text = QCBuyGood.Text = QCBuyOut.Text   = "…";
        QCSellNormal.Text = QCSellGood.Text = QCSellOut.Text = "";
        _ = LoadAllQualitiesAsync(_currentItemId);
    }

    private void QualityCompareClose_Click(object? sender, RoutedEventArgs e) =>
        QualityComparePanel.IsVisible = false;

    private async Task LoadAllQualitiesAsync(string itemId)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var tasks = new[]
        {
            _apiService.GetItemPriceAsync(itemId, 1, cts.Token),
            _apiService.GetItemPriceAsync(itemId, 2, cts.Token),
            _apiService.GetItemPriceAsync(itemId, 3, cts.Token),
        };
        await Task.WhenAll(tasks);

        void Fill(TextBlock buy, TextBlock sell, ItemPriceSummary? s)
        {
            buy.Text  = s?.BestBuyCity     != null ? $"{s.BestBuyCity.BuyAt:N0}" : "—";
            sell.Text = s?.BestInstantSellCity != null ? $"{s.BestInstantSellCity.SellAt:N0}" : "";
        }

        Dispatcher.UIThread.Invoke(() =>
        {
            if (QualityComparePanel.IsVisible && _currentItemId == itemId)
            {
                Fill(QCBuyNormal, QCSellNormal, tasks[0].Result);
                Fill(QCBuyGood,   QCSellGood,   tasks[1].Result);
                Fill(QCBuyOut,    QCSellOut,     tasks[2].Result);
            }
        });
    }

    // ── Flip calculator ──────────────────────────────────────────────────────

    private void DisplayFlipCalculator(ItemPriceSummary summary)
    {
        // Buy at cheapest sell order; sell by listing at or below highest sell order in another city
        var buyCity  = summary.BestBuyCity;
        var sellCity = summary.BestSellOrderCity;

        if (buyCity == null || sellCity == null || buyCity.BuyAt <= 0 || sellCity.BuyAt <= 0
            || buyCity.City == sellCity.City)
        {
            FlipProfitText.Text = "";
            FlipCalcPanel.IsVisible = false;
            return;
        }

        var profit = sellCity.BuyAt * 0.92 - buyCity.BuyAt;
        if (profit <= 0 || profit / buyCity.BuyAt < 0.02)
        {
            FlipProfitText.Text = "";
            FlipCalcPanel.IsVisible = false;
            return;
        }

        var margin = profit / buyCity.BuyAt * 100;
        FlipRouteText.Text  = $"{buyCity.City}  →  {sellCity.City}";
        FlipProfitText.Text = $"+{profit:N0}";
        FlipMarginText.Text = $"Margen: {margin:F1}%  (comprás {buyCity.BuyAt:N0}, vendés {sellCity.BuyAt:N0}, tax 8%)";
        FlipCalcPanel.IsVisible = _priceInfoTab == "prices";
    }

    // ── Price history sparkline ───────────────────────────────────────────────

    private async Task LoadPriceHistoryAsync(string itemId, int quality, CancellationToken ct)
    {
        var history = await _apiService.GetPriceHistoryAsync(itemId, quality, 24, ct);
        if (ct.IsCancellationRequested || history == null || history.Count == 0)
        {
            Dispatcher.UIThread.Invoke(() => { PriceHistoryPanel.IsVisible = false; _sparklineData = null; });
            return;
        }

        var best = history.Where(h => h.Data?.Count > 3).OrderByDescending(h => h.Data!.Count).FirstOrDefault();
        if (best?.Data == null)
        {
            Dispatcher.UIThread.Invoke(() => { PriceHistoryPanel.IsVisible = false; _sparklineData = null; });
            return;
        }

        var points = best.Data.OrderBy(p => p.Timestamp).TakeLast(14).ToList();

        Dispatcher.UIThread.Invoke(() =>
        {
            SparklineTitleText.Text       = $"HISTORIAL 14 DÍAS — {best.City}";
            _sparklineData                = points;
            PriceHistoryPanel.IsVisible   = _priceInfoTab == "prices";
            DrawSparkline(points);
        });
    }

    private void DrawSparkline(List<PriceHistoryPoint> points)
    {
        SparklineCanvas.Children.Clear();
        if (points.Count < 2) return;

        double w = SparklineCanvas.Bounds.Width  > 10 ? SparklineCanvas.Bounds.Width  : 370;
        double h = SparklineCanvas.Bounds.Height > 10 ? SparklineCanvas.Bounds.Height : 50;

        var prices = points.Select(p => (double)p.AvgPrice).ToList();
        var min    = prices.Min();
        var max    = prices.Max();
        var range  = max - min;
        if (range == 0) range = 1;

        var fillPoints = new List<Point>
        {
            new(2, h),
        };
        for (int i = 0; i < prices.Count; i++)
        {
            var x = 2 + i * (w - 4) / (prices.Count - 1);
            var y = h - 4 - (prices[i] - min) / range * (h - 8);
            fillPoints.Add(new Point(x, y));
        }
        fillPoints.Add(new Point(w - 2, h));

        var fill = new Polygon
        {
            Points          = fillPoints,
            Fill            = new SolidColorBrush(Color.FromArgb(30, 255, 117, 80)),
            StrokeThickness = 0,
        };
        SparklineCanvas.Children.Add(fill);

        var linePoints = new List<Point>();
        for (int i = 0; i < prices.Count; i++)
        {
            var x = 2 + i * (w - 4) / (prices.Count - 1);
            var y = h - 4 - (prices[i] - min) / range * (h - 8);
            linePoints.Add(new Point(x, y));
        }
        var line = new Polyline
        {
            Stroke          = new SolidColorBrush(Color.FromRgb(255, 117, 80)),
            StrokeThickness = 1.5,
            Points          = linePoints,
        };
        SparklineCanvas.Children.Add(line);

        var minLabel = new TextBlock { Text = FormatFame((long)min), Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)), FontSize = 7 };
        Canvas.SetLeft(minLabel, 3);
        Canvas.SetBottom(minLabel, 1);
        SparklineCanvas.Children.Add(minLabel);

        var maxLabel = new TextBlock { Text = FormatFame((long)max), Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)), FontSize = 7 };
        Canvas.SetLeft(maxLabel, 3);
        Canvas.SetTop(maxLabel, 1);
        SparklineCanvas.Children.Add(maxLabel);
    }

    // ── Tier / Enchantment ───────────────────────────────────────────────────

    private string BuildItemId() =>
        _currentEnchant > 0 ? $"T{_currentTier}_{_baseId}@{_currentEnchant}" : $"T{_currentTier}_{_baseId}";

    private void SetupTierEnchant(string uniqueName)
    {
        var m = TieredItemRegex.Match(uniqueName);
        if (!m.Success) { TierEnchantPanel.IsVisible = false; return; }

        _currentTier    = int.Parse(m.Groups[1].Value);
        _baseId         = m.Groups[2].Value;
        _currentEnchant = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;

        _variants.Clear();
        foreach (var v in _itemDatabase.GetVariants(_baseId))
        {
            var vm = TieredItemRegex.Match(v);
            if (!vm.Success) continue;
            var t   = int.Parse(vm.Groups[1].Value);
            var enc = vm.Groups[3].Success ? int.Parse(vm.Groups[3].Value) : 0;
            if (!_variants.ContainsKey(t)) _variants[t] = new List<int>();
            if (!_variants[t].Contains(enc)) _variants[t].Add(enc);
        }
        foreach (var k in _variants.Keys) _variants[k].Sort();

        if (_variants.Count == 0) { TierEnchantPanel.IsVisible = false; return; }

        RebuildTierButtons();
        RebuildEnchantButtons();
        TierEnchantPanel.IsVisible = true;
    }

    private void RebuildTierButtons()
    {
        var tiers = _variants.Keys.OrderBy(t => t).Select(t => $"T{t}").ToArray();
        PopulateButtons(TierButtonsPanel, tiers, $"T{_currentTier}", async label =>
        {
            var newTier = int.Parse(label[1..]);
            if (newTier == _currentTier || _isLoading) return;
            _currentTier = newTier;
            if (_variants.TryGetValue(_currentTier, out var enchants) && !enchants.Contains(_currentEnchant))
                _currentEnchant = enchants.FirstOrDefault();
            RefreshButtonSelection(TierButtonsPanel, $"T{_currentTier}");
            RebuildEnchantButtons();
            await RefetchCurrentItem();
        });
    }

    private void RebuildEnchantButtons()
    {
        var enchants = _variants.TryGetValue(_currentTier, out var list)
            ? list.Select(e => $".{e}").ToArray()
            : Array.Empty<string>();
        PopulateButtons(EnchantButtonsPanel, enchants, $".{_currentEnchant}", async label =>
        {
            var newEnchant = int.Parse(label[1..]);
            if (newEnchant == _currentEnchant || _isLoading) return;
            _currentEnchant = newEnchant;
            RefreshButtonSelection(EnchantButtonsPanel, $".{_currentEnchant}");
            await RefetchCurrentItem();
        });
    }

    private static readonly string[] QualityLabels = ["Normal", "Bueno", "Sobresaliente", "Excelente", "Obra M."];

    private static bool IsConsumable(string itemId)
    {
        var u = itemId.ToUpperInvariant();
        return u.Contains("_POTION") || u.Contains("_MEAL") || u.Contains("FISHINGBAIT");
    }

    private void SetupQualityButtons(string itemId)
    {
        _currentQuality = 1;
        if (IsConsumable(itemId) || !TieredItemRegex.IsMatch(itemId))
        {
            QualityPanel.IsVisible = false;
            return;
        }

        QualityButtonsPanel.Children.Clear();
        for (var q = 1; q <= 5; q++)
        {
            var quality    = q;
            var isSelected = quality == 1;
            var btn = new Button
            {
                Content = QualityLabels[q - 1],
                Height = 22, Padding = new Thickness(6, 0), MinHeight = 0,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment   = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0), FontSize = 9, BorderThickness = new Thickness(1),
                Background  = isSelected ? new SolidColorBrush(Color.FromRgb(255, 117, 80)) : new SolidColorBrush(Color.FromRgb(25, 25, 30)),
                Foreground  = isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(255, 117, 80)) : new SolidColorBrush(Color.FromRgb(60, 60, 65)),
            };
            btn.Click += async (_, _) =>
            {
                if (quality == _currentQuality || _isLoading) return;
                _currentQuality = quality;
                RefreshButtonSelection(QualityButtonsPanel, QualityLabels[quality - 1]);
                await RefetchCurrentItem();
            };
            QualityButtonsPanel.Children.Add(btn);
        }
        QualityPanel.IsVisible = true;
    }

    private static void PopulateButtons(WrapPanel panel, string[] labels, string selected, Func<string, Task> onClick)
    {
        panel.Children.Clear();
        foreach (var label in labels)
        {
            var isSelected = label == selected;
            var btn = new Button
            {
                Content = label, Width = 30, Height = 22,
                Padding = new Thickness(2, 0), MinHeight = 0,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment   = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0), FontSize = 10, BorderThickness = new Thickness(1),
                Background  = isSelected ? new SolidColorBrush(Color.FromRgb(255, 117, 80)) : new SolidColorBrush(Color.FromRgb(25, 25, 30)),
                Foreground  = isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(255, 117, 80)) : new SolidColorBrush(Color.FromRgb(60, 60, 65)),
            };
            var capturedLabel = label;
            btn.Click += async (_, _) => await onClick(capturedLabel);
            panel.Children.Add(btn);
        }
    }

    private static void RefreshButtonSelection(WrapPanel panel, string selected)
    {
        foreach (Button btn in panel.Children)
        {
            var isSelected = btn.Content?.ToString() == selected;
            btn.Background  = isSelected ? new SolidColorBrush(Color.FromRgb(255, 117, 80)) : new SolidColorBrush(Color.FromRgb(25, 25, 30));
            btn.Foreground  = isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(140, 140, 140));
            btn.BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(255, 117, 80)) : new SolidColorBrush(Color.FromRgb(60, 60, 65));
        }
    }

    private async Task RefetchCurrentItem()
    {
        var itemId = BuildItemId();
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _isLoading = true;
        try
        {
            StatusText.Text       = "Actualizando...";
            StatusText.IsVisible  = true;
            ErrorText.IsVisible   = false;
            BestBuyCityText.Text = BestBuyPriceText.Text = BestSellCityText.Text = BestSellPriceText.Text = BestInstantCityText.Text = BestInstantPriceText.Text = "…";
            BestBuyVolumeText.Text = BestSellVolumeText.Text = BestInstantVolumeText.Text = "";
            CitiesList.ItemsSource = null;

            var ct      = _cts.Token;
            var summary = await _apiService.GetItemPriceAsync(itemId, _currentQuality, ct);
            StatusText.IsVisible = false;

            if (summary == null || summary.Prices.Count == 0)
            {
                ErrorText.Text      = $"Sin datos para: {itemId}";
                ErrorText.IsVisible = true;
                BestBuyCityText.Text = BestBuyPriceText.Text = BestSellCityText.Text = BestSellPriceText.Text = BestInstantCityText.Text = BestInstantPriceText.Text = "—";
                BestBuyVolumeText.Text = BestSellVolumeText.Text = BestInstantVolumeText.Text = "";
                return;
            }

            summary.ItemName = _itemDatabase.GetNameById(itemId) ?? ItemNameText.Text;
            DisplayPriceInfo(summary, setupTierEnchant: false);
            _ = LoadPriceHistoryAsync(itemId, _currentQuality, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText.IsVisible = false;
            ErrorText.Text      = $"Error: {ex.Message}";
            ErrorText.IsVisible = true;
        }
        finally { _isLoading = false; }
    }

    // ── Player mode ──────────────────────────────────────────────────────────

    private async void PlayerSearchButton_Click(object? sender, RoutedEventArgs e) =>
        await SearchPlayerAsync(PlayerInput.Text ?? "");

    private async void PlayerInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return || e.Key == Key.Enter)
            await SearchPlayerAsync(PlayerInput.Text ?? "");
    }

    private async Task SearchPlayerAsync(string name)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var svc = (Application.Current as App)?.GameInfoService;
        if (svc == null) return;

        _playerCts?.Cancel();
        _playerCts = new CancellationTokenSource();

        PlayerCard.IsVisible        = false;
        PlayerResultsList.IsVisible = false;
        PlayerErrorText.IsVisible   = false;
        PlayerStatusText.Text       = "Buscando...";
        PlayerStatusText.IsVisible  = true;

        try
        {
            var result = await svc.SearchAsync(name, _playerCts.Token, attempt =>
                Dispatcher.UIThread.Invoke(() => PlayerStatusText.Text = $"Reintentando... ({attempt}/2)"));
            PlayerStatusText.IsVisible = false;

            var players = result?.Players ?? [];
            if (players.Count == 0)
            {
                PlayerErrorText.Text      = $"No se encontró: {name}";
                PlayerErrorText.IsVisible = true;
                return;
            }

            players = players
                .OrderByDescending(p => string.Equals(p.Name, name, StringComparison.Ordinal))
                .ThenByDescending(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(p => (p.KillFame + p.DeathFame) + (string.IsNullOrWhiteSpace(p.GuildName) ? 0 : 1_000_000))
                .ToList();

            if (players.Count == 1 && players[0].Id != null)
            {
                await LoadPlayerDetailsAsync(players[0].Id!, players[0].Name ?? name);
            }
            else
            {
                PlayerResultsList.ItemsSource = players.Take(8).ToList();
                PlayerResultsList.IsVisible   = true;
                PlayerStatusText.Text         = "Elegir el jugador correcto";
                PlayerStatusText.IsVisible    = true;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            PlayerStatusText.IsVisible = false;
            PlayerErrorText.Text       = $"Error: {ex.Message}";
            PlayerErrorText.IsVisible  = true;
        }
    }

    private async void PlayerResultsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PlayerResultsList.SelectedItem is PlayerSearchEntry entry && entry.Id != null)
            await LoadPlayerDetailsAsync(entry.Id, entry.Name ?? "");
    }

    private async Task LoadPlayerDetailsAsync(string playerId, string playerName)
    {
        var svc = (Application.Current as App)?.GameInfoService;
        if (svc == null) return;

        PlayerResultsList.IsVisible = false;
        PlayerStatusText.Text       = "Cargando perfil...";
        PlayerStatusText.IsVisible  = true;

        try
        {
            var ct     = _playerCts?.Token ?? default;
            var player = await svc.GetPlayerAsync(playerId, ct);
            PlayerStatusText.IsVisible = false;

            if (player == null)
            {
                PlayerErrorText.Text      = "No se pudo cargar el perfil.";
                PlayerErrorText.IsVisible = true;
                return;
            }

            DisplayPlayerInfo(player, playerName, null, null);

            using var killsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            killsCts.CancelAfter(TimeSpan.FromSeconds(8));

            var killsTask  = svc.GetPlayerKillsAsync(playerId, killsCts.Token);
            var deathsTask = svc.GetPlayerDeathsAsync(playerId, killsCts.Token);
            await Task.WhenAll(killsTask, deathsTask);

            UpdateEquipmentAndDeaths(player, await killsTask, await deathsTask);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            PlayerStatusText.IsVisible = false;
            PlayerErrorText.Text       = $"Error: {ex.Message}";
            PlayerErrorText.IsVisible  = true;
        }
    }

    private void UpdateEquipmentAndDeaths(PlayerInfo player, List<KillEvent>? kills, List<KillEvent>? deaths)
    {
        var allEvents = new List<KillEvent>();
        if (kills  != null) allEvents.AddRange(kills);
        if (deaths != null) allEvents.AddRange(deaths);

        var lastEvent = allEvents
            .Where(k => k.TimeStamp.HasValue &&
                (string.Equals(k.Killer?.Id, player.Id, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(k.Victim?.Id,  player.Id, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(k => k.TimeStamp)
            .FirstOrDefault();

        if (lastEvent != null)
        {
            var isKiller  = string.Equals(lastEvent.Killer?.Id, player.Id, StringComparison.OrdinalIgnoreCase);
            var participant = isKiller ? lastEvent.Killer : lastEvent.Victim;
            var equipment = participant?.Equipment;
            SetPlayerIp(participant?.AverageItemPower ?? player.AverageItemPower);
            DisplayEquipment(equipment, lastEvent.TimeStamp);
        }
        else
        {
            LastEquipPanel.IsVisible = false;
        }

        DisplayKills(player.Id, kills);
        DisplayDeaths(deaths);
    }

    private void DisplayKills(string? playerId, List<KillEvent>? kills)
    {
        if (string.IsNullOrEmpty(playerId) || kills == null || kills.Count == 0)
        {
            RecentKillsPanel.IsVisible = false;
            return;
        }

        var items = kills
            .Where(k => k.TimeStamp.HasValue
                     && string.Equals(k.Killer?.Id, playerId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(k => k.TimeStamp)
            .Take(5)
            .Select(k => new CombatEventItem(
                k,
                k.Victim?.Id   ?? "",
                k.Victim?.Name ?? "Desconocido",
                string.IsNullOrEmpty(k.Victim?.GuildName) ? "" : $"[{k.Victim.GuildName}]",
                FormatAgo(k.TimeStamp!.Value.ToUniversalTime())))
            .ToList();

        if (items.Count == 0) { RecentKillsPanel.IsVisible = false; return; }

        KillsList.ItemsSource      = items;
        RecentKillsPanel.IsVisible = true;
    }

    public void KillEntry_Click(object? sender, PointerReleasedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not CombatEventItem combat) return;
        ShowCombatCompare(combat.Event);
    }

    public void CombatEntry_Click(object? sender, PointerReleasedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not CombatEventItem combat) return;
        ShowCombatCompare(combat.Event);
    }

    private void DisplayPlayerInfo(PlayerInfo player, string fallbackName,
                                   List<KillEvent>? kills, List<KillEvent>? deaths)
    {
        PlayerCard.IsVisible      = true;
        PlayerErrorText.IsVisible = false;

        PlayerNameText.Text  = player.Name ?? fallbackName;
        PlayerGuildText.Text = string.IsNullOrEmpty(player.GuildName) ? "Sin guild" : player.GuildName;

        PlayerAllianceText.Text    = string.IsNullOrEmpty(player.AllianceTag)
            ? "" : $"[{player.AllianceTag}] {player.AllianceName}";
        PlayerAllianceText.IsVisible = !string.IsNullOrEmpty(player.AllianceTag);

        SetPlayerIp(player.AverageItemPower);

        PlayerKillFameText.Text  = FormatFame(player.KillFame);
        PlayerDeathFameText.Text = FormatFame(player.DeathFame);
        PlayerRatioText.Text     = player.FameRatio > 0 ? $"{player.FameRatio:F2}x" : "—";

        PlayerPvEText.Text    = FormatFame(player.LifetimeStatistics?.PvE?.Total ?? 0);
        PlayerGatherText.Text = FormatFame(player.LifetimeStatistics?.Gathering?.All?.Total ?? 0);
        PlayerCraftText.Text  = FormatFame(player.LifetimeStatistics?.Crafting?.Total ?? 0);

        PlayerAvatarImg.Source = null;
        _ = LoadImageFromUrlAsync(PlayerAvatarImg, GameInfoService.GetPlayerAvatarUrl(player.Name ?? fallbackName));

        _setValueCts?.Cancel();
        SetValueText.IsVisible      = false;
        LastEquipPanel.IsVisible    = false;
        RecentKillsPanel.IsVisible  = false;
        RecentDeathsPanel.IsVisible = false;
        CombatComparePanel.IsVisible = false;
    }

    // ── Equipment grid ────────────────────────────────────────────────────────

    private void InitBuildValuationSelector()
    {
        var app = Application.Current as App;
        BuildValuationCombo.SelectedIndex = app?.Settings.BuildValuationMode switch
        {
            BuildValuationMode.HighestBuyOrder => 1,
            BuildValuationMode.HighestSellOrder => 2,
            _ => 0,
        };
    }

    private void BuildValuationCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var app = Application.Current as App;
        if (app == null) return;
        app.Settings.BuildValuationMode = BuildValuationCombo.SelectedIndex switch
        {
            1 => BuildValuationMode.HighestBuyOrder,
            2 => BuildValuationMode.HighestSellOrder,
            _ => BuildValuationMode.CheapestSellOrder,
        };
        app.Settings.Save();
        _combatUnitPriceCache.Clear();
    }

    private void SetPlayerIp(double? averageItemPower)
    {
        PlayerIPText.Text = averageItemPower.HasValue && averageItemPower.Value > 0
            ? $"{averageItemPower.Value:F0}"
            : "—";
    }

    private void DisplayEquipment(KillEquipment? equipment, DateTime? lastSeen)
    {
        if (equipment == null) { LastEquipPanel.IsVisible = false; return; }

        var slots = new (EquipmentItem? item, int row, int col, string label)[]
        {
            (equipment.Bag,      0, 0, "Bolso"),
            (equipment.Head,     0, 1, "Cabeza"),
            (equipment.Cape,     0, 2, "Capa"),
            (equipment.MainHand, 1, 0, "Arma"),
            (equipment.Armor,    1, 1, "Armadura"),
            (equipment.OffHand,  1, 2, "Off-hand"),
            (equipment.Potion,   2, 0, "Poción"),
            (equipment.Shoes,    2, 1, "Zapatos"),
            (equipment.Food,     2, 2, "Comida"),
            (equipment.Mount,    3, 1, "Montura"),
        };

        if (slots.All(s => s.item?.Type == null)) { LastEquipPanel.IsVisible = false; return; }

        LastEquipPanel.IsVisible = true;
        LastSeenAgoText.Text     = lastSeen.HasValue ? $"• {FormatAgo(lastSeen.Value)}" : "";

        EquipmentSlotsGrid.Children.Clear();
        SetValueText.Text      = "Calculando valor...";
        SetValueText.IsVisible = true;

        foreach (var (item, row, col, label) in slots)
        {
            var itemName = item?.Type != null
                ? (_itemDatabase.GetNameById(item.Type) ?? item.Type)
                : null;
            var border = new Border
            {
                Width           = 74, Height = 74,
                Margin          = new Thickness(4),
                CornerRadius    = new CornerRadius(7),
                Background      = new SolidColorBrush(Color.FromArgb((byte)(item?.Type != null ? 50 : 20), 255, 255, 255)),
                BorderThickness = new Thickness(1),
                BorderBrush     = item?.Type != null
                    ? QualityBrush(item.Quality)
                    : new SolidColorBrush(Color.FromRgb(35, 35, 40)),
            };
            ToolTip.SetTip(border, itemName != null ? $"{label}: {itemName}" : label);

            if (item?.Type != null)
            {
                var img = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(5) };
                border.Child = img;
                _ = LoadImageFromUrlAsync(img, GameInfoService.GetItemIconUrl(item.Type));

                var capturedId      = item.Type;
                var capturedName    = itemName ?? item.Type;
                var capturedQuality = item.Quality;
                border.Cursor = new Cursor(StandardCursorType.Hand);
                border.PointerReleased += (_, _) =>
                {
                    SetActiveMode(PriceModeContent, PriceModeBtn);
                    (Application.Current as App)?.RealtimeService?.SetItem(null);
                    _ = CheckItemById(capturedId, capturedName, capturedQuality);
                };
            }

            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            EquipmentSlotsGrid.Children.Add(border);
        }

        _setValueCts?.Cancel();
        _setValueCts = new CancellationTokenSource();
        _ = LoadSetValueAsync(equipment!, _setValueCts.Token);
    }

    private async Task LoadSetValueAsync(KillEquipment equipment, CancellationToken ct)
    {
        var items = GetEquipmentItems(equipment);

        if (items.Count == 0) { Dispatcher.UIThread.Invoke(() => SetValueText.IsVisible = false); return; }

        try
        {
            var ids = items.Select(i => i.Type)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var rows = await _apiService.GetBatchPricesAsync(ids, ct) ?? [];
            if (ct.IsCancellationRequested) return;

            double total  = 0;
            int    priced = 0;
            for (var i = 0; i < tasks.Count; i++)
            {
                var buyAt = (await tasks[i])?.BestBuyCity?.BuyAt ?? 0;
                if (buyAt > 0) { total += buyAt; priced++; }
            }

            Dispatcher.UIThread.Invoke(() =>
            {
                if (total > 0)
                {
                    var suffix         = priced < items.Count ? $" ({priced}/{items.Count} items)" : "";
                    SetValueText.Text  = $"⚔ Valor estimado: {total:N0} plata{suffix}";
                    SetValueText.IsVisible = true;
                }
                else
                {
                    SetValueText.IsVisible = false;
                }
            });
        }
        catch (OperationCanceledException) { }
        catch { Dispatcher.UIThread.Invoke(() => SetValueText.IsVisible = false); }
    }

    private static readonly HttpClient _avatarClient = CreateImageClient();
    private static readonly string ImageCacheDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlbionPrices",
        "ImageCache");

    private static HttpClient CreateImageClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                     System.Net.DecompressionMethods.Deflate |
                                     System.Net.DecompressionMethods.Brotli,
            UseProxy = true,
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("image/png,image/*;q=0.8,*/*;q=0.5");
        return client;
    }

    private static async Task LoadImageFromUrlAsync(Image img, string url)
    {
        try
        {
            var bytes = await LoadImageBytesAsync(url);
            await SetImageBytesAsync(img, bytes);
        }
        catch (Exception ex)
        {
            TryDeleteImageCache(url);
            try
            {
                var bytes = await DownloadImageBytesAsync(url);
                await SetImageBytesAsync(img, bytes);
            }
            catch (Exception retryEx)
            {
                System.Diagnostics.Debug.WriteLine($"Image load failed: {ex.Message}; retry: {retryEx.Message} ({url})");
            }
        }
    }

    private static async Task SetImageBytesAsync(Image img, byte[] bytes)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            using var ms = new MemoryStream(bytes);
            img.Source = new Bitmap(ms);
            img.InvalidateVisual();
        });
    }

    private static async Task<byte[]> LoadImageBytesAsync(string url)
    {
        Directory.CreateDirectory(ImageCacheDir);
        var cacheFile = GetImageCachePath(url);
        if (File.Exists(cacheFile))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile);
            if (age < TimeSpan.FromDays(14))
                return await File.ReadAllBytesAsync(cacheFile);
        }

        return await DownloadImageBytesAsync(url);
    }

    private static async Task<byte[]> DownloadImageBytesAsync(string url)
    {
        Directory.CreateDirectory(ImageCacheDir);
        var cacheFile = GetImageCachePath(url);
        var bytes = await _avatarClient.GetByteArrayAsync(url);
        if (bytes.Length > 0)
            await File.WriteAllBytesAsync(cacheFile, bytes);
        return bytes;
    }

    private static string GetImageCachePath(string url) =>
        System.IO.Path.Combine(ImageCacheDir, GetImageCacheKey(url) + ".png");

    private static void TryDeleteImageCache(string url)
    {
        try
        {
            var cacheFile = GetImageCachePath(url);
            if (File.Exists(cacheFile))
                File.Delete(cacheFile);
        }
        catch { }
    }

    private static string GetImageCacheKey(string url)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(bytes);
    }

    private static SolidColorBrush QualityBrush(int quality) => quality switch
    {
        2 => new SolidColorBrush(Color.FromRgb(76,  175, 80)),
        3 => new SolidColorBrush(Color.FromRgb(33,  150, 243)),
        4 => new SolidColorBrush(Color.FromRgb(156, 39,  176)),
        5 => new SolidColorBrush(Color.FromRgb(255, 215,  0)),
        _ => new SolidColorBrush(Color.FromRgb(70,  70,  75)),
    };

    // ── Deaths ───────────────────────────────────────────────────────────────

    private void CloseCombatCompare_Click(object? sender, RoutedEventArgs e) =>
        CombatComparePanel.IsVisible = false;

    private void ShowCombatCompare(KillEvent ev)
    {
        if (ev.Killer == null || ev.Victim == null) return;

        CombatComparePanel.IsVisible = true;
        CombatMetaText.Text = ev.TimeStamp.HasValue
            ? $"{FormatAgo(ev.TimeStamp.Value.ToUniversalTime())} - Fame: {FormatFame(ev.TotalVictimKillFame)}"
            : $"Fame: {FormatFame(ev.TotalVictimKillFame)}";

        WinnerNameText.Text = ev.Killer.Name ?? "Desconocido";
        WinnerGuildText.Text = string.IsNullOrWhiteSpace(ev.Killer.GuildName) ? "Sin guild" : ev.Killer.GuildName;
        WinnerIpText.Text = ev.Killer.AverageItemPower.HasValue ? $"IP {ev.Killer.AverageItemPower.Value:F0}" : "IP --";

        LoserNameText.Text = ev.Victim.Name ?? "Desconocido";
        LoserGuildText.Text = string.IsNullOrWhiteSpace(ev.Victim.GuildName) ? "Sin guild" : ev.Victim.GuildName;
        LoserIpText.Text = ev.Victim.AverageItemPower.HasValue ? $"IP {ev.Victim.AverageItemPower.Value:F0}" : "IP --";

        var assists = ev.Participants?
            .Where(p => !string.IsNullOrWhiteSpace(p.Name)
                     && !string.Equals(p.Id, ev.Killer.Id, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(p.Id, ev.Victim.Id, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        AssistantsText.Text = assists.Count > 0 ? $"Asistieron: {string.Join(", ", assists)}" : "";
        AssistantsText.IsVisible = assists.Count > 0;

        RenderEquipmentGrid(WinnerEquipmentGrid, ev.Killer.Equipment);
        RenderEquipmentGrid(LoserEquipmentGrid, ev.Victim.Equipment);
        WinnerSetValueText.Text = "Valor estimado: calculando...";
        LoserSetValueText.Text = "Valor estimado: calculando...";
        LootValueText.Text = "Loot estimado: calculando...";
        RenderLoot(ev);

        _setValueCts?.Cancel();
        _setValueCts = new CancellationTokenSource();
        _ = LoadCombatValuesAsync(ev, _setValueCts.Token);
    }

    private void RenderEquipmentGrid(Grid target, KillEquipment? equipment)
    {
        target.Children.Clear();
        foreach (var (item, row, col, label) in GetEquipmentSlots(equipment))
        {
            var border = CreateItemSlot(item, label, 78, showCount: false);
            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            target.Children.Add(border);
        }
    }

    private void RenderLoot(KillEvent ev)
    {
        LootItemsPanel.Children.Clear();
        var loot = GetDroppedLoot(ev).Take(24).ToList();
        if (loot.Count == 0)
        {
            LootValueText.Text = "Loot estimado: 0 plata (sin items dropeados)";
            return;
        }

        foreach (var item in loot)
            LootItemsPanel.Children.Add(CreateItemSlot(item, "Loot", 58, showCount: true));
    }

    private Border CreateItemSlot(EquipmentItem? item, string label, double size, bool showCount)
    {
        var itemName = item?.Type != null ? (_itemDatabase.GetNameById(item.Type) ?? item.Type) : null;
        var border = new Border
        {
            Width = size,
            Height = size,
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb((byte)(item?.Type != null ? 48 : 20), 255, 255, 255)),
            BorderThickness = new Thickness(1),
            BorderBrush = item?.Type != null ? QualityBrush(item.Quality) : new SolidColorBrush(Color.FromRgb(48, 48, 54)),
        };
        ToolTip.SetTip(border, itemName != null ? $"{label}: {itemName}" : label);
        if (item?.Type == null) return border;

        var grid = new Grid();
        var img = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(3) };
        grid.Children.Add(img);
        _ = LoadImageFromUrlAsync(img, GameInfoService.GetItemIconUrl(item.Type));

        if (showCount && item.Count > 1)
        {
            grid.Children.Add(new TextBlock
            {
                Text = $"x{item.Count}",
                Foreground = Brushes.Gold,
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 2, 1),
            });
        }

        border.Child = grid;
        border.Cursor = new Cursor(StandardCursorType.Hand);
        var capturedId = item.Type;
        var capturedName = itemName ?? item.Type;
        var capturedQuality = item.Quality;
        border.PointerReleased += (_, _) =>
        {
            SetActiveMode(PriceModeContent, PriceModeBtn);
            (Application.Current as App)?.RealtimeService?.SetItem(null);
            _ = CheckItemById(capturedId, capturedName, capturedQuality);
        };
        return border;
    }

    private static List<(EquipmentItem? Item, int Row, int Col, string Label)> GetEquipmentSlots(KillEquipment? equipment) =>
        equipment == null ? [] :
        [
            (equipment.Bag,      0, 0, "Bolso"),
            (equipment.Head,     0, 1, "Cabeza"),
            (equipment.Cape,     0, 2, "Capa"),
            (equipment.MainHand, 1, 0, "Arma"),
            (equipment.Armor,    1, 1, "Armadura"),
            (equipment.OffHand,  1, 2, "Off-hand"),
            (equipment.Potion,   2, 0, "Pocion"),
            (equipment.Shoes,    2, 1, "Zapatos"),
            (equipment.Food,     2, 2, "Comida"),
            (equipment.Mount,    3, 1, "Montura"),
        ];

    private static List<EquipmentItem> GetEquipmentItems(KillEquipment? equipment) =>
        GetEquipmentSlots(equipment).Select(s => s.Item).Where(i => i?.Type != null).Cast<EquipmentItem>().ToList();

    private static List<EquipmentItem> GetDroppedLoot(KillEvent ev)
    {
        var items = new List<EquipmentItem>();
        if (ev.Victim?.Inventory != null)
            items.AddRange(ev.Victim.Inventory.Where(i => IsCountableLootItem(i, requireDropped: false)).Cast<EquipmentItem>());
        items.AddRange(GetEquipmentItems(ev.Victim?.Equipment).Where(i => IsCountableLootItem(i, requireDropped: true)));
        return items;
    }

    private static bool IsCountableLootItem(EquipmentItem? item, bool requireDropped)
    {
        if (string.IsNullOrWhiteSpace(item?.Type)) return false;
        if (requireDropped && item.Dropped != true) return false;
        var id = item.Type.ToUpperInvariant();
        if (id.Contains("JOURNAL") ||
            id.Contains("TOME") ||
            id.Contains("SKILLBOOK") ||
            id.Contains("SILVERBAG") ||
            id.Contains("NONTRADABLE"))
            return false;
        return true;
    }

    private async Task LoadCombatValuesAsync(KillEvent ev, CancellationToken ct)
    {
        var winnerItems = GetEquipmentItems(ev.Killer?.Equipment);
        var loserItems = GetEquipmentItems(ev.Victim?.Equipment);
        var lootItems = GetDroppedLoot(ev);
        var allIds = winnerItems.Concat(loserItems).Concat(lootItems)
            .Select(i => i.Type)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (allIds.Count == 0) return;

        try
        {
            var rows = await _apiService.GetBatchPricesAsync(allIds, ct) ?? [];
            if (ct.IsCancellationRequested) return;

            var winnerValue = EstimateItemsValue(winnerItems, rows);
            var loserValue = EstimateItemsValue(loserItems, rows);
            var lootValue = EstimateItemsValue(lootItems, rows);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                WinnerSetValueText.Text = $"Valor estimado: {winnerValue:N0} plata";
                LoserSetValueText.Text = $"Valor estimado: {loserValue:N0} plata";
                LootValueText.Text = $"Loot estimado: {lootValue:N0} plata ({lootItems.Count(i => i.Type != null)}/{lootItems.Count} items)";
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                WinnerSetValueText.Text = "Valor estimado: N/A";
                LoserSetValueText.Text = "Valor estimado: N/A";
                LootValueText.Text = "Loot estimado: N/A";
            });
        }
    }

    private double EstimateItemsValue(IEnumerable<EquipmentItem> items, List<PriceApiResponse> rows)
    {
        double total = 0;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Type)) continue;
            var quality = item.Quality <= 0 ? 1 : item.Quality;
            var cacheKey = $"{_apiService.Region}:{item.Type}:{quality}";
            if (!_combatUnitPriceCache.TryGetValue(cacheKey, out var unitPrice))
            {
                unitPrice = rows
                    .Where(r => string.Equals(r.ItemId, item.Type, StringComparison.OrdinalIgnoreCase)
                             && (r.Quality ?? 1) == quality)
                    .Select(r => Math.Max(r.BuyPriceMax ?? 0, r.SellPriceMin ?? 0))
                    .DefaultIfEmpty(0)
                    .Max();
                if (unitPrice > 0)
                    _combatUnitPriceCache[cacheKey] = unitPrice;
            }
            if (unitPrice > 0)
                total += unitPrice * Math.Max(1, item.Count);
        }
        return total;
    }

    private void DisplayDeaths(List<KillEvent>? deaths)
    {
        if (deaths == null || deaths.Count == 0)
        {
            RecentDeathsPanel.IsVisible = false;
            return;
        }

        var items = deaths
            .Where(d => d.TimeStamp.HasValue)
            .OrderByDescending(d => d.TimeStamp)
            .Take(5)
            .Select(d => new CombatEventItem(
                d,
                d.Killer?.Id ?? "",
                d.Killer?.Name  ?? "Desconocido",
                string.IsNullOrEmpty(d.Killer?.GuildName) ? "" : $"[{d.Killer.GuildName}]",
                FormatAgo(d.TimeStamp!.Value.ToUniversalTime())))
            .ToList();

        DeathsList.ItemsSource      = items;
        RecentDeathsPanel.IsVisible = true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void ForceResizeWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            SizeToContent = SizeToContent.Manual;
            ApplyOverlaySize();
            InvalidateMeasure();
        });
    }

    private static string FormatAgo(DateTime utcDate)
    {
        var diff = DateTime.UtcNow - utcDate;
        if (diff.TotalMinutes < 1)  return "ahora";
        if (diff.TotalHours   < 1)  return $"hace {(int)diff.TotalMinutes}m";
        if (diff.TotalDays    < 1)  return $"hace {(int)diff.TotalHours}h";
        if (diff.TotalDays    < 30) return $"hace {(int)diff.TotalDays}d";
        return $"hace {(int)(diff.TotalDays / 30)}mo";
    }

    private static string FormatFame(long value)
    {
        if (value < 0) return "—";
        if (value == 0) return "0";
        if (value >= 1_000_000_000) return $"{value / 1_000_000_000.0:F1}B";
        if (value >= 1_000_000)     return $"{value / 1_000_000.0:F1}M";
        if (value >= 1_000)         return $"{value / 1_000.0:F1}K";
        return value.ToString("N0");
    }

    // ── Region selector ──────────────────────────────────────────────────────

    private void RegionNA_Click(object? sender, RoutedEventArgs e) => ApplyRegion(ServerRegion.Americas);
    private void RegionEU_Click(object? sender, RoutedEventArgs e) => ApplyRegion(ServerRegion.Europe);
    private void RegionAS_Click(object? sender, RoutedEventArgs e) => ApplyRegion(ServerRegion.Asia);

    private void ApplyRegion(ServerRegion region)
    {
        (Application.Current as App)?.ChangeRegion(region);
        _combatUnitPriceCache.Clear();
        RefreshRegionButtons(region);
        _ = LoadGoldPriceAsync();
    }

    private void RefreshRegionButtons(ServerRegion active)
    {
        if (_regionButtons == null) return;
        var regions = new[] { ServerRegion.Americas, ServerRegion.Europe, ServerRegion.Asia };
        for (int i = 0; i < _regionButtons.Length; i++)
        {
            var isActive = regions[i] == active;
            _regionButtons[i].Background  = isActive ? new SolidColorBrush(Color.FromRgb(255, 117, 80)) : Brushes.Transparent;
            _regionButtons[i].Foreground  = isActive ? Brushes.White : new SolidColorBrush(Color.FromRgb(102, 102, 102));
            _regionButtons[i].BorderBrush = isActive ? new SolidColorBrush(Color.FromRgb(255, 117, 80)) : new SolidColorBrush(Color.FromRgb(51, 51, 51));
        }
    }

    // ── Item search & check ──────────────────────────────────────────────────

    private async void CheckButton_Click(object? sender, RoutedEventArgs e) =>
        await CheckItem(ItemInput.Text ?? "");

    private void ItemInput_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e) =>
        RefreshSearchSuggestions();

    private async void ItemInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && _activeSuggestions.Count > 0)
        {
            _selectedSuggestionIndex = Math.Min(_selectedSuggestionIndex + 1, _activeSuggestions.Count - 1);
            RebuildSearchSuggestionRows();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && _activeSuggestions.Count > 0)
        {
            _selectedSuggestionIndex = Math.Max(_selectedSuggestionIndex - 1, 0);
            RebuildSearchSuggestionRows();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            HideSearchSuggestions();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Return || e.Key == Key.Enter)
        {
            if (_selectedSuggestionIndex >= 0 && _activeSuggestions.Count > 0)
            {
                await ActivateSearchSuggestion(_selectedSuggestionIndex);
                e.Handled = true;
                return;
            }

            HideSearchSuggestions();
            await CheckItem(ItemInput.Text ?? "");
        }
    }

    private async Task CheckItemById(
        string itemId,
        string itemName,
        int quality = 1,
        bool updateSearchText = true,
        bool focusSearch = true)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _isLoading = true;

        try
        {
            HideSearchSuggestions();
            if (updateSearchText)
            {
                _suppressSuggestionRefresh = true;
                ItemInput.Text = itemName;
                _suppressSuggestionRefresh = false;
            }
            StatusText.Text         = "Buscando...";
            StatusText.IsVisible    = true;
            ItemInfoPanel.IsVisible = false;
            ErrorText.IsVisible     = false;

            _currentQuality = quality;
            var ct      = _cts.Token;
            var summary = await _apiService.GetItemPriceAsync(itemId, quality, ct);
            StatusText.IsVisible = false;

            if (summary == null || summary.Prices.Count == 0)
            {
                ShowError($"Sin precios para: {itemId}");
                ShowCentered(focusSearch); return;
            }

            summary.ItemName = _itemDatabase.GetNameById(itemId) ?? itemName;
            DisplayPriceInfo(summary);

            var hist = (Application.Current as App)?.HistoryService;
            hist?.AddToHistory(itemId, summary.ItemName);
            RefreshHistoryUI();

            // SetupQualityButtons resets to 1, so restore the requested quality after
            if (quality > 1 && quality <= QualityLabels.Length)
            {
                _currentQuality = quality;
                RefreshButtonSelection(QualityButtonsPanel, QualityLabels[quality - 1]);
            }
            _ = LoadPriceHistoryAsync(itemId, quality, ct);
            ShowCentered(focusSearch);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError($"Error: {ex.Message}"); ShowCentered(focusSearch); }
        finally { _isLoading = false; }
    }

    private async Task CheckItem(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (_isLoading) { _cts?.Cancel(); await Task.Delay(100); }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _isLoading = true;

        try
        {
            HideSearchSuggestions();
            StatusText.Text       = "Buscando...";
            StatusText.IsVisible  = true;
            ItemInfoPanel.IsVisible = false;
            ErrorText.IsVisible     = false;

            DebugText.Foreground = new SolidColorBrush(Colors.Yellow);
            DebugText.Text       = $"DB: {_itemDatabase.ItemCount} items | Buscando: '{text}'";
            DebugText.IsVisible  = true;

            if (_itemDatabase.ItemCount == 0)
            {
                ShowError($"Base de datos vacía. Error: {_itemDatabase.LoadError ?? "desconocido"}");
                ShowCentered(); return;
            }

            var itemId = _itemDatabase.FindIdByName(text);
            if (string.IsNullOrEmpty(itemId))
            {
                var suggestions = _itemDatabase.Search(text);
                DebugText.Text = $"DB: {_itemDatabase.ItemCount} | '{text}': {suggestions.Count} resultados";
                if (suggestions.Count > 0)
                {
                    itemId = suggestions[0];
                    DebugText.Text += $" → {itemId}";
                }
                else
                {
                    ShowError($"No encontrado ({_itemDatabase.ItemCount} items en DB): {text}");
                    ShowCentered(); return;
                }
            }

            DebugText.Text = $"Encontrado: {itemId}";

            _currentQuality = 1;
            var ct      = _cts.Token;
            var summary = await _apiService.GetItemPriceAsync(itemId, 1, ct);
            if (summary == null || summary.Prices.Count == 0)
            {
                ShowError($"Sin precios para: {itemId}");
                ShowCentered(); return;
            }

            summary.ItemName = _itemDatabase.GetNameById(itemId) ?? itemId;
            DisplayPriceInfo(summary);
            DebugText.IsVisible = false;

            var hist = (Application.Current as App)?.HistoryService;
            hist?.AddToHistory(itemId, summary.ItemName);
            RefreshHistoryUI();

            _ = LoadPriceHistoryAsync(itemId, 1, ct);
            ShowCentered();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError($"Error: {ex.Message}"); ShowCentered(); }
        finally { _isLoading = false; }
    }

    private void ShowError(string message)
    {
        StatusText.IsVisible    = false;
        ItemInfoPanel.IsVisible = false;
        DebugText.IsVisible     = false;
        ErrorText.Text          = message;
        ErrorText.IsVisible     = true;
        ForceResizeWindow();
    }

    // ── Add to watchlist ─────────────────────────────────────────────────────

    private void AddToWatchlist_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentItemId == null || _currentItemName == null) return;
        var ws = (Application.Current as App)?.WatchlistService;
        if (ws == null) return;

        ws.Add(_currentItemId, _currentItemName, _currentQuality, _currentBestBuyPrice);

        SetActiveMode(WatchModeContent, WatchModeBtn);
        (Application.Current as App)?.RealtimeService?.SetItem(null);
        EnsureWatchTimerRunning();
        RefreshWatchlistUI();
    }

    // ── Watchlist timer & refresh ─────────────────────────────────────────────

    private void EnsureWatchTimerRunning()
    {
        if (_watchTimer != null) return;
        _watchSecondsLeft = WatchIntervalSeconds;
        _watchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _watchTimer.Tick += WatchTimer_Tick;
        _watchTimer.Start();
        _ = RefreshAllWatchPricesAsync();
    }

    private void WatchTimer_Tick(object? sender, EventArgs e)
    {
        _watchSecondsLeft--;
        UpdateWatchCountdownLabel();
        if (_watchSecondsLeft <= 0)
        {
            _watchSecondsLeft = WatchIntervalSeconds;
            _ = RefreshAllWatchPricesAsync();
        }
    }

    private void UpdateWatchCountdownLabel()
    {
        var min = _watchSecondsLeft / 60;
        var sec = _watchSecondsLeft % 60;
        WatchCountdownText.Text = $"actualiza en {min}:{sec:D2}";
    }

    private void WatchRefresh_Click(object? sender, RoutedEventArgs e)
    {
        _watchSecondsLeft = WatchIntervalSeconds;
        UpdateWatchCountdownLabel();
        _ = RefreshAllWatchPricesAsync();
    }

    private async Task RefreshAllWatchPricesAsync()
    {
        var ws = (Application.Current as App)?.WatchlistService;
        if (ws == null || ws.Items.Count == 0) return;

        Dispatcher.UIThread.Invoke(() =>
        {
            foreach (var item in ws.Items) item.IsLoading = true;
            RefreshWatchlistUI();
        });

        var snapshot = ws.Items.ToList();
        var tasks = snapshot.Select(async entry =>
        {
            try
            {
                var summary = await _apiService.GetItemPriceAsync(entry.ItemId, entry.Quality, CancellationToken.None);
                if (summary != null)
                    ws.UpdatePrices(entry.ItemId, entry.Quality,
                        summary.BestBuyCity?.BuyAt        ?? 0,
                        summary.BestSellOrderCity?.BuyAt  ?? 0,
                        summary.BestBuyCity?.City         ?? "");
            }
            catch { }
            finally { entry.IsLoading = false; }
        });

        await Task.WhenAll(tasks);

        Dispatcher.UIThread.Invoke(() =>
        {
            RefreshWatchlistUI();
            CheckAndShowAlertBanner();
        });
    }

    private void RefreshWatchlistUI()
    {
        var ws = (Application.Current as App)?.WatchlistService;
        if (ws == null) return;

        WatchEmptyText.IsVisible = ws.Items.Count == 0;
        WatchItemsPanel.Children.Clear();
        foreach (var item in ws.Items)
            WatchItemsPanel.Children.Add(BuildWatchRow(item));
    }

    private Border BuildWatchRow(WatchlistEntry item)
    {
        var isTriggered = item.AlertTriggered;
        var hasAlert    = item.AlertBuyBelow.HasValue;

        double pct      = 0;
        var    pctLabel = "—";
        if (item.BasePrice > 0 && item.LastBuyPrice > 0)
        {
            pct      = (item.LastBuyPrice - item.BasePrice) / item.BasePrice * 100;
            pctLabel = pct >= 0 ? $"+{pct:F1}%" : $"{pct:F1}%";
        }
        var pctBrush = pct >= 0
            ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
            : new SolidColorBrush(Color.FromRgb(255, 68, 68));

        var border = new Border
        {
            Background      = new SolidColorBrush(isTriggered
                ? Color.FromArgb(30, 255, 68, 68)
                : Color.FromArgb(21, 255, 255, 255)),
            BorderBrush     = new SolidColorBrush(isTriggered
                ? Color.FromRgb(255, 68, 68)
                : Color.FromArgb(25, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(8, 6),
            Margin          = new Thickness(0, 0, 0, 4),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var nameText     = item.Quality > 1 ? $"{item.ItemName}  [{QualityLabels[item.Quality - 1]}]" : item.ItemName;
        var priceSubtext = item.IsLoading
            ? "Actualizando..."
            : (item.LastBuyPrice > 0 ? $"{item.LastBuyPrice:N0} — {item.BestBuyCity}" : "Sin datos");

        var leftPanel = new StackPanel();
        leftPanel.Children.Add(new TextBlock { Text = nameText, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeight.SemiBold });
        leftPanel.Children.Add(new TextBlock { Text = priceSubtext, Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136)), FontSize = 9 });
        if (hasAlert)
            leftPanel.Children.Add(new TextBlock
            {
                Text       = $"Alerta <= {item.AlertBuyBelow:N0}",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                FontSize   = 8,
            });
        Grid.SetColumn(leftPanel, 0);
        grid.Children.Add(leftPanel);

        var pctBlock = new TextBlock
        {
            Text              = pctLabel,
            Foreground        = pctBrush,
            FontSize          = 11,
            FontWeight        = FontWeight.Bold,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin            = new Thickness(8, 0),
        };
        Grid.SetColumn(pctBlock, 1);
        grid.Children.Add(pctBlock);

        var capturedItem = item;
        var btnPanel = new StackPanel
        {
            Orientation       = Avalonia.Layout.Orientation.Vertical,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var alertBtn = new Button
        {
            Content    = hasAlert ? "●" : "○",
            Width = 22, Height = 18,
            Padding = new Thickness(0), MinHeight = 0,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment   = Avalonia.Layout.VerticalAlignment.Center,
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = hasAlert
                ? new SolidColorBrush(Color.FromRgb(255, 215, 0))
                : new SolidColorBrush(Color.FromRgb(85, 85, 85)),
            FontSize = 10,
        };
        ToolTip.SetTip(alertBtn, hasAlert
            ? $"Alerta activa: <= {item.AlertBuyBelow:N0}. Click para quitar"
            : "Activar alerta de precio");
        alertBtn.Click += async (_, _) => await ShowAlertDialogAsync(capturedItem);
        btnPanel.Children.Add(alertBtn);

        var removeBtn = new Button
        {
            Content    = "x",
            Width = 22, Height = 18,
            Padding = new Thickness(0), MinHeight = 0,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment   = Avalonia.Layout.VerticalAlignment.Center,
            Background      = Brushes.Transparent,
            Foreground      = new SolidColorBrush(Color.FromRgb(85, 85, 85)),
            BorderThickness = new Thickness(0),
            FontSize        = 9,
        };
        removeBtn.Click += (_, _) =>
        {
            (Application.Current as App)?.WatchlistService?.Remove(capturedItem.ItemId, capturedItem.Quality);
            RefreshWatchlistUI();
        };
        btnPanel.Children.Add(removeBtn);

        Grid.SetColumn(btnPanel, 2);
        grid.Children.Add(btnPanel);

        border.Child = grid;
        return border;
    }

    private async Task ShowAlertDialogAsync(WatchlistEntry item)
    {
        var ws = (Application.Current as App)?.WatchlistService;
        if (ws == null) return;

        if (item.AlertBuyBelow.HasValue)
        {
            ws.SetAlert(item.ItemId, item.Quality, null);
            RefreshWatchlistUI();
            return;
        }

        var dlg = new Window
        {
            Title                 = "Configurar alerta",
            Width                 = 310,
            SizeToContent         = SizeToContent.Height,
            SystemDecorations     = SystemDecorations.BorderOnly,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background            = new SolidColorBrush(Color.FromRgb(20, 20, 24)),
            CanResize             = false,
        };

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text         = $"Alertar cuando {item.ItemName} baje a precio <=:",
            Foreground   = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
            FontSize     = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 8),
        });
        var input = new TextBox
        {
            Background  = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
            Foreground  = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
            FontSize    = 12,
            Padding     = new Thickness(8, 4),
            Text        = item.LastBuyPrice > 0 ? ((long)item.LastBuyPrice).ToString() : "",
        };
        stack.Children.Add(input);

        var btnRow = new StackPanel
        {
            Orientation         = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin              = new Thickness(0, 10, 0, 0),
        };
        var okBtn = new Button
        {
            Content = "Activar", Padding = new Thickness(14, 4), Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(255, 117, 80)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
        };
        var cancelBtn = new Button
        {
            Content = "Cancelar", Padding = new Thickness(10, 4),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
            BorderThickness = new Thickness(0),
        };
        okBtn.Click += (_, _) =>
        {
            var raw = System.Text.RegularExpressions.Regex.Replace(input.Text ?? "", @"[^\d]", "");
            if (double.TryParse(raw, out var threshold) && threshold > 0)
            {
                ws.SetAlert(item.ItemId, item.Quality, threshold);
                Dispatcher.UIThread.Invoke(RefreshWatchlistUI);
            }
            dlg.Close();
        };
        cancelBtn.Click += (_, _) => dlg.Close();
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        stack.Children.Add(btnRow);
        dlg.Content = stack;

        _dialogOpen = true;
        try   { await dlg.ShowDialog(this); }
        finally { _dialogOpen = false; }
    }

    private void AlertBannerDismiss_Click(object? sender, RoutedEventArgs e)
    {
        AlertBanner.IsVisible = false;
        var ws = (Application.Current as App)?.WatchlistService;
        if (ws == null) return;
        foreach (var item in ws.Items)
            item.AlertTriggered = false;
        RefreshWatchlistUI();
    }

    private void CheckAndShowAlertBanner()
    {
        var ws = (Application.Current as App)?.WatchlistService;
        if (ws == null) return;
        var triggered = ws.Items.Where(i => i.AlertTriggered).ToList();
        if (triggered.Count == 0) { AlertBanner.IsVisible = false; return; }
        var wasVisible        = AlertBanner.IsVisible;
        AlertBannerText.Text  = "Alerta: " + string.Join(", ", triggered.Select(i => $"{i.ItemName} <= {i.LastBuyPrice:N0}"));
        AlertBanner.IsVisible = true;
        if (!wasVisible) PlayAlertSound();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = false)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static extern bool MessageBeep(uint uType);

    private static void PlayAlertSound()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                MessageBeep(0x00000030); // MB_ICONEXCLAMATION
            else
                Console.Beep(880, 300);
        }
        catch { }
    }

    // ── Crafting ──────────────────────────────────────────────────────────────

    private async void CraftSearchTarget_Click(object? sender, RoutedEventArgs e) =>
        await SearchCraftTargetAsync(CraftTargetInput.Text ?? "");

    private async void CraftTargetInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return || e.Key == Key.Enter)
            await SearchCraftTargetAsync(CraftTargetInput.Text ?? "");
    }

    private async Task SearchCraftTargetAsync(string text)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        CraftStatusText.Text       = "Buscando...";
        CraftStatusText.IsVisible  = true;
        CraftTargetPanel.IsVisible = false;

        var itemId = _itemDatabase.FindIdByName(text);
        if (string.IsNullOrEmpty(itemId))
        {
            var sugg = _itemDatabase.Search(text);
            itemId = sugg.Count > 0 ? sugg[0] : null;
        }

        if (itemId == null)
        {
            CraftStatusText.Text = $"No encontrado: {text}";
            return;
        }

        var summary = await _apiService.GetItemPriceAsync(itemId, 1, CancellationToken.None);
        CraftStatusText.IsVisible = false;

        _craftTargetId        = itemId;
        _craftTargetName      = _itemDatabase.GetNameById(itemId) ?? itemId;
        var bestSellOrder     = summary?.BestSellOrderCity;
        _craftTargetSellPrice = bestSellOrder?.BuyAt ?? 0;

        CraftTargetNameText.Text = _craftTargetName;
        CraftTargetIdText.Text   = itemId;
        CraftTargetSellText.Text = _craftTargetSellPrice > 0
            ? $"{_craftTargetSellPrice:N0}  ({bestSellOrder?.City ?? ""})"
            : "—";
        CraftTargetIcon.Source   = null;
        _ = LoadImageFromUrlAsync(CraftTargetIcon, GameInfoService.GetItemIconUrl(itemId));

        CraftTargetPanel.IsVisible     = true;
        CraftMaterialsHeader.IsVisible = true;
        CraftAddMaterialRow.IsVisible  = false;
        RebuildMaterialRows();
        RefreshCraftSummary();
    }

    private void CraftAddMaterial_Click(object? sender, RoutedEventArgs e)
    {
        CraftAddMaterialRow.IsVisible = !CraftAddMaterialRow.IsVisible;
        CraftMaterialNameInput.Text   = "";
        CraftMaterialQtyInput.Text    = "1";
        if (CraftAddMaterialRow.IsVisible) CraftMaterialNameInput.Focus();
    }

    private async void CraftMaterialInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return || e.Key == Key.Enter)
            await ConfirmAddMaterialAsync();
        else if (e.Key == Key.Escape)
            CraftAddMaterialRow.IsVisible = false;
    }

    private async void CraftConfirmMaterial_Click(object? sender, RoutedEventArgs e) =>
        await ConfirmAddMaterialAsync();

    private async Task ConfirmAddMaterialAsync()
    {
        var name = CraftMaterialNameInput.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) return;

        if (!int.TryParse(CraftMaterialQtyInput.Text?.Trim(), out var qty) || qty < 1) qty = 1;

        var itemId = _itemDatabase.FindIdByName(name);
        if (string.IsNullOrEmpty(itemId))
        {
            var sugg = _itemDatabase.Search(name);
            itemId = sugg.Count > 0 ? sugg[0] : null;
        }

        if (itemId == null)
        {
            CraftStatusText.Text      = $"Material no encontrado: {name}";
            CraftStatusText.IsVisible = true;
            return;
        }

        _craftMaterials.Add(new CraftMaterial
        {
            ItemId   = itemId,
            ItemName = _itemDatabase.GetNameById(itemId) ?? itemId,
            Quantity = qty,
        });
        CraftAddMaterialRow.IsVisible = false;
        RebuildMaterialRows();
        RefreshCraftSummary();
        await FetchMaterialPricesAsync();
    }

    private void RebuildMaterialRows()
    {
        CraftMaterialsPanel.Children.Clear();
        foreach (var mat in _craftMaterials)
        {
            var m = mat;
            CraftMaterialsPanel.Children.Add(BuildMaterialRow(m));
        }
    }

    private Border BuildMaterialRow(CraftMaterial mat)
    {
        var border = new Border
        {
            Background      = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(8, 5),
            Margin          = new Thickness(0, 0, 0, 3),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var sub = mat.UnitPrice > 0
            ? $"x{mat.Quantity}  {mat.UnitPrice:N0} c/u  ({mat.UnitPrice * mat.Quantity:N0} total)  {mat.BestCity}"
            : $"x{mat.Quantity}  cargando...";

        var leftPanel = new StackPanel();
        leftPanel.Children.Add(new TextBlock { Text = mat.ItemName, Foreground = Brushes.White, FontSize = 11 });
        leftPanel.Children.Add(new TextBlock { Text = sub, Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136)), FontSize = 9 });
        Grid.SetColumn(leftPanel, 0);
        grid.Children.Add(leftPanel);

        var capturedMat = mat;
        var removeBtn = new Button
        {
            Content    = "x",
            Width = 22, Height = 22,
            Padding = new Thickness(0), MinHeight = 0,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment   = Avalonia.Layout.VerticalAlignment.Center,
            Background      = Brushes.Transparent,
            Foreground      = new SolidColorBrush(Color.FromRgb(85, 85, 85)),
            BorderThickness = new Thickness(0),
            FontSize        = 9,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        removeBtn.Click += (_, _) =>
        {
            _craftMaterials.Remove(capturedMat);
            RebuildMaterialRows();
            RefreshCraftSummary();
        };
        Grid.SetColumn(removeBtn, 1);
        grid.Children.Add(removeBtn);

        border.Child = grid;
        return border;
    }

    private async Task FetchMaterialPricesAsync()
    {
        var tasks = _craftMaterials.ToList().Select(async mat =>
        {
            try
            {
                var summary = await _apiService.GetItemPriceAsync(mat.ItemId, 1, CancellationToken.None);
                if (summary?.BestBuyCity != null)
                {
                    mat.UnitPrice = summary.BestBuyCity.BuyAt;
                    mat.BestCity  = summary.BestBuyCity.City;
                }
            }
            catch { }
        });
        await Task.WhenAll(tasks);
        Dispatcher.UIThread.Invoke(() =>
        {
            RebuildMaterialRows();
            RefreshCraftSummary();
        });
    }

    private void CraftReturn_Changed(object? sender, Avalonia.Controls.TextChangedEventArgs e) =>
        RefreshCraftSummary();

    private void CraftYield_Changed(object? sender, Avalonia.Controls.TextChangedEventArgs e) =>
        RefreshCraftSummary();

    private void RefreshCraftSummary()
    {
        if (_craftMaterials.Count == 0 || _craftTargetSellPrice <= 0)
        {
            CraftSummaryPanel.IsVisible = false;
            return;
        }

        var grossCost = _craftMaterials.Sum(m => m.UnitPrice * m.Quantity);
        if (grossCost <= 0) { CraftSummaryPanel.IsVisible = false; return; }

        var yield = int.TryParse(CraftYieldInput.Text?.Trim(), out var y) && y > 0 ? y : 1;

        var returnRate    = double.TryParse(CraftReturnRateInput.Text?.Trim(), out var rr)
                            ? Math.Clamp(rr, 0, 100) : 0;
        var returnAmount  = grossCost * returnRate / 100.0;
        var effectiveCost = grossCost - returnAmount;

        var totalRevenue = _craftTargetSellPrice * yield;
        var taxRate      = _craftPremium ? 0.04 : 0.08;
        var tax          = totalRevenue * taxRate;
        var profit       = totalRevenue - tax - effectiveCost;

        // Gross materials row
        CraftMaterialsLabel.Text = returnRate > 0 ? "Materiales brutos:" : "Costo materiales:";
        CraftTotalCostText.Text  = $"{grossCost:N0}";

        // Conditional return rows
        var showReturn = returnRate > 0;
        CraftReturnRow.IsVisible  = showReturn;
        CraftNetCostRow.IsVisible = showReturn;
        if (showReturn)
        {
            CraftReturnLabel.Text = $"Retorno ({returnRate:F0}%):";
            CraftReturnText.Text  = $"-{returnAmount:N0}";
            CraftNetCostText.Text = $"{effectiveCost:N0}";
        }

        CraftSellPriceText.Text = yield > 1
            ? $"{_craftTargetSellPrice:N0} ×{yield} = {totalRevenue:N0}"
            : $"{_craftTargetSellPrice:N0}";
        CraftTaxText.Text       = $"-{tax:N0}";
        CraftProfitText.Text    = profit >= 0 ? $"+{profit:N0}" : $"{profit:N0}";
        CraftProfitText.Foreground = profit >= 0
            ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
            : new SolidColorBrush(Color.FromRgb(255, 68, 68));

        CraftSummaryPanel.IsVisible = true;
    }

    // ── Shared UI handlers ───────────────────────────────────────────────────

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // REFINING CALCULATOR
    // ══════════════════════════════════════════════════════════════════════════

    private static readonly string[] RefineCities =
        ["Thetford", "Bridgewatch", "Lymhurst", "Martlock", "Fort Sterling", "Caerleon", "Brecilien"];

    private void InitRefineSelectors()
    {
        // Resource buttons
        foreach (var res in ResourceIds.Keys)
        {
            var btn = MakeSmallToggleBtn(res, res == _refineResource, RefineResourceBtn_Click);
            RefineResourcePanel.Children.Add(btn);
        }
        // Tier buttons
        for (var t = 2; t <= 8; t++)
        {
            var tier = t;
            var btn  = MakeSmallToggleBtn($"T{tier}", tier == _refineTier, (s, _) =>
            {
                _refineTier = tier;
                HighlightToggleGroup(RefineResourcePanel, null); // just refresh
                HighlightToggleGroup(RefineTierPanel, (Button)s!);
            });
            RefineTierPanel.Children.Add(btn);
        }
        // City buttons
        foreach (var city in RefineCities)
        {
            var c   = city;
            var btn = MakeSmallToggleBtn(c, c == _refineCity, (s, _) =>
            {
                _refineCity = c;
                HighlightToggleGroup(RefineCityPanel, (Button)s!);
            });
            RefineCityPanel.Children.Add(btn);
        }
        HighlightToggleGroup(RefineTierPanel, (Button)RefineTierPanel.Children[_refineTier - 2]);
        HighlightToggleGroup(RefineCityPanel, (Button)RefineCityPanel.Children[0]);
    }

    private void RefineResourceBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _refineResource = btn.Content?.ToString() ?? _refineResource;
        // Auto-select matching city
        if (ResourceMatchCity.TryGetValue(_refineResource, out var matchCity))
        {
            _refineCity = matchCity;
            var idx = Array.IndexOf(RefineCities, matchCity);
            if (idx >= 0) HighlightToggleGroup(RefineCityPanel, (Button)RefineCityPanel.Children[idx]);
        }
        HighlightToggleGroup(RefineResourcePanel, btn);
    }

    private void RefineFocus_Click(object? sender, RoutedEventArgs e)
    {
        _refineFocus = !_refineFocus;
        RefineFocusBtn.Content    = _refineFocus ? "Sí" : "No";
        RefineFocusBtn.Background = _refineFocus
            ? new SolidColorBrush(Color.FromRgb(255, 117, 80))
            : Brushes.Transparent;
        RefineFocusBtn.Foreground = _refineFocus ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(102, 102, 102));
    }

    private async void RefineCalc_Click(object? sender, RoutedEventArgs e) => await RefineCalculateAsync();

    private async Task RefineCalculateAsync()
    {
        if (!ResourceIds.TryGetValue(_refineResource, out var ids)) return;
        if (!int.TryParse(RefineQtyInput.Text?.Trim(), out var qty) || qty < 1) qty = 1;

        RefineStatusText.Text      = "Consultando precios...";
        RefineStatusText.IsVisible = true;
        RefineResultPanel.IsVisible = false;

        var rawId    = $"T{_refineTier}_{ids.Raw}";
        var outId    = $"T{_refineTier}_{ids.Refined}";
        var lowerId  = _refineTier > 2 ? $"T{_refineTier - 1}_{ids.Refined}" : null;

        var (rawTask, outTask, lowerTask) = (
            _apiService.GetItemPriceAsync(rawId),
            _apiService.GetItemPriceAsync(outId),
            lowerId != null ? _apiService.GetItemPriceAsync(lowerId) : Task.FromResult<ItemPriceSummary?>(null));

        await Task.WhenAll(rawTask, outTask, lowerTask);

        var rawSum   = rawTask.Result;
        var outSum   = outTask.Result;
        var lowerSum = lowerTask.Result;

        if (rawSum == null || outSum == null)
        {
            RefineStatusText.Text = "Sin datos de precio. Verificá el recurso/tier.";
            return;
        }

        double rawPrice   = rawSum.Prices.FirstOrDefault(p => p.City == _refineCity)?.BuyAt
                            ?? rawSum.BestBuyCity?.BuyAt ?? 0;
        double lowerPrice = lowerSum?.Prices.FirstOrDefault(p => p.City == _refineCity)?.BuyAt
                            ?? lowerSum?.BestBuyCity?.BuyAt ?? 0;
        double outPrice   = outSum.Prices.FirstOrDefault(p => p.City == _refineCity)?.BuyAt
                            ?? outSum.BestSellOrderCity?.BuyAt ?? 0;

        double returnRate = GetRefineReturnRate();
        double costPerUnit = _refineTier == 2
            ? 2 * rawPrice * (1 - returnRate)
            : (2 * rawPrice + lowerPrice) * (1 - returnRate);

        double totalCost = costPerUnit * qty;
        double revenue   = outPrice * qty * 0.92;
        double profit    = revenue - totalCost;

        var matchCity = ResourceMatchCity.GetValueOrDefault(_refineResource, "");
        RefineResultTitle.Text = $"REFINADO T{_refineTier} {_refineResource} — {qty} ud.  [{_refineCity}" +
                                 (_refineCity == matchCity ? " ★" : "") + "]";

        RefineRateText.Text      = $"{returnRate * 100:F1}%";
        RefineRawCostText.Text   = rawPrice > 0 ? $"{rawPrice * 2 * qty * (1 - returnRate):N0}" : "—";
        RefineLowerLabel.IsVisible  = _refineTier > 2;
        RefineLowerCostText.IsVisible = _refineTier > 2;
        RefineLowerCostText.Text = lowerPrice > 0 ? $"{lowerPrice * qty * (1 - returnRate):N0}" : "—";
        RefineTotalCostText.Text = totalCost > 0  ? $"{totalCost:N0}" : "—";
        RefineRevText.Text       = outPrice > 0   ? $"{revenue:N0}"  : "—";

        RefineProfitText.Text       = profit != 0 ? $"{(profit >= 0 ? "+" : "")}{profit:N0}" : "—";
        RefineProfitText.Foreground = profit >= 0
            ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
            : new SolidColorBrush(Color.FromRgb(255, 68, 68));

        RefineStatusText.IsVisible  = false;
        RefineResultPanel.IsVisible = true;
        ForceResizeWindow();
    }

    private double GetRefineReturnRate()
    {
        var matchCity = ResourceMatchCity.GetValueOrDefault(_refineResource, "");
        if (_refineCity == matchCity)
            return _refineFocus ? 0.539 : 0.367;
        if (_refineCity is "Caerleon" or "Brecilien")
            return _refineFocus ? 0.479 : 0.302;
        return _refineFocus ? 0.479 : 0.152;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ENCHANTING CALCULATOR
    // ══════════════════════════════════════════════════════════════════════════

    private static readonly string[] EnchantMaterialIds =
        ["T{0}_RUNE", "T{0}_SOUL", "T{0}_RELIC"];

    private void EnchantItemInput_KeyDown(object? sender, KeyEventArgs e)
    { if (e.Key == Key.Return) _ = EnchantSearchAsync(); }

    private void EnchantSearch_Click(object? sender, RoutedEventArgs e) => _ = EnchantSearchAsync();

    private async Task EnchantSearchAsync()
    {
        var input = EnchantItemInput.Text?.Trim();
        if (string.IsNullOrEmpty(input)) return;

        EnchantStatusText.Text      = "Buscando...";
        EnchantStatusText.IsVisible = true;
        EnchantResultPanel.Children.Clear();

        var matches = _itemDatabase.Search(input);
        if (matches.Count == 0)
        {
            EnchantStatusText.Text = "Item no encontrado.";
            return;
        }

        // Pick first match with a tiered ID (T4-T8) and no enchant suffix
        var itemId = matches.FirstOrDefault(m =>
        {
            var m2 = TieredItemRegex.Match(m);
            return m2.Success && !m.Contains('@');
        }) ?? matches[0];

        var match = TieredItemRegex.Match(itemId);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var tier) || tier < 2)
        {
            EnchantStatusText.Text = "Item no enchantable (tier < 2).";
            return;
        }

        _enchantBaseId = itemId;
        EnchantStatusText.Text = "Consultando precios...";

        // Fetch base + @1 @2 @3 prices AND enchant materials in parallel
        var priceTasks = new[]
        {
            _apiService.GetItemPriceAsync($"{itemId}"),
            _apiService.GetItemPriceAsync($"{itemId}@1"),
            _apiService.GetItemPriceAsync($"{itemId}@2"),
            _apiService.GetItemPriceAsync($"{itemId}@3"),
        };
        var matTasks = new[]
        {
            _apiService.GetItemPriceAsync($"T{tier}_RUNE"),
            _apiService.GetItemPriceAsync($"T{tier}_SOUL"),
            _apiService.GetItemPriceAsync($"T{tier}_RELIC"),
        };
        await Task.WhenAll(priceTasks.Concat(matTasks));

        var prices  = priceTasks.Select(t => t.Result?.BestSellOrderCity?.BuyAt ?? 0).ToArray();
        var matPrices = matTasks.Select(t => t.Result?.BestBuyCity?.BuyAt ?? 0).ToArray();
        var matCities = matTasks.Select(t => t.Result?.BestBuyCity?.City ?? "").ToArray();

        EnchantStatusText.IsVisible = false;
        EnchantResultPanel.Children.Clear();

        var itemName = _itemDatabase.GetNameById(itemId) ?? itemId;
        var suffixes = new[] { ".0", ".1", ".2", ".3" };
        var matNames = new[] { "Runa", "Alma", "Reliquia" };

        // Header
        var hdr = new TextBlock
        {
            Text = $"{itemName}  T{tier}",
            Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        EnchantResultPanel.Children.Add(hdr);

        for (var level = 0; level <= 3; level++)
        {
            var price = prices[level];

            // Cost to reach this level from .0
            double matCostFromBase = 0;
            for (var m = 0; m < level; m++) matCostFromBase += matPrices[m];

            double profitFromBase = level == 0 ? 0 : price * 0.92 - prices[0] - matCostFromBase;

            var bg = level == 0
                ? Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)
                : profitFromBase > 0
                    ? Color.FromArgb(0x1A, 0x4C, 0xAF, 0x50)
                    : Color.FromArgb(0x15, 0xFF, 0x44, 0x44);

            var row = new Border
            {
                Background = new SolidColorBrush(bg),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 5),
                Margin = new Thickness(0, 0, 0, 3),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lvlBlock = new TextBlock
            {
                Text = suffixes[level], Foreground = Brushes.White, FontSize = 11,
                FontWeight = FontWeight.SemiBold, Width = 26
            };
            lvlBlock.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Grid.SetColumn(lvlBlock, 0);

            string midText;
            if (level == 0)
                midText = "";
            else
            {
                var matPrice = matPrices[level - 1];
                midText = matPrice > 0
                    ? $"{matNames[level - 1]} {matCities[level - 1]}: {matPrice:N0}"
                    : $"{matNames[level - 1]}: sin datos";
            }
            var midBlock = new TextBlock
            {
                Text = midText, Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136)),
                FontSize = 9, Margin = new Thickness(4, 0)
            };
            midBlock.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Grid.SetColumn(midBlock, 1);

            var rightStack = new StackPanel();
            rightStack.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            var priceBlock = new TextBlock
            {
                Text = price > 0 ? $"{price:N0}" : "—",
                Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                FontSize = 11, FontWeight = FontWeight.Bold
            };
            rightStack.Children.Add(priceBlock);
            if (level > 0)
            {
                var profitBlock = new TextBlock
                {
                    Text = profitFromBase != 0
                        ? $"{(profitFromBase >= 0 ? "+" : "")}{profitFromBase:N0}"
                        : "—",
                    Foreground = profitFromBase >= 0
                        ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
                        : new SolidColorBrush(Color.FromRgb(255, 68, 68)),
                    FontSize = 9
                };
                rightStack.Children.Add(profitBlock);
            }
            Grid.SetColumn(rightStack, 2);

            grid.Children.Add(lvlBlock);
            grid.Children.Add(midBlock);
            grid.Children.Add(rightStack);
            row.Child = grid;
            EnchantResultPanel.Children.Add(row);
        }
        ForceResizeWindow();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ROUTE CALCULATOR
    // ══════════════════════════════════════════════════════════════════════════

    private void InitRoutePanel()
    {
        RouteCalcRow.IsVisible = false;
        RouteStatusText.IsVisible = false;
    }

    private void RouteAddItem_Click(object? sender, RoutedEventArgs e)
    {
        var name = RouteItemInput.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (!int.TryParse(RouteQtyInput.Text?.Trim(), out var qty) || qty < 1) qty = 1;

        var matches = _itemDatabase.Search(name);
        if (matches.Count == 0) return;

        var item = matches[0];
        var displayName = _itemDatabase.GetNameById(item) ?? item;

        _routeItems.Add((item, displayName, qty));
        RouteItemInput.Text  = "";
        RouteQtyInput.Text   = "1";
        RouteResultPanel.Children.Clear();
        RebuildRouteItemsUI();
        ForceResizeWindow();
    }

    private void RebuildRouteItemsUI()
    {
        RouteItemsPanel.Children.Clear();
        for (var i = 0; i < _routeItems.Count; i++)
        {
            var idx  = i;
            var item = _routeItems[i];
            var row  = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBlock = new TextBlock
            {
                Text = $"×{item.Qty}  {item.Name}", Foreground = Brushes.White,
                FontSize = 10
            };
            nameBlock.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Grid.SetColumn(nameBlock, 0);

            var removeBtn = new Button
            {
                Content = "×", Width = 22, Height = 22, Padding = new Thickness(0),
                MinHeight = 0, Margin = new Thickness(4, 0, 0, 0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102)), FontSize = 12
            };
            removeBtn.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            removeBtn.VerticalContentAlignment   = Avalonia.Layout.VerticalAlignment.Center;
            removeBtn.Click += (_, _) =>
            {
                _routeItems.RemoveAt(idx);
                RouteResultPanel.Children.Clear();
                RebuildRouteItemsUI();
                ForceResizeWindow();
            };
            Grid.SetColumn(removeBtn, 2);

            row.Children.Add(nameBlock);
            row.Children.Add(removeBtn);
            RouteItemsPanel.Children.Add(row);
        }

        RouteCalcRow.IsVisible = _routeItems.Count > 0;
        RouteItemCountText.Text = $"{_routeItems.Count} item(s)";
    }

    private async void RouteCalc_Click(object? sender, RoutedEventArgs e) => await RouteCalculateAsync();

    private async Task RouteCalculateAsync()
    {
        if (_routeItems.Count == 0) return;

        RouteStatusText.Text      = "Consultando precios...";
        RouteStatusText.IsVisible = true;
        RouteResultPanel.Children.Clear();

        var tasks = _routeItems
            .Select(it => _apiService.GetItemPriceAsync(it.Id))
            .ToList();
        await Task.WhenAll(tasks);

        var summaries = tasks.Select(t => t.Result).ToList();

        // Collect all known cities
        var cities = summaries
            .Where(s => s != null)
            .SelectMany(s => s!.Prices.Select(p => p.City))
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        // For each city, compute total revenue
        var cityTotals = cities.Select(city =>
        {
            double total = 0;
            var breakdown = new List<(string Name, double Value)>();
            for (var i = 0; i < _routeItems.Count; i++)
            {
                var s = summaries[i];
                var price = s?.Prices.FirstOrDefault(p => p.City == city)?.BuyAt ?? 0;
                var value = price * _routeItems[i].Qty * 0.92;
                total += value;
                breakdown.Add((_routeItems[i].Name, value));
            }
            return (City: city, Total: total, Breakdown: breakdown);
        })
        .Where(c => c.Total > 0)
        .OrderByDescending(c => c.Total)
        .ToList();

        RouteStatusText.IsVisible = false;

        if (cityTotals.Count == 0)
        {
            RouteStatusText.Text      = "Sin datos de precio para los items.";
            RouteStatusText.IsVisible = true;
            return;
        }

        var best = cityTotals[0].Total;
        foreach (var (city, total, breakdown) in cityTotals.Take(5))
        {
            var pct = best > 0 ? total / best * 100 : 0;
            var isFirst = city == cityTotals[0].City;

            var cityBorder = new Border
            {
                Background    = isFirst
                    ? new SolidColorBrush(Color.FromArgb(0x28, 0x4C, 0xAF, 0x50))
                    : new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
                CornerRadius  = new CornerRadius(4),
                Padding       = new Thickness(8, 6),
                Margin        = new Thickness(0, 0, 0, 4),
            };
            var sp = new StackPanel();

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cityBlock = new TextBlock
            {
                Text = city + (isFirst ? "  ★" : ""),
                Foreground = isFirst
                    ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
                    : Brushes.White,
                FontSize = 11, FontWeight = FontWeight.SemiBold
            };
            Grid.SetColumn(cityBlock, 0);

            var totalBlock = new TextBlock
            {
                Text = $"{total:N0}",
                Foreground = isFirst
                    ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
                    : Brushes.White,
                FontSize = 11, FontWeight = FontWeight.Bold
            };
            Grid.SetColumn(totalBlock, 1);

            header.Children.Add(cityBlock);
            header.Children.Add(totalBlock);
            sp.Children.Add(header);

            if (!isFirst)
            {
                var diff = new TextBlock
                {
                    Text = $"-{best - total:N0} vs mejor",
                    Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136)),
                    FontSize = 8, Margin = new Thickness(0, 1, 0, 0)
                };
                sp.Children.Add(diff);
            }

            // Per-item breakdown (only for best city or if > 1 item)
            if (isFirst && _routeItems.Count > 1)
            {
                foreach (var (name, value) in breakdown.Where(b => b.Value > 0))
                {
                    var lineBlock = new TextBlock
                    {
                        Text = $"  {name}: {value:N0}",
                        Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136)),
                        FontSize = 9
                    };
                    sp.Children.Add(lineBlock);
                }
            }

            cityBorder.Child = sp;
            RouteResultPanel.Children.Add(cityBorder);
        }
        ForceResizeWindow();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SHARED HELPER — small toggle button
    // ══════════════════════════════════════════════════════════════════════════

    private static Button MakeSmallToggleBtn(string label, bool active, EventHandler<RoutedEventArgs> handler)
    {
        var btn = new Button
        {
            Content = label,
            Height = 22, Padding = new Thickness(8, 0), MinHeight = 0,
            BorderThickness = new Thickness(1),
            FontSize = 9, Margin = new Thickness(0, 0, 3, 3),
        };
        btn.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        btn.VerticalContentAlignment   = Avalonia.Layout.VerticalAlignment.Center;
        SetToggleBtnStyle(btn, active);
        btn.Click += handler;
        return btn;
    }

    private static void SetToggleBtnStyle(Button btn, bool active)
    {
        btn.Background   = active
            ? new SolidColorBrush(Color.FromRgb(255, 117, 80))
            : Brushes.Transparent;
        btn.Foreground   = active ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(102, 102, 102));
        btn.BorderBrush  = active
            ? new SolidColorBrush(Color.FromRgb(255, 117, 80))
            : new SolidColorBrush(Color.FromRgb(51, 51, 51));
    }

    private static void HighlightToggleGroup(WrapPanel panel, Button? active)
    {
        foreach (var child in panel.Children.OfType<Button>())
            SetToggleBtnStyle(child, child == active);
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => Hide();

    // ══════════════════════════════════════════════════════════════════════════
    // ISLAND / FARMING TAB
    // ══════════════════════════════════════════════════════════════════════════

    private bool _islandTabInitialized;

    private void InitIslandTab()
    {
        _islandTabInitialized = true;
        _farmingGuide.LoadRecipes();

        // Build city selector buttons
        _newIslandCityBtns = FarmingGuideService.AllCities.Select(city =>
        {
            var btn = MakeIslandFilterBtn(city, 9);
            btn.Click += (_, _) =>
            {
                _newIslandCity = city;
                HighlightIslandBtn(_newIslandCityBtns!, btn);
            };
            return btn;
        }).ToArray();

        foreach (var b in _newIslandCityBtns) NewIslandCityPanel.Children.Add(b);

        // Build tier selector buttons (T2–T6)
        _newIslandTierBtns = Enumerable.Range(2, 5).Select(t =>
        {
            var btn = MakeIslandFilterBtn($"T{t}", 9);
            btn.Click += (_, _) =>
            {
                _newIslandTier = t;
                HighlightIslandBtn(_newIslandTierBtns!, btn);
            };
            return btn;
        }).ToArray();

        foreach (var b in _newIslandTierBtns) NewIslandTierPanel.Children.Add(b);
        HighlightIslandBtn(_newIslandTierBtns, _newIslandTierBtns[2]); // T4 default
        _newIslandTier = 4;

        // Restore persisted islands
        var islands = (Application.Current as App)?.Settings.Islands ?? [];
        foreach (var cfg in islands.ToList()) AddIslandCardToPanel(cfg);
        RefreshSynergiesPanel();
    }

    private void AddIsland_Click(object? sender, RoutedEventArgs e)
    {
        AddIslandForm.IsVisible = true;
        AddIslandBtn.IsEnabled  = false;
        _newIslandCity          = null;
        if (_newIslandCityBtns != null)
            HighlightIslandBtn(_newIslandCityBtns, null);
    }

    private void AddIslandCancel_Click(object? sender, RoutedEventArgs e)
    {
        AddIslandForm.IsVisible = false;
        AddIslandBtn.IsEnabled  = true;
        _newIslandCity          = null;
    }

    private void AddIslandConfirm_Click(object? sender, RoutedEventArgs e)
    {
        if (_newIslandCity == null) return;

        var cfg = new IslandConfig { City = _newIslandCity, Tier = _newIslandTier };
        var app = Application.Current as App;
        app?.Settings.Islands.Add(cfg);
        app?.Settings.Save();

        AddIslandCardToPanel(cfg);
        RefreshSynergiesPanel();

        AddIslandForm.IsVisible = false;
        AddIslandBtn.IsEnabled  = true;
        _newIslandCity          = null;
    }

    private void AddIslandCardToPanel(IslandConfig cfg)
    {
        var recoPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        recoPanel.Children.Add(new TextBlock
        {
            Text       = "Presioná \"Ver precios\" para cargar recomendaciones",
            Foreground = new SolidColorBrush(Color.Parse("#506070")),
            FontSize   = 8,
            FontStyle  = Avalonia.Media.FontStyle.Italic,
        });

        var headerStack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };

        var titleTb = new TextBlock
        {
            Text       = $"{cfg.City}  T{cfg.Tier}  ({cfg.Plots} parcelas)",
            Foreground = new SolidColorBrush(Color.Parse("#FF7550")),
            FontSize   = 10,
            FontWeight = FontWeight.Bold,
        };

        var craftBonus = _farmingGuide.GetCraftingBonus(cfg.City);
        if (craftBonus != null)
        {
            titleTb.Text += "  ✦";
            ToolTip.SetTip(titleTb, $"Bonus de crafteo: {craftBonus}");
        }

        var removeBtn = new Button
        {
            Content         = "×",
            Background      = Brushes.Transparent,
            Foreground      = new SolidColorBrush(Color.Parse("#FF6060")),
            BorderThickness = new Thickness(0),
            Padding         = new Thickness(0),
            Margin          = new Thickness(8, 0, 0, 0),
            FontSize        = 14,
            Cursor          = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var card = new Border
        {
            Width           = 375,
            Background      = new SolidColorBrush(Color.Parse("#18FFFFFF")),
            BorderBrush     = new SolidColorBrush(Color.Parse("#FF7550")),
            BorderThickness = new Thickness(2, 0, 0, 0),
            CornerRadius    = new CornerRadius(0, 4, 4, 0),
            Padding         = new Thickness(10, 8),
            Margin          = new Thickness(0, 0, 10, 10),
        };

        removeBtn.Click += (_, _) => RemoveIslandCard(cfg, card);

        headerStack.Children.Add(titleTb);
        headerStack.Children.Add(removeBtn);

        // Avoid section
        var avoidItems = _farmingGuide.GetAvoid(cfg.City);
        var avoidPanel = new StackPanel
        {
            Margin     = new Thickness(0, 8, 0, 0),
            Background = new SolidColorBrush(Color.Parse("#0AFF4444")),
        };
        avoidPanel.Children.Add(new TextBlock
        {
            Text       = "⊘  NO PLANTAR:",
            Foreground = new SolidColorBrush(Color.Parse("#E57373")),
            FontSize   = 8,
            FontWeight = FontWeight.Bold,
            Margin     = new Thickness(0, 0, 0, 3),
        });
        foreach (var item in avoidItems)
            avoidPanel.Children.Add(new TextBlock
            {
                Text       = $"  • {item}",
                Foreground = new SolidColorBrush(Color.Parse("#8090A0")),
                FontSize   = 8,
            });

        var mainStack = new StackPanel();
        mainStack.Children.Add(headerStack);
        mainStack.Children.Add(recoPanel);
        mainStack.Children.Add(avoidPanel);
        card.Child = mainStack;

        _islandCards.Add((cfg, recoPanel, card));
        IslandCardsPanel.Children.Add(card);
        if (IslandModeContent.IsVisible) UpdateIslandWindowWidth();
    }

    private void RemoveIslandCard(IslandConfig cfg, Border card)
    {
        IslandCardsPanel.Children.Remove(card);
        _islandCards.RemoveAll(x => x.Card == card);

        var app = Application.Current as App;
        app?.Settings.Islands.RemoveAll(i => i.City == cfg.City && i.Tier == cfg.Tier);
        app?.Settings.Save();
        RefreshSynergiesPanel();
        UpdateIslandWindowWidth();
    }

    private async void IslandFetchPrices_Click(object? sender, RoutedEventArgs e)
    {
        if (_islandCards.Count == 0) return;

        IslandFetchPricesBtn.IsEnabled = false;
        IslandFetchPricesBtn.Content   = "Cargando…";

        try
        {
            var configs  = _islandCards.Select(x => x.Config).ToList();
            var itemIds  = _farmingGuide.GetAllItemIds(configs).ToList();
            var api      = _apiService;

            var raw = await api.GetBatchPricesAsync(itemIds);
            if (raw == null || raw.Count == 0) return;

            var priceMap = raw
                .Where(p => p.SellPriceMin > 0 && p.ItemId != null && p.City != null)
                .GroupBy(p => (p.ItemId!, p.City!))
                .ToDictionary(g => g.Key, g => g.Min(p => p.SellPriceMin ?? 0));

            foreach (var (cfg, recoPanel, _) in _islandCards)
                PopulateIslandCard(cfg, recoPanel, priceMap);

            RefreshSynergiesPanel(priceMap);
        }
        finally
        {
            IslandFetchPricesBtn.IsEnabled = true;
            IslandFetchPricesBtn.Content   = "Ver precios";
        }
    }

    private void PopulateIslandCard(
        IslandConfig cfg,
        StackPanel   recoPanel,
        Dictionary<(string itemId, string city), double> priceMap)
    {
        recoPanel.Children.Clear();

        var bonuses = _farmingGuide.GetBonuses(cfg.City);
        if (bonuses.Count == 0) return;

        recoPanel.Children.Add(new TextBlock
        {
            Text       = "✦  PLANTAR / CRIAR:",
            Foreground = new SolidColorBrush(Color.Parse("#4CAF50")),
            FontSize   = 8,
            FontWeight = FontWeight.Bold,
            Margin     = new Thickness(0, 0, 0, 4),
        });

        foreach (var bonus in bonuses)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var prioColor = bonus.Priority == FarmPriority.Alta ? "#5DC961" : "#8AA0B0";

            var nameText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.Parse(prioColor)),
                FontSize   = 9,
                Margin     = new Thickness(0, 1, 0, 0),
            };

            if (bonus.Type == FarmItemType.Animal)
            {
                nameText.Text = $"{bonus.NameEs} T{bonus.Tier}";
                if (!string.IsNullOrEmpty(bonus.Notes))
                    ToolTip.SetTip(nameText, bonus.Notes);
            }
            else
            {
                nameText.Text = $"{bonus.NameEs} T{bonus.Tier}  [{bonus.Type}]";
                if (!string.IsNullOrEmpty(bonus.Notes))
                    ToolTip.SetTip(nameText, bonus.Notes);
            }

            Grid.SetColumn(nameText, 0);

            // Price column
            var priceStack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            var sellPrice  = GetCityPrice(priceMap, bonus.ItemId, cfg.City);

            if (bonus.Type == FarmItemType.Animal && bonus.ProductItemId != null)
            {
                var productPrice = GetCityPrice(priceMap, bonus.ProductItemId, cfg.City);
                var priceText    = productPrice > 0
                    ? $"{bonus.ProductNameEs}: {productPrice:N0}s"
                    : $"{bonus.ProductNameEs}: —";

                priceStack.Children.Add(new TextBlock
                {
                    Text       = priceText,
                    Foreground = new SolidColorBrush(Color.Parse("#FFD700")),
                    FontSize   = 8,
                });

                if (bonus.HasButcherBonus)
                    priceStack.Children.Add(new TextBlock
                    {
                        Text       = "  +10% carne",
                        Foreground = new SolidColorBrush(Color.Parse("#FF8A65")),
                        FontSize   = 7,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    });
            }
            else if (sellPrice > 0)
            {
                priceStack.Children.Add(new TextBlock
                {
                    Text       = $"{sellPrice:N0}s",
                    Foreground = new SolidColorBrush(Color.Parse("#FFD700")),
                    FontSize   = 9,
                });
            }

            Grid.SetColumn(priceStack, 1);
            row.Children.Add(nameText);
            row.Children.Add(priceStack);
            recoPanel.Children.Add(row);

            // Animal food hint
            if (bonus.Type == FarmItemType.Animal && bonus.FavFoodId != null)
            {
                var configs   = _islandCards.Select(x => x.Config).ToList();
                var ownFood   = configs.Any(c => c.City == bonus.FoodBonusCity && c.City != cfg.City);
                var foodPrice = GetCityPrice(priceMap, bonus.FavFoodId, bonus.FoodBonusCity ?? cfg.City);
                var foodBonus = _farmingGuide.GetBonuses(bonus.FoodBonusCity ?? "")
                    .FirstOrDefault(b => b.ItemId == bonus.FavFoodId)?.NameEs ?? bonus.FavFoodId;

                var hint = ownFood
                    ? $"  ★ Comida favorita: {foodBonus} de tu isla en {bonus.FoodBonusCity} (+10%) → mitad de consumo"
                    : $"  ↳ Comida favorita: {foodBonus} (bonus en {bonus.FoodBonusCity}){(foodPrice > 0 ? $" ~ {foodPrice:N0}s/ud." : "")}";

                recoPanel.Children.Add(new TextBlock
                {
                    Text       = hint,
                    Foreground = new SolidColorBrush(Color.Parse(ownFood ? "#4CAF50" : "#7890A8")),
                    FontSize   = 7,
                    Margin     = new Thickness(0, 0, 0, 2),
                });
            }
        }
    }

    private void RefreshSynergiesPanel(
        Dictionary<(string itemId, string city), double>? priceMap = null)
    {
        var configs    = _islandCards.Select(x => x.Config).ToList();
        var synergies  = _farmingGuide.DetectSynergies(configs);

        IslandSynergiesPanel.IsVisible = synergies.Count > 0;
        SynergyItemsPanel.Children.Clear();

        foreach (var syn in synergies)
        {
            var accentColor = syn.IsFullyCovered ? "#4CAF50" : "#FF8A65";
            var bgColor     = syn.IsFullyCovered ? "#0C1F2B18" : "#0C2B1E10";
            var icon        = syn.IsFullyCovered ? "✓" : "◑";

            var titleRow = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 0, 4),
            };
            titleRow.Children.Add(new Border
            {
                Background    = new SolidColorBrush(Color.Parse(accentColor)),
                CornerRadius  = new CornerRadius(2),
                Padding       = new Thickness(4, 1),
                Margin        = new Thickness(0, 0, 6, 0),
                Child         = new TextBlock
                {
                    Text       = icon,
                    Foreground = Brushes.White,
                    FontSize   = 8,
                    FontWeight = FontWeight.Bold,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                },
            });
            titleRow.Children.Add(new TextBlock
            {
                Text       = syn.Title,
                Foreground = new SolidColorBrush(Color.Parse(accentColor)),
                FontSize   = 9,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });

            var synBorder = new Border
            {
                Background      = new SolidColorBrush(Color.Parse(bgColor)),
                BorderBrush     = new SolidColorBrush(Color.Parse(accentColor)),
                BorderThickness = new Thickness(2, 0, 0, 0),
                CornerRadius    = new CornerRadius(0, 4, 4, 0),
                Padding         = new Thickness(10, 7, 8, 8),
                Margin          = new Thickness(0, 0, 0, 6),
            };

            var synStack = new StackPanel();
            synStack.Children.Add(titleRow);
            synStack.Children.Add(new TextBlock
            {
                Text         = syn.Description,
                Foreground   = new SolidColorBrush(Color.Parse("#A0B4C4")),
                FontSize     = 8,
                TextWrapping = TextWrapping.Wrap,
            });

            // Show recipe output price if available
            if (priceMap != null && syn.Recipe != null)
            {
                var outputPrice = priceMap
                    .Where(kv => kv.Key.itemId == syn.Recipe.OutputId)
                    .Select(kv => kv.Value)
                    .DefaultIfEmpty(0)
                    .Max();

                if (outputPrice > 0)
                    synStack.Children.Add(new TextBlock
                    {
                        Text       = $"Output: {outputPrice:N0}s",
                        Foreground = new SolidColorBrush(Color.Parse("#FFD700")),
                        FontSize   = 8,
                        FontWeight = FontWeight.Bold,
                        Margin     = new Thickness(0, 4, 0, 0),
                    });
            }

            synBorder.Child = synStack;
            SynergyItemsPanel.Children.Add(synBorder);
        }
    }

    private static double GetCityPrice(
        Dictionary<(string itemId, string city), double> map,
        string itemId, string city)
    {
        map.TryGetValue((itemId, city), out var price);
        return price;
    }

    private static Button MakeIslandFilterBtn(string label, int fontSize = 9)
        => new()
        {
            Content         = label,
            Height          = 22,
            FontSize        = fontSize,
            Background      = Brushes.Transparent,
            Foreground      = new SolidColorBrush(Color.Parse("#8AABB8")),
            BorderThickness = new Thickness(1),
            BorderBrush     = new SolidColorBrush(Color.Parse("#3A4A58")),
            CornerRadius    = new CornerRadius(3),
            Padding         = new Thickness(8, 0),
            Margin          = new Thickness(0, 0, 5, 5),
        };

    private void UpdateIslandWindowWidth()
    {
        ApplyOverlaySize();
        CenterOnPrimaryScreen();
    }

    private static void HighlightIslandBtn(Button[] btns, Button? active)
    {
        foreach (var b in btns)
        {
            b.Background = active == b
                ? new SolidColorBrush(Color.Parse("#FF7550"))
                : Brushes.Transparent;
            b.Foreground = active == b
                ? Brushes.White
                : new SolidColorBrush(Color.Parse("#666"));
        }
    }

    private void UpdateBanner_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        var svc = (Application.Current as App)?.UpdateService;
        if (svc?.IsUpdateAvailable != true) return;
        var url = svc.DownloadUrl ?? svc.ReleasePageUrl
            ?? "https://github.com/EstebanLemes/AlbionPricesOverlay/releases/latest";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}

public record CombatEventItem(KillEvent Event, string PlayerId, string PlayerName, string PlayerGuild, string TimeAgo)
{
    public string VictimName => PlayerName;
    public string VictimGuild => PlayerGuild;
    public string KillerName => PlayerName;
    public string KillerGuild => PlayerGuild;
}
public record SearchSuggestion(string ItemId, string ItemName, string Badge, bool Starred);


