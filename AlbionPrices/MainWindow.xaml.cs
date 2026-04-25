using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AlbionPrices.Helpers;
using AlbionPrices.Models;
using AlbionPrices.Services;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using Point = System.Windows.Point;

namespace AlbionPrices;

public partial class MainWindow : Window
{
    private readonly AlbionApiService _apiService;
    private readonly ItemDatabase _itemDatabase;
    private Button[]? _regionButtons;
    private GlobalHotkey? _hotkey;
    private bool _isLoading;
    private bool _dbLoaded;
    private NotifyIcon? _notifyIcon;
    private System.Windows.Threading.DispatcherTimer? _hideTimer;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _playerCts;
    private CancellationTokenSource? _setValueCts;

    private string? _baseId;
    private int _currentTier;
    private int _currentEnchant;
    private int _currentQuality = 1;
    private Dictionary<int, List<int>> _variants = new();
    private string? _currentItemId;
    private string? _currentItemName;

    private List<PriceHistoryPoint>? _sparklineData;

    private readonly Dictionary<string, CityPriceViewModel> _cityViewModels = new();

    private static readonly Regex TieredItemRegex =
        new(@"^T(\d)_(.+?)(?:@(\d))?$", RegexOptions.Compiled);

    public MainWindow()
    {
        InitializeComponent();
        Icon = IconHelper.CreateWindowIcon();
        _apiService   = (System.Windows.Application.Current as App)?.AlbionApiService ?? new AlbionApiService();
        _itemDatabase = new ItemDatabase();

        Loaded      += MainWindow_Loaded;
        Closed      += MainWindow_Closed;
        Deactivated += MainWindow_Deactivated;

        SparklineCanvas.SizeChanged += (_, _) =>
        {
            if (_sparklineData != null) DrawSparkline(_sparklineData);
        };

        _hideTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _hideTimer.Tick += HideTimer_Tick;

        var rt = (System.Windows.Application.Current as App)?.RealtimeService;
        if (rt != null)
        {
            rt.PriceUpdated      += OnRealtimePriceUpdated;
            rt.ConnectionChanged += OnRealtimeConnectionChanged;
        }

        Loaded += async (s, e) =>
        {
            _regionButtons = [RegionNABtn, RegionEUBtn, RegionASBtn];
            var savedRegion = (System.Windows.Application.Current as App)?.Settings.Region
                              ?? AlbionPrices.Models.ServerRegion.Europe;
            RefreshRegionButtons(savedRegion);

            if (_dbLoaded) return;
            _dbLoaded = true;
            StatusText.Text = "Descargando base de datos...";
            await _itemDatabase.LoadAsync();
            StatusText.Text = _itemDatabase.ItemCount == 0
                ? $"ERROR DB: {_itemDatabase.LoadError}"
                : $"{_itemDatabase.ItemCount:N0} items cargados. Escribí el nombre del item.";

            RefreshHistoryUI();
            _ = LoadGoldPriceAsync();

            try
            {
                var updateService = (System.Windows.Application.Current as App)?.UpdateService;
                if (updateService != null)
                {
                    await updateService.CheckForUpdateAsync();
                    if (updateService.IsUpdateAvailable)
                    {
                        UpdateBanner.Text = $"Nueva version disponible: v{updateService.LatestVersion}";
                        UpdateBanner.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update check error: {ex.Message}");
            }
        };
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _hotkey = new GlobalHotkey(this, 9000);
        if (_hotkey.Register()) _hotkey.HotkeyPressed += OnHotkeyPressed;
    }

    private void MainWindow_Closed(object? sender, EventArgs e) => _hotkey?.Dispose();

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (IsVisible && !_isLoading) _hideTimer?.Start();
    }

    private void HideTimer_Tick(object? sender, EventArgs e)
    {
        _hideTimer?.Stop();
        Hide();
    }

    public void SetNotifyIcon(NotifyIcon notifyIcon) => _notifyIcon = notifyIcon;

    internal void ShowCentered()
    {
        if (IsVisible && IsLoaded) { Activate(); Focus(); return; }
        Left = (SystemParameters.PrimaryScreenWidth  - Width)  / 2;
        Top  = (SystemParameters.PrimaryScreenHeight - Height) / 2;
        Show(); Activate(); Focus();
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        _hideTimer?.Stop();
        ShowCentered();
    }

    // ── Mode toggle ──────────────────────────────────────────────────────────

    private void PriceMode_Click(object sender, RoutedEventArgs e)
    {
        PriceModeContent.Visibility  = Visibility.Visible;
        PlayerModeContent.Visibility = Visibility.Collapsed;
        PriceModeBtn.Background  = new SolidColorBrush(Color.FromRgb(255, 117, 80));
        PriceModeBtn.Foreground  = Brushes.White;
        PlayerModeBtn.Background = Brushes.Transparent;
        PlayerModeBtn.Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102));
        (System.Windows.Application.Current as App)?.RealtimeService?.SetItem(_currentItemId);
    }

    private void PlayerMode_Click(object sender, RoutedEventArgs e)
    {
        PlayerModeContent.Visibility = Visibility.Visible;
        PriceModeContent.Visibility  = Visibility.Collapsed;
        PlayerModeBtn.Background = new SolidColorBrush(Color.FromRgb(255, 117, 80));
        PlayerModeBtn.Foreground = Brushes.White;
        PriceModeBtn.Background  = Brushes.Transparent;
        PriceModeBtn.Foreground  = new SolidColorBrush(Color.FromRgb(102, 102, 102));
        (System.Windows.Application.Current as App)?.RealtimeService?.SetItem(null);
        PlayerInput.Focus();
    }

    // ── Gold price ───────────────────────────────────────────────────────────

    private async Task LoadGoldPriceAsync()
    {
        var gold = await _apiService.GetGoldPriceAsync();
        if (gold != null)
        {
            GoldPriceText.Text = $"{gold.Price:N0}";
            GoldPriceRow.Visibility = Visibility.Visible;
        }
    }

    // ── History & Favorites UI ────────────────────────────────────────────────

    private void RefreshHistoryUI()
    {
        var hist = (System.Windows.Application.Current as App)?.HistoryService;
        if (hist == null) return;

        RecentSearchChips.Children.Clear();
        foreach (var entry in hist.RecentItems)
        {
            var e = entry;
            var chip = MakeChip(e.ItemName, async () => await CheckItemById(e.ItemId, e.ItemName));
            RecentSearchChips.Children.Add(chip);
        }
        RecentSearchesSection.Visibility = hist.RecentItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        FavoriteChips.Children.Clear();
        foreach (var entry in hist.Favorites)
        {
            var e = entry;
            var chip = MakeChip($"★ {e.ItemName}", async () => await CheckItemById(e.ItemId, e.ItemName), starred: true);
            FavoriteChips.Children.Add(chip);
        }
        FavoritesSection.Visibility = hist.Favorites.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Button MakeChip(string label, Func<Task> onClick, bool starred = false)
    {
        var btn = new Button
        {
            Content         = label,
            FontSize        = 9,
            Height          = 20,
            Padding         = new Thickness(7, 0, 7, 0),
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

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        (System.Windows.Application.Current as App)?.HistoryService?.ClearHistory();
        RefreshHistoryUI();
    }

    private void FavoriteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentItemId == null || _currentItemName == null) return;
        var hist = (System.Windows.Application.Current as App)?.HistoryService;
        if (hist == null) return;
        var isFav = hist.ToggleFavorite(_currentItemId, _currentItemName);
        FavoriteBtn.Content = isFav ? "★" : "☆";
        RefreshHistoryUI();
    }

    // ── NATS real-time ───────────────────────────────────────────────────────

    private void OnRealtimePriceUpdated(object? sender, PriceApiResponse msg)
    {
        if (msg.City is null) return;
        Dispatcher.Invoke(() =>
        {
            if (!_cityViewModels.TryGetValue(msg.City, out var vm)) return;
            if (msg.SellPriceMin > 0) { vm.BuyAt = msg.SellPriceMin.Value; vm.BuyAtDate = msg.SellPriceMinDate ?? DateTime.UtcNow; }
            if (msg.BuyPriceMax  > 0) { vm.SellAt = msg.BuyPriceMax.Value; vm.SellAtDate = msg.BuyPriceMaxDate ?? DateTime.UtcNow; }
        });
    }

    private void OnRealtimeConnectionChanged(object? sender, bool connected)
    {
        Dispatcher.Invoke(() =>
        {
            LiveDot.Fill    = connected
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
                : new SolidColorBrush(Color.FromRgb(85, 85, 85));
            LiveDot.ToolTip = connected ? "En vivo — datos en tiempo real" : "Sin conexión en tiempo real";
        });
    }

    // ── Price mode ───────────────────────────────────────────────────────────

    private void DisplayPriceInfo(ItemPriceSummary summary, bool setupTierEnchant = true)
    {
        StatusText.Visibility = Visibility.Collapsed;
        ItemInfoPanel.Visibility = Visibility.Visible;

        ItemNameText.Text = summary.ItemName;
        ItemIdText.Text   = summary.ItemId;
        _currentItemId    = summary.ItemId;
        _currentItemName  = summary.ItemName;

        // Favorite button state
        var hist = (System.Windows.Application.Current as App)?.HistoryService;
        FavoriteBtn.Content = hist?.IsFavorite(summary.ItemId) == true ? "★" : "☆";

        // Item icon
        try
        {
            ItemIcon.Source = new BitmapImage(new Uri(GameInfoService.GetItemIconUrl(summary.ItemId)));
        }
        catch { ItemIcon.Source = null; }

        if (setupTierEnchant)
        {
            SetupTierEnchant(summary.ItemId);
            SetupQualityButtons(summary.ItemId);
        }

        var bestBuy = summary.BestBuyCity;
        if (bestBuy != null)
        {
            BestBuyCityText.Text  = bestBuy.City;
            BestBuyPriceText.Text = $"{bestBuy.BuyAt:N0}";
        }

        var bestSell = summary.BestSellCity;
        if (bestSell != null)
        {
            BestSellCityText.Text  = bestSell.City;
            BestSellPriceText.Text = $"{bestSell.SellAt:N0}";
        }

        var vms = summary.Prices.Select(p => new CityPriceViewModel
        {
            City       = p.City,
            BuyAt      = p.BuyAt,
            BuyAtDate  = p.BuyAtDate,
            SellAt     = p.SellAt,
            SellAtDate = p.SellAtDate,
        }).ToList();

        _cityViewModels.Clear();
        foreach (var vm in vms) _cityViewModels[vm.City] = vm;
        CitiesList.ItemsSource = vms;

        DisplayFlipCalculator(summary);

        (System.Windows.Application.Current as App)?.RealtimeService?.SetItem(summary.ItemId);
    }

    // ── Flip calculator ──────────────────────────────────────────────────────

    private void DisplayFlipCalculator(ItemPriceSummary summary)
    {
        var buyCity  = summary.BestBuyCity;
        var sellCity = summary.BestSellCity;

        if (buyCity == null || sellCity == null || buyCity.BuyAt <= 0 || sellCity.SellAt <= 0)
        {
            FlipCalcPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var profit = sellCity.SellAt * 0.92 - buyCity.BuyAt;
        if (profit <= 0 || profit / buyCity.BuyAt < 0.02)
        {
            FlipCalcPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var margin = profit / buyCity.BuyAt * 100;
        FlipRouteText.Text  = buyCity.City == sellCity.City
            ? buyCity.City
            : $"{buyCity.City}  →  {sellCity.City}";
        FlipProfitText.Text = $"+{profit:N0}";
        FlipMarginText.Text = $"Margen: {margin:F1}%  (comprás {buyCity.BuyAt:N0}, vendés {sellCity.SellAt:N0}, tax 8%)";
        FlipCalcPanel.Visibility = Visibility.Visible;
    }

    // ── Price history sparkline ───────────────────────────────────────────────

    private async Task LoadPriceHistoryAsync(string itemId, int quality, CancellationToken ct)
    {
        var history = await _apiService.GetPriceHistoryAsync(itemId, quality, 24, ct);
        if (ct.IsCancellationRequested || history == null || history.Count == 0)
        {
            Dispatcher.Invoke(() => { PriceHistoryPanel.Visibility = Visibility.Collapsed; _sparklineData = null; });
            return;
        }

        // Pick the city with the most data points (most active market)
        var best = history
            .Where(h => h.Data?.Count > 3)
            .OrderByDescending(h => h.Data!.Count)
            .FirstOrDefault();

        if (best?.Data == null)
        {
            Dispatcher.Invoke(() => { PriceHistoryPanel.Visibility = Visibility.Collapsed; _sparklineData = null; });
            return;
        }

        var points = best.Data.OrderBy(p => p.Timestamp).TakeLast(14).ToList();

        Dispatcher.Invoke(() =>
        {
            SparklineTitleText.Text = $"HISTORIAL 14 DÍAS — {best.City}";
            _sparklineData = points;
            PriceHistoryPanel.Visibility = Visibility.Visible;
            PriceHistoryPanel.UpdateLayout();
            DrawSparkline(points);
        });
    }

    private void DrawSparkline(List<PriceHistoryPoint> points)
    {
        SparklineCanvas.Children.Clear();
        if (points.Count < 2) return;

        double w = SparklineCanvas.ActualWidth > 10 ? SparklineCanvas.ActualWidth : 370;
        double h = SparklineCanvas.ActualHeight > 10 ? SparklineCanvas.ActualHeight : 50;

        var prices = points.Select(p => (double)p.AvgPrice).ToList();
        var min    = prices.Min();
        var max    = prices.Max();
        var range  = max - min;
        if (range == 0) range = 1;

        // Fill area under curve
        var fillPoints = new PointCollection();
        fillPoints.Add(new Point(2, h));
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

        // Line
        var line = new Polyline
        {
            Stroke          = new SolidColorBrush(Color.FromRgb(255, 117, 80)),
            StrokeThickness = 1.5,
            Points          = new PointCollection(),
        };
        for (int i = 0; i < prices.Count; i++)
        {
            var x = 2 + i * (w - 4) / (prices.Count - 1);
            var y = h - 4 - (prices[i] - min) / range * (h - 8);
            line.Points.Add(new Point(x, y));
        }
        SparklineCanvas.Children.Add(line);

        // Min/max labels
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
        if (!m.Success) { TierEnchantPanel.Visibility = Visibility.Collapsed; return; }

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

        if (_variants.Count == 0) { TierEnchantPanel.Visibility = Visibility.Collapsed; return; }

        RebuildTierButtons();
        RebuildEnchantButtons();
        TierEnchantPanel.Visibility = Visibility.Visible;
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
            QualityPanel.Visibility = Visibility.Collapsed;
            return;
        }

        QualityButtonsPanel.Children.Clear();
        for (var q = 1; q <= 5; q++)
        {
            var quality = q;
            var isSelected = quality == 1;
            var btn = new Button
            {
                Content = QualityLabels[q - 1],
                Height = 22, Padding = new Thickness(6, 0, 6, 0),
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
        QualityPanel.Visibility = Visibility.Visible;
    }

    private static void PopulateButtons(WrapPanel panel, string[] labels, string selected,
        Func<string, Task> onClick)
    {
        panel.Children.Clear();
        foreach (var label in labels)
        {
            var isSelected = label == selected;
            var btn = new Button
            {
                Content = label, Width = 30, Height = 22,
                Margin = new Thickness(2, 0, 2, 0), FontSize = 10, BorderThickness = new Thickness(1),
                Background = isSelected
                    ? new SolidColorBrush(Color.FromRgb(255, 117, 80))
                    : new SolidColorBrush(Color.FromRgb(25, 25, 30)),
                Foreground = isSelected
                    ? Brushes.White
                    : new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromRgb(255, 117, 80))
                    : new SolidColorBrush(Color.FromRgb(60, 60, 65)),
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
            StatusText.Text = "Actualizando...";
            StatusText.Visibility = Visibility.Visible;
            ErrorText.Visibility  = Visibility.Collapsed;
            BestBuyCityText.Text = BestBuyPriceText.Text = BestSellCityText.Text = BestSellPriceText.Text = "…";
            CitiesList.ItemsSource = null;

            var ct = _cts.Token;
            var summary = await _apiService.GetItemPriceAsync(itemId, _currentQuality, ct);
            StatusText.Visibility = Visibility.Collapsed;

            if (summary == null || summary.Prices.Count == 0)
            {
                ErrorText.Text = $"Sin datos para: {itemId}";
                ErrorText.Visibility = Visibility.Visible;
                BestBuyCityText.Text = BestBuyPriceText.Text = BestSellCityText.Text = BestSellPriceText.Text = "—";
                return;
            }

            summary.ItemName = _itemDatabase.GetNameById(itemId) ?? ItemNameText.Text;
            DisplayPriceInfo(summary, setupTierEnchant: false);
            _ = LoadPriceHistoryAsync(itemId, _currentQuality, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText.Visibility = Visibility.Collapsed;
            ErrorText.Text = $"Error: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
        }
        finally { _isLoading = false; }
    }

    // ── Player mode ──────────────────────────────────────────────────────────

    private async void PlayerSearchButton_Click(object sender, RoutedEventArgs e) =>
        await SearchPlayerAsync(PlayerInput.Text);

    private async void PlayerInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SearchPlayerAsync(PlayerInput.Text);
    }

    private async Task SearchPlayerAsync(string name)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var svc = (System.Windows.Application.Current as App)?.GameInfoService;
        if (svc == null) return;

        _playerCts?.Cancel();
        _playerCts = new CancellationTokenSource();

        PlayerCard.Visibility        = Visibility.Collapsed;
        PlayerResultsList.Visibility = Visibility.Collapsed;
        PlayerErrorText.Visibility   = Visibility.Collapsed;
        PlayerStatusText.Text        = "Buscando...";
        PlayerStatusText.Visibility  = Visibility.Visible;

        try
        {
            var result = await svc.SearchAsync(name, _playerCts.Token, attempt =>
                Dispatcher.Invoke(() => PlayerStatusText.Text = $"Reintentando... ({attempt}/2)"));
            PlayerStatusText.Visibility = Visibility.Collapsed;

            var players = result?.Players ?? [];
            if (players.Count == 0)
            {
                PlayerErrorText.Text       = $"No se encontró: {name}";
                PlayerErrorText.Visibility = Visibility.Visible;
                return;
            }

            var exact = players.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.Ordinal));

            if (exact == null)
            {
                var ci = players.Where(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                exact = ci.Count == 1 ? ci[0] : null;
            }

            if (exact?.Id != null)
            {
                await LoadPlayerDetailsAsync(exact.Id, exact.Name ?? name);
            }
            else
            {
                PlayerResultsList.ItemsSource  = players.Take(8).ToList();
                PlayerResultsList.Visibility   = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            PlayerStatusText.Visibility = Visibility.Collapsed;
            PlayerErrorText.Text        = $"Error: {ex.Message}";
            PlayerErrorText.Visibility  = Visibility.Visible;
        }
    }

    private async void PlayerResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlayerResultsList.SelectedItem is PlayerSearchEntry entry && entry.Id != null)
            await LoadPlayerDetailsAsync(entry.Id, entry.Name ?? "");
    }

    private async Task LoadPlayerDetailsAsync(string playerId, string playerName)
    {
        var svc = (System.Windows.Application.Current as App)?.GameInfoService;
        if (svc == null) return;

        PlayerResultsList.Visibility = Visibility.Collapsed;
        PlayerStatusText.Text        = "Cargando perfil...";
        PlayerStatusText.Visibility  = Visibility.Visible;

        try
        {
            var ct = _playerCts?.Token ?? default;

            // Phase 1 — basic profile (fast, hits cached server after first search)
            var player = await svc.GetPlayerAsync(playerId, ct);
            PlayerStatusText.Visibility = Visibility.Collapsed;

            if (player == null)
            {
                PlayerErrorText.Text       = "No se pudo cargar el perfil.";
                PlayerErrorText.Visibility = Visibility.Visible;
                return;
            }

            // Show the card immediately with what we have
            DisplayPlayerInfo(player, playerName, null, null);

            // Phase 2 — kills + deaths in parallel (uses cached server, much faster)
            using var killsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            killsCts.CancelAfter(TimeSpan.FromSeconds(8));

            var killsTask  = svc.GetPlayerKillsAsync(playerId, killsCts.Token);
            var deathsTask = svc.GetPlayerDeathsAsync(playerId, killsCts.Token);
            await Task.WhenAll(killsTask, deathsTask);

            var kills  = await killsTask;
            var deaths = await deathsTask;
            UpdateEquipmentAndDeaths(player, kills, deaths);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            PlayerStatusText.Visibility = Visibility.Collapsed;
            PlayerErrorText.Text        = $"Error: {ex.Message}";
            PlayerErrorText.Visibility  = Visibility.Visible;
        }
    }

    private void UpdateEquipmentAndDeaths(PlayerInfo player, List<KillEvent>? kills, List<KillEvent>? deaths)
    {
        var killCount = kills?.Count(k =>
            string.Equals(k.Killer?.Id, player.Id, StringComparison.OrdinalIgnoreCase));
        PlayerKillCountText.Text = killCount > 0 ? $"{killCount}+" : "—";

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
            var equipment = isKiller ? lastEvent.Killer?.Equipment : lastEvent.Victim?.Equipment;
            DisplayEquipment(equipment, lastEvent.TimeStamp);
        }
        else
        {
            LastEquipPanel.Visibility = Visibility.Collapsed;
        }

        DisplayDeaths(deaths);
    }

    private void DisplayPlayerInfo(PlayerInfo player, string fallbackName,
                                   List<KillEvent>? kills, List<KillEvent>? deaths)
    {
        PlayerCard.Visibility      = Visibility.Visible;
        PlayerErrorText.Visibility = Visibility.Collapsed;

        PlayerNameText.Text  = player.Name ?? fallbackName;
        PlayerGuildText.Text = string.IsNullOrEmpty(player.GuildName) ? "Sin guild" : player.GuildName;

        PlayerAllianceText.Text = string.IsNullOrEmpty(player.AllianceTag)
            ? "" : $"[{player.AllianceTag}] {player.AllianceName}";
        PlayerAllianceText.Visibility = string.IsNullOrEmpty(player.AllianceTag)
            ? Visibility.Collapsed : Visibility.Visible;

        PlayerIPText.Text        = player.AverageItemPower > 0 ? $"{player.AverageItemPower.Value:F0}" : "—";
        PlayerKillCountText.Text = "...";  // updated in phase 2

        PlayerKillFameText.Text  = FormatFame(player.KillFame);
        PlayerDeathFameText.Text = FormatFame(player.DeathFame);
        PlayerRatioText.Text     = player.FameRatio > 0 ? $"{player.FameRatio:F2}x" : "—";

        PlayerPvEText.Text    = FormatFame(player.LifetimeStatistics?.PvE?.Total ?? 0);
        PlayerGatherText.Text = FormatFame(player.LifetimeStatistics?.Gathering?.All?.Total ?? 0);
        PlayerCraftText.Text  = FormatFame(player.LifetimeStatistics?.Crafting?.Total ?? 0);

        PlayerAvatarImg.Source = null;
        _ = LoadPlayerAvatarAsync(player.Name ?? fallbackName);

        // Equipment and deaths are updated in phase 2 (UpdateEquipmentAndDeaths)
        _setValueCts?.Cancel();
        SetValueText.Visibility      = Visibility.Collapsed;
        LastEquipPanel.Visibility    = Visibility.Collapsed;
        RecentDeathsPanel.Visibility = Visibility.Collapsed;
    }

    // ── Equipment grid (matches in-game inventory screen) ────────────────────
    //
    //  row 0: [Bolso]   [Cabeza]   [Capa]
    //  row 1: [MH]      [Armadura] [OH]
    //  row 2: [Poción]  [Zapatos]  [Comida]
    //  row 3:           [Montura]

    private void DisplayEquipment(KillEquipment? equipment, DateTime? lastSeen)
    {
        if (equipment == null) { LastEquipPanel.Visibility = Visibility.Collapsed; return; }

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

        if (slots.All(s => s.item?.Type == null)) { LastEquipPanel.Visibility = Visibility.Collapsed; return; }

        LastEquipPanel.Visibility = Visibility.Visible;
        LastSeenAgoText.Text = lastSeen.HasValue ? $"• {FormatAgo(lastSeen.Value)}" : "";

        EquipmentSlotsGrid.Children.Clear();
        SetValueText.Text       = "Calculando valor...";
        SetValueText.Visibility = Visibility.Visible;

        foreach (var (item, row, col, label) in slots)
        {
            var itemName = item?.Type != null
                ? (_itemDatabase.GetNameById(item.Type) ?? item.Type)
                : null;
            var border = new Border
            {
                Width           = 38, Height = 38,
                Margin          = new Thickness(2),
                CornerRadius    = new CornerRadius(5),
                Background      = new SolidColorBrush(Color.FromArgb((byte)(item?.Type != null ? 50 : 20), 255, 255, 255)),
                BorderThickness = new Thickness(1),
                BorderBrush     = item?.Type != null
                    ? QualityBrush(item.Quality)
                    : new SolidColorBrush(Color.FromRgb(35, 35, 40)),
                ToolTip = itemName != null ? $"{label}: {itemName}" : label,
            };

            if (item?.Type != null)
            {
                try
                {
                    var img = new System.Windows.Controls.Image { Stretch = Stretch.Uniform, Margin = new Thickness(3) };
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource        = new Uri(GameInfoService.GetItemIconUrl(item.Type));
                    bmp.DecodePixelWidth = 64;
                    bmp.EndInit();
                    img.Source   = bmp;
                    border.Child = img;
                }
                catch { }
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
        var items = new (string? Type, int Quality)[]
        {
            (equipment.MainHand?.Type, equipment.MainHand?.Quality ?? 1),
            (equipment.OffHand?.Type,  equipment.OffHand?.Quality  ?? 1),
            (equipment.Head?.Type,     equipment.Head?.Quality     ?? 1),
            (equipment.Armor?.Type,    equipment.Armor?.Quality    ?? 1),
            (equipment.Shoes?.Type,    equipment.Shoes?.Quality    ?? 1),
            (equipment.Bag?.Type,      equipment.Bag?.Quality      ?? 1),
            (equipment.Cape?.Type,     equipment.Cape?.Quality     ?? 1),
            (equipment.Mount?.Type,    equipment.Mount?.Quality    ?? 1),
            (equipment.Potion?.Type,   equipment.Potion?.Quality   ?? 1),
            (equipment.Food?.Type,     equipment.Food?.Quality     ?? 1),
        }.Where(x => !string.IsNullOrEmpty(x.Type)).ToList();

        if (items.Count == 0) { Dispatcher.Invoke(() => SetValueText.Visibility = Visibility.Collapsed); return; }

        try
        {
            var tasks = items.Select(x => _apiService.GetItemPriceAsync(x.Type!, x.Quality, ct)).ToList();
            await Task.WhenAll(tasks);
            if (ct.IsCancellationRequested) return;

            double total = 0;
            int priced = 0;
            for (var i = 0; i < tasks.Count; i++)
            {
                var buyAt = (await tasks[i])?.BestBuyCity?.BuyAt ?? 0;
                if (buyAt > 0) { total += buyAt; priced++; }
            }

            Dispatcher.Invoke(() =>
            {
                if (total > 0)
                {
                    var suffix = priced < items.Count ? $" ({priced}/{items.Count} items)" : "";
                    SetValueText.Text       = $"⚔ Valor estimado: {total:N0} plata{suffix}";
                    SetValueText.Visibility = Visibility.Visible;
                }
                else
                {
                    SetValueText.Visibility = Visibility.Collapsed;
                }
            });
        }
        catch (OperationCanceledException) { }
        catch { Dispatcher.Invoke(() => SetValueText.Visibility = Visibility.Collapsed); }
    }

    private static readonly HttpClient _avatarClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders = { { "User-Agent", "AlbionPrices/1.0" } }
    };

    private async Task LoadPlayerAvatarAsync(string playerName)
    {
        try
        {
            var url   = GameInfoService.GetPlayerAvatarUrl(playerName);
            var bytes = await _avatarClient.GetByteArrayAsync(url);

            using var ms = new System.IO.MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.DecodePixelWidth = 120;
            bmp.EndInit();
            bmp.Freeze();

            Dispatcher.Invoke(() => PlayerAvatarImg.Source = bmp);
        }
        catch { }
    }

    private static SolidColorBrush QualityBrush(int quality) => quality switch
    {
        2 => new SolidColorBrush(Color.FromRgb(76,  175, 80)),
        3 => new SolidColorBrush(Color.FromRgb(33,  150, 243)),
        4 => new SolidColorBrush(Color.FromRgb(156, 39,  176)),
        5 => new SolidColorBrush(Color.FromRgb(255, 215,  0)),
        _ => new SolidColorBrush(Color.FromRgb(70,  70,   75)),
    };

    // ── Deaths ───────────────────────────────────────────────────────────────

    private void DisplayDeaths(List<KillEvent>? deaths)
    {
        if (deaths == null || deaths.Count == 0)
        {
            RecentDeathsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var items = deaths
            .Where(d => d.TimeStamp.HasValue)
            .OrderByDescending(d => d.TimeStamp)
            .Take(5)
            .Select(d => new
            {
                KillerName  = d.Killer?.Name  ?? "Desconocido",
                KillerGuild = string.IsNullOrEmpty(d.Killer?.GuildName) ? "" : $"[{d.Killer.GuildName}]",
                TimeAgo     = FormatAgo(d.TimeStamp!.Value.ToUniversalTime()),
            })
            .ToList();

        DeathsList.ItemsSource = items;
        RecentDeathsPanel.Visibility = Visibility.Visible;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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

    private void RegionNA_Click(object sender, RoutedEventArgs e) => ApplyRegion(AlbionPrices.Models.ServerRegion.Americas);
    private void RegionEU_Click(object sender, RoutedEventArgs e) => ApplyRegion(AlbionPrices.Models.ServerRegion.Europe);
    private void RegionAS_Click(object sender, RoutedEventArgs e) => ApplyRegion(AlbionPrices.Models.ServerRegion.Asia);

    private void ApplyRegion(AlbionPrices.Models.ServerRegion region)
    {
        (System.Windows.Application.Current as App)?.ChangeRegion(region);
        RefreshRegionButtons(region);
        _ = LoadGoldPriceAsync();
    }

    private void RefreshRegionButtons(AlbionPrices.Models.ServerRegion active)
    {
        if (_regionButtons == null) return;
        var regions = new[]
        {
            AlbionPrices.Models.ServerRegion.Americas,
            AlbionPrices.Models.ServerRegion.Europe,
            AlbionPrices.Models.ServerRegion.Asia,
        };
        for (int i = 0; i < _regionButtons.Length; i++)
        {
            var isActive = regions[i] == active;
            _regionButtons[i].Background  = isActive
                ? new SolidColorBrush(Color.FromRgb(255, 117, 80))
                : Brushes.Transparent;
            _regionButtons[i].Foreground  = isActive
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(102, 102, 102));
            _regionButtons[i].BorderBrush = isActive
                ? new SolidColorBrush(Color.FromRgb(255, 117, 80))
                : new SolidColorBrush(Color.FromRgb(51, 51, 51));
        }
    }

    // ── Item search & check ──────────────────────────────────────────────────

    private async void CheckButton_Click(object sender, RoutedEventArgs e) =>
        await CheckItem(ItemInput.Text);

    private async void ItemInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await CheckItem(ItemInput.Text);
    }

    private async Task CheckItemById(string itemId, string itemName)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _isLoading = true;

        try
        {
            ItemInput.Text = itemName;
            StatusText.Text       = "Buscando...";
            StatusText.Visibility = Visibility.Visible;
            ItemInfoPanel.Visibility = Visibility.Collapsed;
            ErrorText.Visibility     = Visibility.Collapsed;

            _currentQuality = 1;
            var ct      = _cts.Token;
            var summary = await _apiService.GetItemPriceAsync(itemId, 1, ct);
            StatusText.Visibility = Visibility.Collapsed;

            if (summary == null || summary.Prices.Count == 0)
            {
                ShowError($"Sin precios para: {itemId}");
                ShowCentered(); return;
            }

            summary.ItemName = _itemDatabase.GetNameById(itemId) ?? itemName;
            DisplayPriceInfo(summary);
            _ = LoadPriceHistoryAsync(itemId, 1, ct);
            ShowCentered();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowError($"Error: {ex.Message}");
            ShowCentered();
        }
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
            StatusText.Text       = "Buscando...";
            StatusText.Visibility = Visibility.Visible;
            ItemInfoPanel.Visibility = Visibility.Collapsed;
            ErrorText.Visibility     = Visibility.Collapsed;

            DebugText.Foreground = Brushes.Yellow;
            DebugText.Text       = $"DB: {_itemDatabase.ItemCount} items | Buscando: '{text}'";
            DebugText.Visibility = Visibility.Visible;

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

            var hist = (System.Windows.Application.Current as App)?.HistoryService;
            hist?.AddToHistory(itemId, summary.ItemName);
            RefreshHistoryUI();

            _ = LoadPriceHistoryAsync(itemId, 1, ct);

            ShowCentered();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowError($"Error: {ex.Message}");
            ShowCentered();
        }
        finally { _isLoading = false; }
    }

    // ── OCR / shared helpers ─────────────────────────────────────────────────

    private static string? ExtractItemName(string? ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText)) return null;
        foreach (var line in ocrText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = line.Trim();
            if (clean.Length >= 4 && clean.Any(char.IsLetter)) return clean;
        }
        return null;
    }

    private void ShowError(string message)
    {
        StatusText.Visibility    = Visibility.Collapsed;
        ItemInfoPanel.Visibility = Visibility.Collapsed;
        ErrorText.Text           = message;
        ErrorText.Visibility     = Visibility.Visible;
    }

    // ── Shared UI handlers ───────────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void UpdateBanner_Click(object sender, RoutedEventArgs e)
    {
        var svc = (System.Windows.Application.Current as App)?.UpdateService;
        if (svc?.IsUpdateAvailable != true) return;

        var url = svc.DownloadUrl ?? svc.ReleasePageUrl
            ?? $"https://github.com/EstebanLemes/AlbionPricesOverlay/releases/latest";

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
