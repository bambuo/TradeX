using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;
using TradeX.Trading.Engine;
using TradeX.Trading.Execution;
using TradeX.Trading.Events;
using TradeX.Trading.EventBus;
using TradeX.Trading.Observability;
using TradeX.Trading.Risk;

namespace TradeX.Trading.Streaming;

/// <summary>
/// 逐笔成交（Trade）和 K 线收盘（Kline）双驱动的策略评估消费者。
/// 替代原 <see cref="TradingEngine"/> 的 15s 轮询循环。
///
/// <b>Trade 事件路径</b>：
/// 1. 维护价格序列（Trade 价格逐步填充）
/// 2. 查本地策略缓存找出此 pair 的活跃策略
/// 3. 按 trader 分组、同 trader 顺序执行评估
/// 4. 计算指标 → 条件评估 → 风控 → 下单 → 发布事件
///
/// <b>Kline 事件路径</b>：
/// 1. 使用完整 OHLC 数据构建 KlineWindow
/// 2. 按 (pair, exchangeId, interval) 匹配策略
/// 3. 计算技术指标 → 条件评估 → 风控 → 下单 → 发布事件
/// </summary>
public sealed class StrategyEvaluationConsumer(
    IServiceScopeFactory scopeFactory,
    Channel<TradeEvent> tradeChannel,
    Channel<KlineEvent> klineChannel,
    IIndicatorRegistry indicatorRegistry,
    IDomainEventBus eventBus,
    TradeXMetrics metrics,
    TradeStreamManager streamManager,
    KlineStreamManager klineStreamManager,
    IClock clock,
    ILogger<StrategyEvaluationConsumer> logger) : BackgroundService
{
    private const int PriceHistoryMaxLength = 2000;

    // 入场规则未指定下单金额（size）且未配置 positionSize 时的兜底下单金额（quote）。
    // 命中即记 Warning——通常意味着规则配置缺失，应在规则集中显式指定 size。
    private const decimal FallbackEntryQuoteSize = 100m;

    // pair → 价格序列（Trade 价格逐步填充）
    private readonly ConcurrentDictionary<string, List<decimal>> _priceHistory = new(StringComparer.OrdinalIgnoreCase);

    // (pair|exchange|interval) → 上一根已收盘 K 线，用于给 prevWindow 提供正确的“上一根”OHLC（穿越检测）
    private readonly ConcurrentDictionary<string, Kline> _lastClosedKline = new(StringComparer.OrdinalIgnoreCase);

    // 活跃策略缓存
    private volatile IReadOnlyList<StrategyBinding> _activeStrategies = [];
    private readonly object _refreshLock = new();

    // 入场幂等闸：进程内"在途买单"锁，键 "{bindingId}|{pair}"。
    // 挡住持仓落库前同一 (binding,pair) 被 Trade/Kline 多路并发或连续 tick 重复下单。
    private readonly ConcurrentDictionary<string, byte> _inFlightEntry = new();

    // 平仓幂等闸：进程内"在途卖单"锁，键为持仓 Id。
    // 挡住卖单成交、持仓状态翻转前同一持仓被高频 tick 重复选中下卖单（双重平仓）。
    private readonly ConcurrentDictionary<Guid, byte> _inFlightExit = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动 Trade 流管理器
        await streamManager.StartAsync(stoppingToken);

        // 启动 K 线流管理器
        await klineStreamManager.StartAsync(stoppingToken);

        // 首次加载策略缓存
        await RefreshStrategyCacheAsync(stoppingToken);

        logger.LogInformation("StrategyEvaluationConsumer 启动（Trade + Kline 双驱动），活跃策略 {Count} 个",
            _activeStrategies.Count);

        try
        {
            // 同时消费 Trade 和 Kline 两个通道
            var tradeTask = ConsumeTradeChannelAsync(stoppingToken);
            var klineTask = ConsumeKlineChannelAsync(stoppingToken);
            await Task.WhenAll(tradeTask, klineTask);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "StrategyEvaluationConsumer 异常退出");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await klineStreamManager.StopAsync();
        await streamManager.StopAsync();
        await base.StopAsync(cancellationToken);
        logger.LogInformation("StrategyEvaluationConsumer 已停止");
    }

    // ═══════════════════════════════════════════════════════════
    // Trade 事件处理
    // ═══════════════════════════════════════════════════════════

    private async Task ConsumeTradeChannelAsync(CancellationToken ct)
    {
        await foreach (var evt in tradeChannel.Reader.ReadAllAsync(ct))
            await ProcessTradeAsync(evt, ct);
    }

    private async Task ProcessTradeAsync(TradeEvent evt, CancellationToken ct)
    {
        var pair = evt.Pair;

        // 更新价格序列
        var prices = _priceHistory.GetOrAdd(pair, _ => []);
        lock (prices)
        {
            prices.Add(evt.Trade.Price);
            if (prices.Count > PriceHistoryMaxLength)
                prices.RemoveRange(0, prices.Count - PriceHistoryMaxLength);
        }

        // 查缓存的活跃策略
        var matchingBindings = _activeStrategies
            .Where(s => s.Pairs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(pair, StringComparer.OrdinalIgnoreCase)
                && s.ExchangeId == evt.ExchangeId)
            .ToList();

        if (matchingBindings.Count == 0)
            return;

        // 按 trader 分组
        var traderGroups = matchingBindings.GroupBy(s => s.TraderId).ToList();

        await Parallel.ForEachAsync(traderGroups, ct, async (traderGroup, token) =>
        {
            using var cycle = new TradingCycleScope(scopeFactory);
            foreach (var binding in traderGroup)
            {
                if (token.IsCancellationRequested) break;
                try
                {
                    await EvaluateTradeBindingAsync(binding, pair, evt, prices, cycle, token);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Trade 策略评估异常, BindingId={BindingId}, TraderId={TraderId}, Pair={Pair}",
                        binding.Id, traderGroup.Key, pair);
                }
            }
        });
    }

    private async Task EvaluateTradeBindingAsync(
        StrategyBinding binding,
        string pair,
        TradeEvent evt,
        List<decimal> prices,
        TradingCycleScope cycle,
        CancellationToken ct)
    {
        if (prices.Count < 2) return;

        var currentPrice = prices[^1];
        var trade = evt.Trade;

        // 以 Trade 价格构建 KlineWindow（无 OHLC 结构，当前价同时作为 Open/High/Low/Close）
        var currentWindow = new KlineWindow(prices, [], trade.Price, trade.Price, trade.Price, trade.Price);

        // 前一根 K 线窗口（用于穿越检测 CA/CB）
        List<decimal> prevPrices;
        lock (prices) { prevPrices = prices[..^1]; }
        var prevWindow = new KlineWindow(prevPrices, [], trade.Price, trade.Price, trade.Price, trade.Price);

        await EvaluateBindingCoreAsync(binding, pair, currentPrice, currentWindow, prevWindow, cycle, ct);
    }

    // ═══════════════════════════════════════════════════════════
    // Kline 事件处理
    // ═══════════════════════════════════════════════════════════

    private async Task ConsumeKlineChannelAsync(CancellationToken ct)
    {
        await foreach (var evt in klineChannel.Reader.ReadAllAsync(ct))
            await ProcessKlineAsync(evt, ct);
    }

    private async Task ProcessKlineAsync(KlineEvent evt, CancellationToken ct)
    {
        var pair = evt.Pair;
        var kline = evt.Kline;

        // 以实际 OHLC 数据构建 KlineWindow
        var currentPrice = kline.Close;
        var prices = _priceHistory.GetOrAdd(pair, _ => []);
        lock (prices)
        {
            prices.Add(kline.Close);
            if (prices.Count > PriceHistoryMaxLength)
                prices.RemoveRange(0, prices.Count - PriceHistoryMaxLength);
        }

        // 查缓存的活跃策略 — 额外匹配 Timeframe
        var matchingBindings = _activeStrategies
            .Where(s =>
            {
                var pairs = s.Pairs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return pairs.Contains(pair, StringComparer.OrdinalIgnoreCase)
                    && s.ExchangeId == evt.ExchangeId
                    && (string.IsNullOrWhiteSpace(s.Timeframe) || s.Timeframe == evt.Interval);
            })
            .ToList();

        if (matchingBindings.Count == 0)
            return;

        // 用实际 OHLC 构建前后窗口
        List<decimal> prevPrices;
        lock (prices) { prevPrices = prices.Count > 1 ? prices[..^1] : []; }

        // 使用 Candle 的完整 OHLC
        var currentWindow = new KlineWindow(prices, [],
            kline.Open, kline.High, kline.Low, kline.Close);

        // prevWindow 须用"上一根"的 OHLC，否则 OHLC 类指标（如 RANGE_PCT）prev==cur，穿越永不触发
        var klineKey = $"{pair}|{evt.ExchangeId}|{evt.Interval}";
        var prevWindow = prevPrices.Count > 0 && _lastClosedKline.TryGetValue(klineKey, out var prevKline)
            ? new KlineWindow(prevPrices, [], prevKline.Open, prevKline.High, prevKline.Low, prevKline.Close)
            : currentWindow;
        _lastClosedKline[klineKey] = kline;

        // 按 trader 分组
        var traderGroups = matchingBindings.GroupBy(s => s.TraderId).ToList();

        await Parallel.ForEachAsync(traderGroups, ct, async (traderGroup, token) =>
        {
            using var cycle = new TradingCycleScope(scopeFactory);
            foreach (var binding in traderGroup)
            {
                if (token.IsCancellationRequested) break;
                try
                {
                    await EvaluateBindingCoreAsync(binding, pair, currentPrice, currentWindow, prevWindow, cycle, token,
                        refreshPositionPrice: true);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Kline 策略评估异常, BindingId={BindingId}, TraderId={TraderId}, Pair={Pair}",
                        binding.Id, traderGroup.Key, pair);
                }
            }
        });
    }

    // ═══════════════════════════════════════════════════════════
    // 核心评估逻辑（Trade 和 Kline 共享）
    // ═══════════════════════════════════════════════════════════

    private async Task EvaluateBindingCoreAsync(
        StrategyBinding binding,
        string pair,
        decimal currentPrice,
        KlineWindow currentWindow,
        KlineWindow prevWindow,
        TradingCycleScope cycle,
        CancellationToken ct,
        bool refreshPositionPrice = false)
    {
        var indicatorValues = indicatorRegistry.ComputeAll(currentWindow);
        var previousValues = indicatorRegistry.ComputeAll(prevWindow);

        // 获取规则集 JSON
        var strategyTemplate = binding.StrategyId != Guid.Empty
            ? await cycle.StrategyRepo.GetByIdAsync(binding.StrategyId, ct)
            : null;
        var executionRuleJson = strategyTemplate?.ExecutionRule ?? "{}";

        // 检查持仓
        var openPositions = await cycle.PositionRepo.GetByStrategyIdAsync(binding.Id, ct);
        var openPairPositions = openPositions
            .Where(p => p.Status == PositionStatus.Open && p.Pair.Equals(pair, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.OpenedAt)
            .ToList();
        var hasOpenPosition = openPairPositions.Count > 0;

        // 仅 K 线收盘路径刷新持仓市价（限频到 K 线周期），使风控的未实现盈亏/敞口接近真实。
        // Trade 逐笔路径不落库，避免高频写放大。
        if (refreshPositionPrice && currentPrice > 0)
        {
            foreach (var position in openPairPositions)
            {
                position.UpdateMarketPrice(currentPrice);
                await cycle.PositionRepo.UpdateAsync(position, ct);
            }
        }

        // 聚合持仓状态
        var quantityHeld = openPairPositions.Sum(p => p.Quantity);
        var avgEntry = quantityHeld > 0
            ? openPairPositions.Sum(p => p.EntryPrice * p.Quantity) / quantityHeld
            : 0m;

        // 构建统一决策输入。ScopeKey 按 (binding, pair) 隔离 MinInterval 冷却，
        // 避免不同策略绑定/交易对的同名规则互相串扰；评估时间用 IClock（实盘=墙钟）。
        var input = new StrategyDecisionInput(
            ExecutionRule: executionRuleJson,
            IndicatorValues: indicatorValues,
            PreviousIndicatorValues: previousValues,
            CurrentPrice: currentPrice,
            AverageEntryPrice: avgEntry,
            QuantityHeld: quantityHeld,
            LotCount: openPairPositions.Count,
            ScopeKey: $"{binding.Id}:{pair}",
            EvaluationTime: clock.UtcNow);

        // 通过 StrategyDecisionEngine（内部用 RuleEvaluator）评估
        var decision = cycle.DecisionEngine.Decide(input);

        // 执行决策结果
        switch (decision.Action)
        {
            case StrategyAction.EnterMarket:
                var orderSize = decision.QuoteSize > 0 ? decision.QuoteSize : FallbackEntryQuoteSize;
                if (decision.QuoteSize <= 0)
                    logger.LogWarning("策略 {BindingId}: 入场规则未指定下单金额，回退默认 {Size}", binding.Id, FallbackEntryQuoteSize);
                if (await PassesRiskAsync(binding, pair, orderSize, cycle, ct))
                    await PlaceMarketBuyAsync(binding, pair, orderSize, cycle, ct);
                break;

            case StrategyAction.Reduce:
                // QuoteSize>0：自最早持仓起逐笔平仓，累计名义价值达到目标即止（整笔 lot 粒度）；
                // QuoteSize<=0：平掉最早一笔。
                foreach (var position in SelectPositionsToReduce(openPairPositions, decision.QuoteSize, currentPrice))
                    await CloseGridPositionAsync(binding, pair, position, currentPrice, cycle, ct);
                break;

            case StrategyAction.ExitAll:
                foreach (var position in openPairPositions)
                    await CloseGridPositionAsync(binding, pair, position, currentPrice, cycle, ct);
                break;

            default:
                logger.LogDebug("策略 {BindingId}: Hold, {Reason}", binding.Id, decision.Reason);
                break;
        }
    }

    /// <summary>
    /// 选择本次减仓要平掉的持仓（FIFO）。<paramref name="targetQuote"/> &gt; 0 时自最早持仓累计名义价值
    /// 直到达到目标金额（含跨过阈值的那一笔）；否则只平最早一笔。<paramref name="openPairPositions"/> 须按开仓时间升序。
    /// </summary>
    private static IEnumerable<Position> SelectPositionsToReduce(
        IReadOnlyList<Position> openPairPositions, decimal targetQuote, decimal currentPrice)
    {
        if (openPairPositions.Count == 0)
            yield break;

        // 目标金额无效或价格不可用（无法估算名义价值）时，保守地只平最早一笔，
        // 避免累计名义价值恒为 0 导致误平全部持仓。
        if (targetQuote <= 0m || currentPrice <= 0m)
        {
            yield return openPairPositions[0];
            yield break;
        }

        var accumulated = 0m;
        foreach (var position in openPairPositions)
        {
            yield return position;
            accumulated += position.Quantity * currentPrice;
            if (accumulated >= targetQuote)
                yield break;
        }
    }



    /// <summary>组合级 + 币种级风控检查，任一不通过即拒绝并发告警/指标。</summary>
    private async Task<bool> PassesRiskAsync(
        StrategyBinding binding, string pair, decimal plannedNotional,
        TradingCycleScope cycle, CancellationToken ct)
    {
        var riskCheck = await cycle.RiskManager.CheckAsync(binding.TraderId, binding.ExchangeId, ct);
        if (!riskCheck.IsAllowed)
        {
            var msg = string.Join("; ", riskCheck.DeniedReasons);
            logger.LogWarning("策略 {BindingId}: 风控拒绝入场, {Reasons}", binding.Id, msg);
            metrics.RiskDenials.Add(1, new KeyValuePair<string, object?>("scope", "portfolio"));
            await eventBus.PublishAsync(new RiskAlertPayload(
                Guid.NewGuid(), "Warning", "RiskCheck", binding.TraderId,
                binding.Id, msg, DateTime.UtcNow), ct);
            return false;
        }

        var pairRisk = await cycle.RiskManager.CheckPairRiskAsync(binding.TraderId, binding.ExchangeId, pair, plannedNotional, ct);
        if (!pairRisk.IsAllowed)
        {
            var msg = string.Join("; ", pairRisk.DeniedReasons);
            logger.LogWarning("策略 {BindingId}: 币种风控拒绝, {Reasons}", binding.Id, msg);
            metrics.RiskDenials.Add(1, new KeyValuePair<string, object?>("scope", "pair"));
            await eventBus.PublishAsync(new RiskAlertPayload(
                Guid.NewGuid(), "Warning", "PairRisk", binding.TraderId,
                binding.Id, msg, DateTime.UtcNow), ct);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 按 quote 金额下市价买单，并发布成交/拒单事件与指标。
    /// 入场幂等闸：进程内在途锁 + DB 在途买单检查，避免持仓落库前重复下单。
    /// </summary>
    private async Task PlaceMarketBuyAsync(
        StrategyBinding binding, string pair, decimal quoteSize,
        TradingCycleScope cycle, CancellationToken ct)
    {
        var gateKey = $"{binding.Id}|{pair}";
        if (!_inFlightEntry.TryAdd(gateKey, 0))
        {
            logger.LogDebug("策略 {BindingId}: {Pair} 已有在途买单，跳过本次入场", binding.Id, pair);
            return;
        }

        try
        {
            // 跨重启兜底：DB 中已有 Pending/PartiallyFilled 买单则跳过
            if (await cycle.OrderRepo.HasActiveBuyAsync(binding.Id, pair, ct))
            {
                logger.LogDebug("策略 {BindingId}: {Pair} DB 存在在途买单，跳过本次入场", binding.Id, pair);
                return;
            }

            await PlaceMarketBuyCoreAsync(binding, pair, quoteSize, cycle, ct);
        }
        finally
        {
            _inFlightEntry.TryRemove(gateKey, out _);
        }
    }

    private async Task PlaceMarketBuyCoreAsync(
        StrategyBinding binding, string pair, decimal quoteSize,
        TradingCycleScope cycle, CancellationToken ct)
    {
        var order = new Order
        {
            TraderId = binding.TraderId,
            ExchangeId = binding.ExchangeId,
            StrategyId = binding.Id,
            Pair = pair,
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 0,
            QuoteQuantity = quoteSize
        };

        var result = await cycle.TradeExecutor.ExecuteMarketOrderAsync(order, ct);
        if (result.Success)
        {
            logger.LogInformation("策略 {BindingId}: 买入成交 {Pair} {Quantity}",
                binding.Id, pair, result.FilledQuantity);
            metrics.OrdersPlaced.Add(1,
                new KeyValuePair<string, object?>("side", "buy"),
                new KeyValuePair<string, object?>("status", order.Status.ToString()));
            await eventBus.PublishAsync(new OrderPlacedPayload(
                order.Id, binding.TraderId, order.ExchangeId, order.StrategyId,
                order.Pair, order.Side.ToString(), order.Type.ToString(),
                order.Status.ToString(), order.Quantity, order.PlacedAt), ct);
        }
        else
        {
            logger.LogWarning("策略 {BindingId}: 买入失败 {Pair}, {Error}",
                binding.Id, pair, result.Error);
            metrics.OrdersRejected.Add(1,
                new KeyValuePair<string, object?>("side", "buy"),
                new KeyValuePair<string, object?>("reason", result.Error ?? "unknown"));
        }
    }

    /// <summary>
    /// 对单笔持仓下市价卖单平仓。卖单携带 PositionId，持仓的关闭与持仓事件由
    /// <see cref="FillProjector"/> 在成交时统一负责（覆盖实盘与对账恢复两条路径），
    /// 此处只负责下单与订单级事件/指标。
    /// <para>
    /// 平仓幂等闸：进程内在途卖单锁（按持仓 Id）+ DB 在途卖单检查，
    /// 避免卖单成交、持仓状态翻转前同一持仓被高频 tick 重复下卖单（双重平仓）。
    /// </para>
    /// </summary>
    private async Task CloseGridPositionAsync(
        StrategyBinding binding, string pair, Position position, decimal currentPrice,
        TradingCycleScope cycle, CancellationToken ct)
    {
        if (!_inFlightExit.TryAdd(position.Id, 0))
        {
            logger.LogDebug("策略 {BindingId}: 持仓 {PositionId} 已有在途卖单，跳过本次平仓", binding.Id, position.Id);
            return;
        }

        try
        {
            // 跨重启兜底：DB 中该持仓已有 Pending/PartiallyFilled 卖单则跳过
            if (await cycle.OrderRepo.HasActiveSellAsync(position.Id, ct))
            {
                logger.LogDebug("策略 {BindingId}: 持仓 {PositionId} DB 存在在途卖单，跳过本次平仓", binding.Id, position.Id);
                return;
            }

            await CloseGridPositionCoreAsync(binding, pair, position, cycle, ct);
        }
        finally
        {
            _inFlightExit.TryRemove(position.Id, out _);
        }
    }

    private async Task CloseGridPositionCoreAsync(
        StrategyBinding binding, string pair, Position position,
        TradingCycleScope cycle, CancellationToken ct)
    {
        var sellOrder = new Order
        {
            TraderId = binding.TraderId,
            ExchangeId = binding.ExchangeId,
            StrategyId = binding.Id,
            PositionId = position.Id,
            Pair = pair,
            Side = OrderSide.Sell,
            Type = OrderType.Market,
            Quantity = position.Quantity,
            QuoteQuantity = position.CurrentPrice * position.Quantity
        };

        var result = await cycle.TradeExecutor.ExecuteMarketOrderAsync(sellOrder, ct);
        if (result.Success)
        {
            logger.LogInformation("策略 {BindingId}: 卖出平仓下单成交 {Pair} {Quantity}（持仓由投影器关闭）",
                binding.Id, pair, position.Quantity);

            await eventBus.PublishAsync(new OrderPlacedPayload(
                sellOrder.Id, binding.TraderId, sellOrder.ExchangeId, sellOrder.StrategyId,
                sellOrder.Pair, sellOrder.Side.ToString(), sellOrder.Type.ToString(),
                sellOrder.Status.ToString(), sellOrder.Quantity, sellOrder.PlacedAt), ct);

            metrics.OrdersPlaced.Add(1,
                new KeyValuePair<string, object?>("side", "sell"),
                new KeyValuePair<string, object?>("status", sellOrder.Status.ToString()));
        }
        else
        {
            metrics.OrdersRejected.Add(1,
                new KeyValuePair<string, object?>("side", "sell"),
                new KeyValuePair<string, object?>("reason", result.Error ?? "unknown"));
        }
    }

    internal async Task RefreshStrategyCacheAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var bindingRepo = scope.ServiceProvider.GetRequiredService<IStrategyBindingRepository>();
        var bindings = await bindingRepo.GetAllActiveAsync(ct);

        lock (_refreshLock)
        {
            _activeStrategies = bindings;
        }

        logger.LogDebug("策略缓存已刷新: {Count} 个活跃策略", bindings.Count);
    }

    /// <summary>外部调用刷新（由 WorkerCommand 触发）。</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        await RefreshStrategyCacheAsync(ct);
        await streamManager.RefreshSubscriptionsAsync(ct);
        await klineStreamManager.RefreshSubscriptionsAsync(ct);
    }
}
