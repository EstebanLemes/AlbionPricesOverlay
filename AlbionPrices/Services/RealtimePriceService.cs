using System.Diagnostics;
using System.Text.Json;
using AlbionPrices.Models;
using NATS.Client.Core;

namespace AlbionPrices.Services;

public class RealtimePriceService : IAsyncDisposable
{
    private const string NatsUrl = "nats://public:thenewalbiondata@nats.albion-online-data.com:4222";
    private const string Subject  = "marketorders.deduped";

    private NatsConnection? _nats;
    private CancellationTokenSource? _cts;
    private string? _currentItemId;

    public bool IsConnected { get; private set; }

    public event EventHandler<PriceApiResponse>? PriceUpdated;
    public event EventHandler<bool>? ConnectionChanged;

    public void SetItem(string? itemId) => _currentItemId = itemId;

    public async Task ConnectAsync()
    {
        if (_nats != null) return;
        try
        {
            var opts = NatsOpts.Default with { Url = NatsUrl };
            _nats = new NatsConnection(opts);
            await _nats.ConnectAsync();

            IsConnected = true;
            ConnectionChanged?.Invoke(this, true);
            Debug.WriteLine("NATS: connected");

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => SubscribeLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NATS connect failed: {ex.Message}");
        }
    }

    private async Task SubscribeLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var msg in _nats!.SubscribeAsync<string>(Subject, cancellationToken: ct))
            {
                if (msg.Data is null || _currentItemId is null) continue;
                try
                {
                    var price = JsonSerializer.Deserialize<PriceApiResponse>(msg.Data);
                    if (price?.ItemId?.Equals(_currentItemId, StringComparison.OrdinalIgnoreCase) == true)
                        PriceUpdated?.Invoke(this, price);
                }
                catch { }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"NATS subscription error: {ex.Message}");
            IsConnected = false;
            ConnectionChanged?.Invoke(this, false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_nats != null)
        {
            await _nats.DisposeAsync();
            _nats = null;
        }
        IsConnected = false;
    }
}
