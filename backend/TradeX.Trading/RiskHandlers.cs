using Microsoft.Extensions.Logging;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading;

public class DailyLossHandler(ILogger<DailyLossHandler> logger) : RiskCheckHandler
{
    public override async Task<RiskContext> CheckAsync(RiskContext context, CancellationToken ct = default)
    {
        if (context.DailyLoss > context.MaxDailyLoss)
        {
            context.Deny($"当日亏损 {context.DailyLoss} 超过限制 {context.MaxDailyLoss}");
            logger.LogWarning("风控触发: 当日亏损超限, TraderId={TraderId}, Loss={Loss}, Limit={Limit}",
                context.TraderId, context.DailyLoss, context.MaxDailyLoss);
        }
        return await base.CheckAsync(context, ct);
    }
}

public class DrawdownHandler(ILogger<DrawdownHandler> logger) : RiskCheckHandler
{
    public override async Task<RiskContext> CheckAsync(RiskContext context, CancellationToken ct = default)
    {
        if (context.PortfolioValue > 0)
        {
            var drawdown = Math.Abs(context.DailyLoss) / context.PortfolioValue * 100;
            if (drawdown > context.MaxDrawdownPercent)
            {
                context.Deny($"回撤 {drawdown:F2}% 超过限制 {context.MaxDrawdownPercent}%");
                logger.LogWarning("风控触发: 回撤超限, TraderId={TraderId}, Drawdown={Drawdown:F2}%, Limit={Limit}%",
                    context.TraderId, drawdown, context.MaxDrawdownPercent);
            }
        }
        return await base.CheckAsync(context, ct);
    }
}

public class ConsecutiveLossHandler(ILogger<ConsecutiveLossHandler> logger) : RiskCheckHandler
{
    public override async Task<RiskContext> CheckAsync(RiskContext context, CancellationToken ct = default)
    {
        if (context.ConsecutiveLossCount >= context.MaxConsecutiveLosses)
        {
            context.Deny($"连续亏损 {context.ConsecutiveLossCount} 次超过限制 {context.MaxConsecutiveLosses}");
            logger.LogWarning("风控触发: 连续亏损超限, TraderId={TraderId}, Count={Count}, Limit={Limit}",
                context.TraderId, context.ConsecutiveLossCount, context.MaxConsecutiveLosses);
        }
        return await base.CheckAsync(context, ct);
    }
}

public class PositionLimitHandler(ILogger<PositionLimitHandler> logger) : RiskCheckHandler
{
    public override async Task<RiskContext> CheckAsync(RiskContext context, CancellationToken ct = default)
    {
        if (context.OpenPositionCount >= context.MaxOpenPositions)
        {
            context.Deny($"持仓数量 {context.OpenPositionCount} 超过限制 {context.MaxOpenPositions}");
            logger.LogWarning("风控触发: 持仓数量超限, TraderId={TraderId}, Count={Count}, Limit={Limit}",
                context.TraderId, context.OpenPositionCount, context.MaxOpenPositions);
        }
        return await base.CheckAsync(context, ct);
    }
}

public class CircuitBreakerHandler(ILogger<CircuitBreakerHandler> logger) : RiskCheckHandler
{
    public override async Task<RiskContext> CheckAsync(RiskContext context, CancellationToken ct = default)
    {
        if (context.CircuitBreakerActive)
        {
            context.Deny("熔断机制已激活，暂停所有交易");
            logger.LogWarning("风控触发: 熔断激活, TraderId={TraderId}", context.TraderId);
        }
        return await base.CheckAsync(context, ct);
    }
}

public class MaxOrderNotionalHandler(ILogger<MaxOrderNotionalHandler> logger) : RiskCheckHandler
{
    public override async Task<RiskContext> CheckAsync(RiskContext context, CancellationToken ct = default)
    {
        if (context.MaxOrderNotional > 0
            && context.OrderNotional is { } notional
            && notional > context.MaxOrderNotional)
        {
            context.Deny($"单笔名义价值 {notional:F2} 超过限制 {context.MaxOrderNotional:F2}");
            logger.LogWarning("风控触发: 单笔名义价值超限, TraderId={TraderId}, Notional={Notional}, Limit={Limit}",
                context.TraderId, notional, context.MaxOrderNotional);
        }
        return await base.CheckAsync(context, ct);
    }
}

