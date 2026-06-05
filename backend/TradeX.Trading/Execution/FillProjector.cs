using Microsoft.Extensions.Logging;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Events;
using TradeX.Trading.EventBus;

namespace TradeX.Trading.Execution;

/// <summary>
/// 「成交 → 持仓」投影器实现。持仓模型为"每笔买入成交一条 Position"，与策略消费者
/// "逐笔平最旧 / 条件出场平全部"的语义一致。
///
/// 幂等设计：
/// <list type="bullet">
///   <item><b>买入</b>：以 <see cref="Position.OpeningOrderId"/> == 订单 Id 唯一约束判重，
///         无论实盘还是对账器重复调用都只开一条。</item>
///   <item><b>卖出</b>：定向（order.PositionId）或 FIFO 平仓，已 Closed 的持仓自然跳过。</item>
/// </list>
///
/// 与该投影器共享 DI scope（同一 DbContext）的调用方在调用前应已 SaveChanges 订单更新，
/// 投影器内部各仓储方法各自 SaveChanges，崩溃间隙由上述幂等键兜底。
/// </summary>
public sealed class FillProjector(
    IPositionRepository positionRepo,
    IOrderRepository orderRepo,
    IDomainEventBus eventBus,
    ILogger<FillProjector> logger) : IFillProjector
{
    public async Task ProjectFilledAsync(Order order, decimal avgFillPrice, CancellationToken ct = default)
    {
        if (order.Status != OrderStatus.Filled || order.FilledQuantity <= 0)
            return;

        if (order.Side == OrderSide.Buy)
            await ProjectBuyAsync(order, avgFillPrice, ct);
        else
            await ProjectSellAsync(order, avgFillPrice, ct);
    }

    private async Task ProjectBuyAsync(Order order, decimal avgFillPrice, CancellationToken ct)
    {
        // 幂等：该买单已开过仓 → 跳过
        if (await positionRepo.GetByOpeningOrderIdAsync(order.Id, ct) is not null)
        {
            logger.LogDebug("投影跳过：买单 {OrderId} 已存在对应持仓", order.Id);
            return;
        }

        var entryPrice = ResolveEntryPrice(order, avgFillPrice);
        if (entryPrice <= 0)
        {
            logger.LogWarning("投影失败：买单 {OrderId} 无法确定开仓价（avg={Avg}, quote={Quote}, filled={Filled}）",
                order.Id, avgFillPrice, order.QuoteQuantity, order.FilledQuantity);
            return;
        }

        var position = Position.Open(
            order.TraderId, order.ExchangeId, order.StrategyId ?? Guid.Empty,
            order.Pair, order.FilledQuantity, entryPrice);
        position.OpeningOrderId = order.Id;
        await positionRepo.AddAsync(position, ct);

        // 审计回链（非幂等依赖；幂等已由 OpeningOrderId 保证）
        order.PositionId = position.Id;
        await orderRepo.UpdateAsync(order, ct);

        logger.LogInformation("投影开仓：OrderId={OrderId} → PositionId={PositionId}, {Pair} {Qty} @ {Price}",
            order.Id, position.Id, position.Pair, position.Quantity, entryPrice);

        await PublishPositionAsync(position, ct);
    }

    private async Task ProjectSellAsync(Order order, decimal avgFillPrice, CancellationToken ct)
    {
        // 定向平仓：卖单显式携带 PositionId（网格逐笔平最旧 / 条件出场逐仓）
        if (order.PositionId is { } positionId)
        {
            var position = await positionRepo.GetByIdAsync(positionId, ct);
            if (position is null)
            {
                logger.LogWarning("投影平仓：卖单 {OrderId} 关联持仓 {PositionId} 不存在", order.Id, positionId);
                return;
            }
            await CloseOneAsync(position, ResolveExitPrice(order, avgFillPrice, position), ct);
            return;
        }

        // FIFO 平仓：手动/无定向卖单，按开仓时间顺序平至覆盖成交量
        if (order.StrategyId is not { } strategyId)
        {
            logger.LogWarning("投影平仓：卖单 {OrderId} 既无 PositionId 也无 StrategyId，无法定位持仓", order.Id);
            return;
        }

        var open = await positionRepo.GetOpenByStrategyAndPairAsync(strategyId, order.Pair, ct);
        var remaining = order.FilledQuantity;
        foreach (var position in open)
        {
            if (remaining <= 0) break;
            await CloseOneAsync(position, ResolveExitPrice(order, avgFillPrice, position), ct);
            remaining -= position.Quantity;
        }

        if (remaining > 0)
            logger.LogWarning("投影平仓：卖单 {OrderId} 成交量超过在手持仓，缺口 {Gap}（疑似持仓/余额漂移，待持仓级对账）",
                order.Id, remaining);
    }

    private async Task CloseOneAsync(Position position, decimal exitPrice, CancellationToken ct)
    {
        // 幂等：已 Closed → 跳过
        if (position.Status != PositionStatus.Open)
            return;

        position.Close(exitPrice);
        await positionRepo.UpdateAsync(position, ct);

        logger.LogInformation("投影平仓：PositionId={PositionId}, {Pair} {Qty} @ {Price}, PnL={Pnl}",
            position.Id, position.Pair, position.Quantity, exitPrice, position.RealizedPnl);

        await PublishPositionAsync(position, ct);
    }

    private Task PublishPositionAsync(Position p, CancellationToken ct)
        => eventBus.PublishAsync(new PositionUpdatedPayload(
            p.Id, p.TraderId, p.ExchangeId, p.StrategyId, p.Pair,
            p.Quantity, p.EntryPrice, p.UnrealizedPnl, p.RealizedPnl, p.Status.ToString(), p.UpdatedAt), ct);

    /// <summary>开仓价：成交均价优先，退化到 quote 金额/成交量，再退化到委托价。</summary>
    private static decimal ResolveEntryPrice(Order order, decimal avgFillPrice)
    {
        if (avgFillPrice > 0) return avgFillPrice;
        if (order.QuoteQuantity > 0 && order.FilledQuantity > 0) return order.QuoteQuantity / order.FilledQuantity;
        return order.Price ?? 0m;
    }

    /// <summary>平仓价：成交均价优先，退化到委托价，再退化到持仓最后已知市价/开仓价。</summary>
    private static decimal ResolveExitPrice(Order order, decimal avgFillPrice, Position position)
    {
        if (avgFillPrice > 0) return avgFillPrice;
        if (order.Price is { } p && p > 0) return p;
        if (position.CurrentPrice > 0) return position.CurrentPrice;
        return position.EntryPrice;
    }
}
