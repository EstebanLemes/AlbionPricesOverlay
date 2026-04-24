using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AlbionPrices.Helpers;
using AlbionPrices.Models;
using AlbionPrices.Services;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using NotifyIcon = System.Windows.Forms.NotifyIcon;

namespace AlbionPrices;

public partial class MainWindow : Window
{
    private readonly AlbionApiService _apiService;
    private readonly ItemDatabase _itemDatabase;
    private GlobalHotkey? _hotkey;
    private bool _isLoading;
    private bool _dbLoaded;
    private NotifyIcon? _notifyIcon;
    private System.Windows.Threading.DispatcherTimer? _hideTimer;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _playerCts;

    private string? _baseId;
    private int _currentTier;
    private int _currentEnchant;
    private Dictionary<int, List<int>> _variants = new();
    private string? _currentItemId;

    private readonly Dictionary<string, CityPriceViewModel> _cityViewModels = new();

    private static readonly Regex TieredItemRegex =
        new(@"^T(\d)_(.+?)(?:@(\d))?$", RegexOptions.Compiled);

    public MainWindow()
    {
        InitializeComponent();
        Icon = IconHelper.CreateWindowIcon();
        _apiService   = new AlbionApiService();
        _itemDatabase = new ItemDatabase();

        Loaded  += MainWindow_Loaded;
        Closed  += MainWindow_Closed;
        Deactivated += MainWindow_Deactivated;

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
            if (_dbLoaded) return;
            _dbLoaded = true;
            StatusText.Text = "Descargando base de datos...";
            await _itemDatabase.LoadAsync();
            StatusText.Text = _itemDatabase.ItemCount == 0
                ? $"ERROR DB: {_itemDatabase.LoadError}"
                : $"{_itemDatabase.ItemCount:N0} items cargados. Escribí el nombre del item.";

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

        // Item icon
        try
        {
            ItemIcon.Source = new BitmapImage(new Uri(GameInfoService.GetItemIconUrl(summary.ItemId)));
        }
        catch { ItemIcon.Source = null; }

        if (setupTierEnchant) SetupTierEnchant(summary.ItemId);

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

        (System.Windows.Application.Current as App)?.RealtimeService?.SetItem(summary.ItemId);
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
            var t = int.Parse(vm.Groups[1].Value);
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

            var summary = await _apiService.GetItemPriceAsync(itemId, _cts.Token);
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
            var result = await svc.SearchAsync(name, _playerCts.Token);
            PlayerStatusText.Visibility = Visibility.Collapsed;

            var players = result?.Players ?? [];
            if (players.Count == 0)
            {
                PlayerErrorText.Text       = $"No se encontró: {name}";
                PlayerErrorText.Visibility = Visibility.Visible;
                return;
            }

            // Auto-select exact match, otherwise show list
            var exact = players.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

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
            var player = await svc.GetPlayerAsync(playerId, _playerCts?.Token ?? default);
            PlayerStatusText.Visibility = Visibility.Collapsed;

            if (player == null)
            {
                PlayerErrorText.Text       = "No se pudo cargar el perfil.";
                PlayerErrorText.Visibility = Visibility.Visible;
                return;
            }

            DisplayPlayerInfo(player, playerName);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            PlayerStatusText.Visibility = Visibility.Collapsed;
            PlayerErrorText.Text        = $"Error: {ex.Message}";
            PlayerErrorText.Visibility  = Visibility.Visible;
        }
    }

    private void DisplayPlayerInfo(PlayerInfo player, string fallbackName)
    {
        PlayerCard.Visibility      = Visibility.Visible;
        PlayerErrorText.Visibility = Visibility.Collapsed;

        PlayerNameText.Text = player.Name ?? fallbackName;

        PlayerGuildText.Text = string.IsNullOrEmpty(player.GuildName)
            ? "Sin guild" : player.GuildName;

        PlayerAllianceText.Text = string.IsNullOrEmpty(player.AllianceTag)
            ? "" : $"[{player.AllianceTag}] {player.AllianceName}";
        PlayerAllianceText.Visibility = string.IsNullOrEmpty(player.AllianceTag)
            ? Visibility.Collapsed : Visibility.Visible;

        PlayerKillFameText.Text  = FormatFame(player.KillFame);
        PlayerDeathFameText.Text = FormatFame(player.DeathFame);
        PlayerRatioText.Text     = player.FameRatio > 0 ? $"{player.FameRatio:F2}x" : "—";

        PlayerPvEText.Text    = FormatFame(player.LifetimeStatistics?.PvE?.Total ?? 0);
        PlayerGatherText.Text = FormatFame(player.LifetimeStatistics?.Gathering?.Total ?? 0);
        PlayerCraftText.Text  = FormatFame(player.LifetimeStatistics?.Crafting?.Total ?? 0);

        // Avatar
        try
        {
            PlayerAvatarImg.Source = new BitmapImage(
                new Uri(GameInfoService.GetPlayerAvatarUrl(player.Name ?? fallbackName)));
        }
        catch { PlayerAvatarImg.Source = null; }
    }

    private static string FormatFame(long value)
    {
        if (value <= 0)       return "—";
        if (value >= 1_000_000_000) return $"{value / 1_000_000_000.0:F1}B";
        if (value >= 1_000_000)     return $"{value / 1_000_000.0:F1}M";
        if (value >= 1_000)         return $"{value / 1_000.0:F1}K";
        return value.ToString("N0");
    }

    // ── OCR helper ───────────────────────────────────────────────────────────

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
        var app = System.Windows.Application.Current as App;
        if (app?.UpdateService?.IsUpdateAvailable == true)
            _ = app.UpdateService.DownloadAndInstallUpdateAsync();
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e) =>
        await CheckItem(ItemInput.Text);

    private async void ItemInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await CheckItem(ItemInput.Text);
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

            var summary = await _apiService.GetItemPriceAsync(itemId, _cts.Token);
            if (summary == null || summary.Prices.Count == 0)
            {
                ShowError($"Sin precios para: {itemId}");
                ShowCentered(); return;
            }

            summary.ItemName = _itemDatabase.GetNameById(itemId) ?? itemId;
            DisplayPriceInfo(summary);
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
}