public class SlippageHandler(ILogger<SlippageHandler> logger) : RiskCheckHandler
{
    public override async Task<RiskContext> CheckAsync(RiskContext context, CancellationToken ct = default)
    {
        if (context.OrderQuantity.HasValue && context.OrderPrice.HasValue)
        {
            var slippage = Math.Abs(context.OrderQuantity.Value * context.OrderPrice.Value * context.SlippageTolerance);
            if (slippage > context.MaxSlippageAmount)
            {
                context.Deny($"滑点预估 {slippage:F2} 超过限制 {context.MaxSlippageAmount}");
                logger.LogWarning("风控触发: 滑点超限, TraderId={TraderId}, Slippage={Slippage:F2}, Limit={Limit}",
                    context.TraderId, slippage, context.MaxSlippageAmount);
            }
        }
        return await base.CheckAsync(context, ct);
    }
}

public class CooldownCheck(ILogger<CooldownCheck> logger) : RiskCheckHandler
{
    public override async Task<RiskContext> CheckAsync(RiskContext context, CancellationToken ct = default)
    {
        if (context.LastTradeTimeUtc.HasValue && context.CooldownSeconds > 0)
        {
            var elapsed = DateTime.UtcNow - context.LastTradeTimeUtc.Value;
            if (elapsed.TotalSeconds < context.CooldownSeconds)
            {
                context.Deny($"冷却期未结束: 距上次交易 {elapsed.TotalSeconds:F0}s, 需要 {context.CooldownSeconds}s");
                logger.LogWarning("风控触发: 冷却期, TraderId={TraderId}, Elapsed={Elapsed:F0}s, Cooldown={Cooldown}s",
                    context.TraderId, elapsed.TotalSeconds, context.CooldownSeconds);
            }
        }
        return await base.CheckAsync(context, ct);
    }
}

public class ExchangeHealthHandler(
    IExchangeClientFactory clientFactory,
    IExchangeRepository exchangeRepo,
    IEncryptionService encryptionService,
    ILogger<ExchangeHealthHandler> logger) : RiskCheckHandler
{
    private static readonly Dictionary<Guid, (bool Healthy, DateTime CheckedAt)> _healthCache = [];
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public override async Task<RiskContext> CheckAsync(RiskContext context, CancellationToken ct = default)
    {
        if (_healthCache.TryGetValue(context.ExchangeId, out var cached) &&
            DateTime.UtcNow - cached.CheckedAt < CacheTtl)
        {
            if (!cached.Healthy)
            {
                context.Deny($"交易所健康检查失败 (缓存)");
                logger.LogWarning("风控触发: 交易所不可用(缓存), ExchangeId={ExchangeId}", context.ExchangeId);
            }
            return await base.CheckAsync(context, ct);
        }

        try
        {
            var exchange = await exchangeRepo.GetByIdAsync(context.ExchangeId, ct);
            if (exchange is null)
            {
                logger.LogWarning("交易所不存在, ExchangeId={ExchangeId}", context.ExchangeId);
                _healthCache[context.ExchangeId] = (false, DateTime.UtcNow);
                context.Deny("交易所不存在");
                return await base.CheckAsync(context, ct);
            }

            var apiKey = encryptionService.Decrypt(exchange.ApiKeyEncrypted);
            var secretKey = encryptionService.Decrypt(exchange.SecretKeyEncrypted);
            var client = clientFactory.CreateClient(exchange.Type, apiKey, secretKey);
            var result = await client.TestConnectionAsync(ct);

            _healthCache[context.ExchangeId] = (result.Success, DateTime.UtcNow);

            if (!result.Success)
            {
                context.Deny($"交易所连接失败: {result.Message}");
                logger.LogWarning("风控触发: 交易所连接失败, ExchangeId={ExchangeId}, Message={Message}",
                    context.ExchangeId, result.Message);
            }
        }
        catch (Exception ex)
        {
            _healthCache[context.ExchangeId] = (false, DateTime.UtcNow);
            logger.LogError(ex, "交易所健康检查异常, ExchangeId={ExchangeId}", context.ExchangeId);
        }

        return await base.CheckAsync(context, ct);
    }
}
