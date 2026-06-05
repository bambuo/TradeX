using System.Diagnostics;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

using ExchangeModel = TradeX.Core.Models.Exchange;

namespace TradeX.Worker;

public sealed class ExchangeOrderSyncService(
    IServiceScopeFactory scopeFactory,
    IEncryptionService encryption,
    IExchangeClientFactory clientFactory,
    ILogger<ExchangeOrderSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private const int OrderHistoryLimit = 200;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("ExchangeOrderSyncService 将在 {Delay}s 后首次执行", InitialDelay.TotalSeconds);
        await Task.Delay(InitialDelay, ct);

        while (!ct.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await SyncAllExchangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "同步历史订单时发生未预期异常");
            }

            sw.Stop();
            logger.LogInformation("ExchangeOrderSyncService 本轮同步完成，耗时 {Elapsed}s，下次将在 {Interval}m 后执行",
                sw.Elapsed.TotalSeconds.ToString("F1"), SyncInterval.TotalMinutes);
            await Task.Delay(SyncInterval, ct);
        }
    }

    private async Task SyncAllExchangesAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var exchangeRepo = scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
        var historyRepo = scope.ServiceProvider.GetRequiredService<IExchangeOrderHistoryRepository>();

        var exchanges = await exchangeRepo.GetAllEnabledAsync(ct);
        logger.LogInformation("开始同步 {Count} 个已启用交易所的历史订单", exchanges.Count);

        foreach (var exchange in exchanges)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await SyncSingleExchangeAsync(exchange, historyRepo, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "同步交易所 [{Name}]({Id}) 历史订单失败", exchange.Name, exchange.Id);
            }
        }
    }

    private async Task SyncSingleExchangeAsync(
        ExchangeModel exchange,
        IExchangeOrderHistoryRepository historyRepo,
        CancellationToken ct)
    {
        var apiKey = encryption.Decrypt(exchange.ApiKeyEncrypted);
        var secretKey = encryption.Decrypt(exchange.SecretKeyEncrypted);
        var passphrase = exchange.PassphraseEncrypted is not null
            ? encryption.Decrypt(exchange.PassphraseEncrypted)
            : null;

        var client = clientFactory.CreateClient(exchange.Type, apiKey, secretKey, passphrase);

        // 获取所有非 USDT 资产，推断交易对
        var balances = await client.GetAssetBalancesAsync(ct);
        var tradingCurrencies = balances.Keys
            .Where(c => !string.Equals(c, "USDT", StringComparison.OrdinalIgnoreCase))
            .ToList();

        logger.LogDebug("交易所 [{Name}] 发现 {Count} 个可交易资产", exchange.Name, tradingCurrencies.Count);

        var allOrders = new List<ExchangeOrderHistory>();

        foreach (var currency in tradingCurrencies)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var pair = FormatPair(currency, exchange.Type);
                var orders = await client.GetOrderHistoryByPairAsync(pair, OrderHistoryLimit, ct);

                var mapped = orders.Select(o => new ExchangeOrderHistory
                {
                    ExchangeId = exchange.Id,
                    Pair = o.Pair,
                    Side = o.Side,
                    Type = o.Type,
                    Status = o.Status,
                    Price = o.Price,
                    Quantity = o.Quantity,
                    FilledQuantity = o.FilledQuantity,
                    ExchangeOrderId = o.ExchangeOrderId,
                    PlacedAt = o.PlacedAt,
                    SyncedAt = DateTime.UtcNow
                });

                allOrders.AddRange(mapped);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "交易所 [{Name}] 同步交易对 {Currency}USDT 历史订单失败",
                    exchange.Name, currency);
            }
        }

        if (allOrders.Count > 0)
        {
            await historyRepo.UpsertManyAsync(allOrders, ct);
            logger.LogDebug("交易所 [{Name}] 同步完成，共 {Count} 条订单", exchange.Name, allOrders.Count);
        }
        else
        {
            logger.LogDebug("交易所 [{Name}] 无新历史订单", exchange.Name);
        }
    }

    /// <summary>
    /// 根据交易所类型格式化交易对符号。
    /// Binance/Bybit/HTX: "BTCUSDT"；OKX: "BTC-USDT"；Gate: "BTC_USDT"
    /// </summary>
    private static string FormatPair(string currency, ExchangeType type) => type switch
    {
        ExchangeType.OKX => $"{currency}-USDT",
        ExchangeType.Gate => $"{currency}_USDT",
        _ => $"{currency}USDT"
    };
}
