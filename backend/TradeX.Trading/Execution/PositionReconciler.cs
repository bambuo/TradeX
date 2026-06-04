using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Events;
using TradeX.Trading.Observability;
using TradeX.Trading.Risk;

namespace TradeX.Trading.Execution;

/// <summary>
/// 持仓级对账器实现。对每个已启用交易所：
/// <list type="number">
///   <item>把本地 Open 持仓按 base 资产聚合出"应持有量"；</item>
///   <item>拉取交易所实际余额；</item>
///   <item>对每个 base 资产比较，漂移超阈值则发 <see cref="TradingEventTypes.PositionDriftDetected"/> 告警。</item>
/// </list>
///
/// <para>漂移方向语义：<c>Drift = 本地量 - 实际量</c>。</para>
/// <list type="bullet">
///   <item><b>正值（本地 &gt; 实际）</b>：本地以为持有更多，策略可能尝试卖出不存在的头寸 → <b>Critical</b>。</item>
///   <item><b>负值（实际 &gt; 本地）</b>：交易所盈余，多为人工存入/未跟踪持仓 → 默认不上报（噪声大）。</item>
/// </list>
///
/// 仅检测 + 告警，不自动修改持仓——因账户余额包含非策略持仓，自动平改会造成错误平仓。
/// </summary>
public sealed class PositionReconciler(
    IExchangeRepository exchangeRepo,
    IPositionRepository positionRepo,
    IExchangeClientFactory clientFactory,
    IEncryptionService encryption,
    IOutboxRepository outbox,
    TradeXMetrics metrics,
    IOptions<RiskSettings> riskSettings,
    ILogger<PositionReconciler> logger) : IPositionReconciler
{
    public async Task<int> ReconcilePositionsAsync(CancellationToken ct = default)
    {
        var settings = riskSettings.Value;
        if (!settings.PositionReconcileEnabled) return 0;

        var exchanges = await exchangeRepo.GetAllEnabledAsync(ct);
        if (exchanges.Count == 0) return 0;

        // base 资产后缀按长度降序匹配，避免 "USD" 抢先于 "USDT"。
        var quotes = settings.QuoteAssets
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .OrderByDescending(q => q.Length)
            .ToList();

        var allOpen = await positionRepo.GetAllOpenAsync(ct);
        var tolerance = settings.PositionDriftTolerancePercent;
        var minAbs = settings.PositionDriftMinAbsolute;
        var totalDrift = 0;

        foreach (var exchange in exchanges)
        {
            if (ct.IsCancellationRequested) break;

            // 本地：按 base 资产聚合该交易所的开仓量
            var localByAsset = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in allOpen.Where(p => p.ExchangeId == exchange.Id))
            {
                var asset = ResolveBaseAsset(p.Pair, quotes);
                if (asset is null)
                {
                    logger.LogDebug("持仓对账：无法从交易对 {Pair} 解析 base 资产，跳过", p.Pair);
                    continue;
                }
                localByAsset[asset] = localByAsset.GetValueOrDefault(asset) + p.Quantity;
            }
            if (localByAsset.Count == 0) continue;

            var client = TryCreateClient(exchange);
            if (client is null) continue;

            Dictionary<string, decimal> balances;
            try
            {
                balances = await client.GetAssetBalancesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "持仓对账：拉取交易所余额失败, ExchangeId={ExchangeId}", exchange.Id);
                continue;
            }

            foreach (var (asset, localQty) in localByAsset)
            {
                if (ct.IsCancellationRequested) break;

                var actualQty = balances.GetValueOrDefault(asset);
                var drift = localQty - actualQty;
                var absDrift = Math.Abs(drift);
                if (absDrift <= minAbs) continue;

                var basis = Math.Max(localQty, actualQty);
                if (basis <= 0) continue;
                var driftPct = absDrift / basis * 100m;
                if (driftPct < tolerance) continue;

                // 盈余方向（实际 > 本地）默认不上报
                if (drift < 0 && !settings.PositionDriftReportSurplus) continue;

                var severity = drift > 0 ? "Critical" : "Warning";
                totalDrift++;

                var payload = new PositionDriftDetectedPayload(
                    exchange.Id, exchange.Type.ToString(), exchange.TraderId, asset,
                    localQty, actualQty, drift, Math.Round(driftPct, 4), severity, DateTime.UtcNow);

                // 包装为标准 TradingEventEnvelope，使 RedisToSignalRBridge 能解析并路由到管理员组。
                var envelope = new TradingEventEnvelope(
                    TradingEventTypes.PositionDriftDetected,
                    Guid.NewGuid(), exchange.TraderId ?? Guid.Empty,
                    JsonSerializer.Serialize(payload));
                await outbox.EnqueueAsync(new OutboxEvent
                {
                    Type = TradingEventTypes.PositionDriftDetected,
                    PayloadJson = JsonSerializer.Serialize(envelope),
                    TraderId = exchange.TraderId
                }, ct);
                await outbox.SaveChangesAsync(ct);

                metrics.PositionDriftDetected.Add(1,
                    new KeyValuePair<string, object?>("exchange", exchange.Type.ToString()),
                    new KeyValuePair<string, object?>("severity", severity));

                logger.Log(drift > 0 ? LogLevel.Error : LogLevel.Warning,
                    "持仓漂移告警: ExchangeId={ExchangeId}, Asset={Asset}, 本地={Local}, 实际={Actual}, 漂移={Drift} ({Pct:F2}%), 等级={Severity}",
                    exchange.Id, asset, localQty, actualQty, drift, driftPct, severity);
            }
        }

        if (totalDrift > 0)
            logger.LogWarning("持仓对账完成: 共发现 {Count} 项漂移超阈值", totalDrift);
        return totalDrift;
    }

    /// <summary>从交易对名切出 base 资产：归一化分隔符后按 quote 后缀（长度降序）剥离。无法识别返回 null。</summary>
    internal static string? ResolveBaseAsset(string pair, IReadOnlyList<string> quotesLongestFirst)
    {
        if (string.IsNullOrWhiteSpace(pair)) return null;
        var normalized = pair.Replace("_", "").Replace("-", "").Replace("/", "").Trim().ToUpperInvariant();
        foreach (var quote in quotesLongestFirst)
        {
            var q = quote.ToUpperInvariant();
            if (normalized.Length > q.Length && normalized.EndsWith(q, StringComparison.Ordinal))
                return normalized[..^q.Length];
        }
        return null;
    }

    private IExchangeClient? TryCreateClient(TradeX.Core.Models.Exchange exchange)
    {
        try
        {
            var pass = exchange.PassphraseEncrypted is not null ? encryption.Decrypt(exchange.PassphraseEncrypted) : null;
            return clientFactory.CreateClient(
                exchange.Type,
                encryption.Decrypt(exchange.ApiKeyEncrypted),
                encryption.Decrypt(exchange.SecretKeyEncrypted),
                pass);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "持仓对账：创建交易所客户端失败, ExchangeId={ExchangeId}", exchange.Id);
            return null;
        }
    }
}
