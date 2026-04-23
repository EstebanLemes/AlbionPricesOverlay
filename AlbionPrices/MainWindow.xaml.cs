using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AlbionPrices.Helpers;
using AlbionPrices.Models;
using AlbionPrices.Services;
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

    private string? _baseId;
    private int _currentTier;
    private int _currentEnchant;
    private Dictionary<int, List<int>> _variants = new();

    private static readonly Regex TieredItemRegex =
        new(@"^T(\d)_(.+?)(?:@(\d))?$", RegexOptions.Compiled);

    public MainWindow()
    {
        InitializeComponent();
        Icon = IconHelper.CreateWindowIcon();
        _apiService = new AlbionApiService();
        _itemDatabase = new ItemDatabase();
        
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        Deactivated += MainWindow_Deactivated;
        
        _hideTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _hideTimer.Tick += HideTimer_Tick;
        
        Loaded += async (s, e) =>
        {
            if (!_dbLoaded)
            {
                _dbLoaded = true;
                StatusText.Text = "Descargando base de datos...";
                await _itemDatabase.LoadAsync();
                if (_itemDatabase.ItemCount == 0)
                    StatusText.Text = $"ERROR DB: {_itemDatabase.LoadError}";
                else
                    StatusText.Text = $"{_itemDatabase.ItemCount:N0} items cargados. Escribí el nombre del item.";

                _ = CheckForUpdateAsync();
            }
        };
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _hotkey = new GlobalHotkey(this, 9000);
        if (_hotkey.Register())
        {
            _hotkey.HotkeyPressed += OnHotkeyPressed;
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _hotkey?.Dispose();
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (IsVisible && !_isLoading)
        {
            _hideTimer?.Start();
        }
    }

    private void HideTimer_Tick(object? sender, EventArgs e)
    {
        _hideTimer?.Stop();
        Hide();
    }

    public void SetNotifyIcon(NotifyIcon notifyIcon)
    {
        _notifyIcon = notifyIcon;
    }

    internal void ShowCentered()
    {
        if (IsVisible && IsLoaded)
        {
            Activate();
            Focus();
            return;
        }

        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        Left = (screenWidth - Width) / 2;
        Top = (screenHeight - Height) / 2;

        Show();
        Activate();
        Focus();
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        _hideTimer?.Stop();
        ShowCentered();
    }

    private void DisplayPriceInfo(ItemPriceSummary summary, bool setupTierEnchant = true)
    {
        StatusText.Visibility = Visibility.Collapsed;
        ItemInfoPanel.Visibility = Visibility.Visible;

        ItemNameText.Text = summary.ItemName;
        ItemIdText.Text = summary.ItemId;

        if (setupTierEnchant)
            SetupTierEnchant(summary.ItemId);

        var bestBuy = summary.BestBuyCity;
        if (bestBuy != null)
        {
            BestBuyCityText.Text = bestBuy.City;
            BestBuyPriceText.Text = $"{bestBuy.BuyAt:N0} ";
        }

        var bestSell = summary.BestSellCity;
        if (bestSell != null)
        {
            BestSellCityText.Text = bestSell.City;
            BestSellPriceText.Text = $"{bestSell.SellAt:N0} ";
        }

        CitiesList.ItemsSource = summary.Prices.Select(p => new CityPriceViewModel
        {
            City = p.City,
            BuyAt = p.BuyAt,
            SellAt = p.SellAt,
        }).ToList();
    }

    // ── Tier / Enchantment helpers ────────────────────────────────────────────

    private string BuildItemId() =>
        _currentEnchant > 0
            ? $"T{_currentTier}_{_baseId}@{_currentEnchant}"
            : $"T{_currentTier}_{_baseId}";

    private void SetupTierEnchant(string uniqueName)
    {
        var m = TieredItemRegex.Match(uniqueName);
        if (!m.Success) { TierEnchantPanel.Visibility = Visibility.Collapsed; return; }

        _currentTier = int.Parse(m.Groups[1].Value);
        _baseId      = m.Groups[2].Value;
        _currentEnchant = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;

        // Build variant map from items.json (only combos that actually exist)
        _variants.Clear();
        foreach (var v in _itemDatabase.GetVariants(_baseId))
        {
            var vm = TieredItemRegex.Match(v);
            if (!vm.Success) continue;
            var t = int.Parse(vm.Groups[1].Value);
            var e = vm.Groups[3].Success ? int.Parse(vm.Groups[3].Value) : 0;
            if (!_variants.ContainsKey(t)) _variants[t] = new List<int>();
            if (!_variants[t].Contains(e)) _variants[t].Add(e);
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
            // Clamp enchant to what's available for the new tier
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
            var btn = new System.Windows.Controls.Button
            {
                Content = label,
                Width = 30,
                Height = 22,
                Margin = new Thickness(2, 0, 2, 0),
                FontSize = 10,
                BorderThickness = new Thickness(1),
                Background = isSelected
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 117, 80))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 25, 30)),
                Foreground = isSelected
                    ? System.Windows.Media.Brushes.White
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 140, 140)),
                BorderBrush = isSelected
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 117, 80))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 65)),
            };
            var capturedLabel = label;
            btn.Click += async (_, _) => await onClick(capturedLabel);
            panel.Children.Add(btn);
        }
    }

    private static void RefreshButtonSelection(WrapPanel panel, string selected)
    {
        foreach (System.Windows.Controls.Button btn in panel.Children)
        {
            var isSelected = btn.Content?.ToString() == selected;
            btn.Background = isSelected
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 117, 80))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 25, 30));
            btn.Foreground = isSelected
                ? System.Windows.Media.Brushes.White
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 140, 140));
            btn.BorderBrush = isSelected
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 117, 80))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 65));
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
            ErrorText.Visibility = Visibility.Collapsed;
            BestBuyCityText.Text = "…";
            BestBuyPriceText.Text = "…";
            BestSellCityText.Text = "…";
            BestSellPriceText.Text = "…";
            CitiesList.ItemsSource = null;

            var summary = await _apiService.GetItemPriceAsync(itemId, _cts.Token);

            StatusText.Visibility = Visibility.Collapsed;

            if (summary == null || summary.Prices.Count == 0)
            {
                ErrorText.Text = $"Sin datos para: {itemId}";
                ErrorText.Visibility = Visibility.Visible;
                BestBuyCityText.Text = "—";
                BestBuyPriceText.Text = "—";
                BestSellCityText.Text = "—";
                BestSellPriceText.Text = "—";
                return;
            }

            summary.ItemName = _itemDatabase.GetNameById(itemId) ?? ItemNameText.Text;
            DisplayPriceInfo(summary, setupTierEnchant: false);
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("RefetchCurrentItem cancelled");
        }
        catch (Exception ex)
        {
            StatusText.Visibility = Visibility.Collapsed;
            ErrorText.Text = $"Error: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
        }
        finally { _isLoading = false; }
    }

    // ── OCR helper ───────────────────────────────────────────────────────────

    // Takes the first non-empty line from OCR output — tooltip title is always the first line
    private static string? ExtractItemName(string? ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText)) return null;

        foreach (var line in ocrText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = line.Trim();
            // Skip lines that are too short or look like noise (only digits/symbols)
            if (clean.Length >= 4 && clean.Any(char.IsLetter))
                return clean;
        }

        return null;
    }

    private void ShowError(string message)
    {
        StatusText.Visibility = Visibility.Collapsed;
        ItemInfoPanel.Visibility = Visibility.Collapsed;
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private async Task CheckForUpdateAsync()
    {
        var app = System.Windows.Application.Current as App;
        var updateService = app?.UpdateService;
        if (updateService == null) return;

        await updateService.CheckForUpdateAsync();

        if (updateService.IsUpdateAvailable)
        {
            UpdateBanner.Text = $"Nueva version disponible: v{updateService.LatestVersion}";
            UpdateBanner.Visibility = Visibility.Visible;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void UpdateBanner_Click(object sender, RoutedEventArgs e)
    {
        var app = System.Windows.Application.Current as App;
        if (app?.UpdateService?.IsUpdateAvailable == true)
        {
            _ = app.UpdateService.DownloadAndInstallUpdateAsync();
        }
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckItem(ItemInput.Text);
    }

    private async void ItemInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await CheckItem(ItemInput.Text);
        }
    }

    private async Task CheckItem(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (_isLoading)
        {
            _cts?.Cancel();
            await Task.Delay(100);
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        _isLoading = true;

        try
        {
            StatusText.Text = "Fetching price...";
            StatusText.Visibility = Visibility.Visible;
            ItemInfoPanel.Visibility = Visibility.Collapsed;
            ErrorText.Visibility = Visibility.Collapsed;

            DebugText.Foreground = System.Windows.Media.Brushes.Yellow;
            DebugText.Text = $"DB: {_itemDatabase.ItemCount} items | Buscando: '{text}'";
            DebugText.Visibility = Visibility.Visible;

            if (_itemDatabase.ItemCount == 0)
            {
                ShowError($"Base de datos vacía. Error: {_itemDatabase.LoadError ?? "desconocido"}");
                ShowCentered();
                return;
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
                    ShowCentered();
                    return;
                }
            }

            DebugText.Text = $"Encontrado: {itemId}";

            var summary = await _apiService.GetItemPriceAsync(itemId, _cts.Token);

            if (summary == null || summary.Prices.Count == 0)
            {
                ShowError($"No price data for: {itemId}");
                ShowCentered();
                return;
            }

            summary.ItemName = _itemDatabase.GetNameById(itemId) ?? itemId;

            DisplayPriceInfo(summary);
            ShowCentered();
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("CheckItem cancelled");
        }
        catch (Exception ex)
        {
            ShowError($"Error: {ex.Message}");
            ShowCentered();
        }
        finally
        {
            _isLoading = false;
        }
    }
}

public class CityPriceViewModel
{
    public string City { get; set; } = "";
    public double BuyAt { get; set; }
    public double SellAt { get; set; }
    public string BuyAtLabel => BuyAt > 0 ? $"{BuyAt:N0}" : "—";
    public string SellAtLabel => SellAt > 0 ? $"{SellAt:N0}" : "—";
}