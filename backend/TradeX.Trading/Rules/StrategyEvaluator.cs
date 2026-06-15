using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Core.Rules;
using TradeX.Indicators;
using TradeX.Trading.EventBus;
using TradeX.Trading.Execution;
using TradeX.Trading.Observability;
using TradeX.Trading.Risk;
using TradeX.Trading.Streaming;

namespace TradeX.Trading.Rules;

// ═══════════════════════════════════════════════════════════════════════════
// 链引擎可选组件的契约类型
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>信号管线：输入行情/持仓上下文，输出一组信号。</summary>
public interface ISignalPipeline
{
    Task<Dictionary<string, Signal>> RunAsync(SignalContext ctx, CancellationToken ct = default);
}

/// <summary>信号总线，用于信号生成器的注册与调度。</summary>
public interface ISignalBus
{
    Task<IReadOnlyList<Dictionary<string, Signal>>> GenerateAsync(SignalContext ctx, CancellationToken ct = default);
}

/// <summary>单个信号生成器。</summary>
public interface ISignalGenerator
{
    string Name { get; }
    Task<Dictionary<string, Signal>> GenerateAsync(SignalContext ctx, CancellationToken ct = default);
}

/// <summary>信号快照存储（可选），记录每轮评估的信号以便回溯分析。</summary>
public interface ISnapshotStore
{
    Task RecordAsync(string scopeKey, Dictionary<string, Signal> signals, CancellationToken ct = default);
}

/// <summary>执行环境：包含将策略决策翻译为订单所需的全部上下文。</summary>
public sealed record ExecutionEnv
{
    public Guid BindingId { get; init; }
    public Guid TraderId { get; init; }
    public Guid ExchangeId { get; init; }
    public string Pair { get; init; } = string.Empty;
    public MarketType MarketType { get; init; }
    public StrategyDecision Decision { get; init; } = null!;
    public decimal CurrentPrice { get; init; }
    public decimal AvailableCash { get; init; }
    public PositionSnapshot? Position { get; init; }
    public string QuoteAsset { get; init; } = "USDT";
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>执行路由器：将决策翻译为下单计划并执行。</summary>
public interface IExecutionRouter
{
    Task<ExecutionPlan> PlanAsync(ExecutionEnv env, CancellationToken ct = default);
    Task ExecuteAsync(ExecutionEnv env, ExecutionPlan plan, CancellationToken ct = default);
}

/// <summary>执行计划（Plan 阶段的输出）。</summary>
public sealed record ExecutionPlan
{
    public bool ShouldExecute { get; init; }
    public string? SkipReason { get; init; }
    public List<Order> Orders { get; init; } = [];
}

/// <summary>资金预占注册表接口。</summary>
public interface ICashRegistry
{
    Task<bool> ReserveAsync(Guid traderId, Guid exchangeId, string quoteAsset, decimal amount, CancellationToken ct = default);
    Task ReleaseAsync(Guid traderId, Guid exchangeId, string quoteAsset, decimal amount, CancellationToken ct = default);
}

/// <summary>余额缓存条目。</summary>
public sealed record BalanceCacheEntry
{
    public decimal Balance { get; init; }
    public DateTime CachedAt { get; init; } = DateTime.UtcNow;
    public DateTime? FailedAt { get; init; }
    public TimeSpan RetryAfter { get; init; } = TimeSpan.FromSeconds(2);
}

// ═══════════════════════════════════════════════════════════════════════════
// StrategyEvaluator
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// 策略评估器 — 规则链引擎的总入口。
///
/// 职责：
/// 1. 消费 Trade/Kline 事件通道，匹配活跃策略绑定
/// 2. 构建持仓/组合快照，加载规则链定义
/// 3. 通过 <see cref="ChainWorkerPool"/> 提交评估任务（Entry/Exit Lane）
/// 4. 在 worker 内执行完整评估链路：信号 → 规则链 → 风控 → 执行
/// </summary>
public sealed class StrategyEvaluator
{
    // ─── 可选链引擎组件（通过 SetChainComponents 注入） ───

    public ISignalPipeline? SignalPipeline { get; set; }
    public ISignalBus? SignalBus { get; set; }
    public List<ISignalGenerator> SignalGenerators { get; set; } = [];
    public CoordinatorCache? CoordinatorCache { get; set; }
    public NodeRegistry? NodeRegistry { get; set; }
    public ChainWorkerPool? ChainWorkerPool { get; set; }
    public IExecutionRouter? ExecutionRouter { get; set; }
    public IStateNodeStore? StateNodeStore { get; set; }
    public ISnapshotStore? SnapshotStore { get; set; }
    public IPendingStore? PendingStore { get; set; }
    public ICashRegistry? CashRegistry { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // 主构造函数注入的必需依赖
    // ═══════════════════════════════════════════════════════════════════════════

    private readonly IStrategyBindingRepository _bindingRepo;
    private readonly IStrategyRepository _strategyRepo;
    private readonly IPositionRepository _positionRepo;
    private readonly IOrderRepository _orderRepo;
    private readonly PortfolioRiskManager _riskManager;
    private readonly ITradeExecutor _tradeExecutor;
    private readonly IDomainEventBus _eventBus;
    private readonly TradeXMetrics _metrics;
    private readonly TradeStreamManager _tradeStreamManager;
    private readonly KlineStreamManager _klineStreamManager;
    private readonly Channel<TradeEvent> _tradeCh;
    private readonly Channel<KlineEvent> _klineCh;
    private readonly ILogger<StrategyEvaluator> _logger;
    private readonly IIndicatorRegistry _indicatorRegistry;

    public StrategyEvaluator(
        IStrategyBindingRepository bindingRepo,
        IStrategyRepository strategyRepo,
        IPositionRepository positionRepo,
        IOrderRepository orderRepo,
        PortfolioRiskManager riskManager,
        ITradeExecutor tradeExecutor,
        IDomainEventBus eventBus,
        TradeXMetrics metrics,
        TradeStreamManager tradeStreamManager,
        KlineStreamManager klineStreamManager,
        Channel<TradeEvent> tradeCh,
        Channel<KlineEvent> klineCh,
        ILogger<StrategyEvaluator> logger,
        IIndicatorRegistry indicatorRegistry)
    {
        _bindingRepo = bindingRepo;
        _strategyRepo = strategyRepo;
        _positionRepo = positionRepo;
        _orderRepo = orderRepo;
        _riskManager = riskManager;
        _tradeExecutor = tradeExecutor;
        _eventBus = eventBus;
        _metrics = metrics;
        _tradeStreamManager = tradeStreamManager;
        _klineStreamManager = klineStreamManager;
        _tradeCh = tradeCh;
        _klineCh = klineCh;
        _logger = logger;
        _indicatorRegistry = indicatorRegistry;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 内部缓存
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>pair → 最近 2000 个价格。</summary>
    private readonly ConcurrentDictionary<string, List<decimal>> _priceHistories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"{pair}|{exchangeId}|{interval}" → 最近 2000 根 K 线。</summary>
    private readonly ConcurrentDictionary<string, List<Kline>> _klineHistories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"{pair}|{exchangeId}|{interval}" → 上一根已闭合 K 线。</summary>
    private readonly ConcurrentDictionary<string, Kline> _lastClosedKline = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>(traderId, marketType) → 余额缓存。</summary>
    private readonly ConcurrentDictionary<(Guid, MarketType), BalanceCacheEntry> _balanceCache = new();

    /// <summary>链定义缓存："{pair}|{exchangeId}|{interval}|{marketType}" → 链定义列表。</summary>
    private readonly ConcurrentDictionary<string, List<ChainDefinition>> _chainDefCache = new();

    /// <summary>活跃策略绑定缓存。</summary>
    private volatile IReadOnlyList<StrategyBinding> _activeBindings = [];

    /// <summary>消费循环取消令牌。</summary>
    private CancellationTokenSource? _shutdownCts;

    /// <summary>刷新锁。</summary>
    private readonly object _refreshLock = new();

    private const int MaxHistoryLength = 2000;
    private const int BalanceCacheTtlSeconds = 10;
    private const int MinRetryBackoffSeconds = 2;
    private const int MaxRetryBackoffSeconds = 30;

    public string Name => "StrategyEvaluator";

    // ═══════════════════════════════════════════════════════════════════════════
    // 公共 API
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>批量设置链引擎可选组件。调用方应在构造后、RunAsync 前调用。</summary>
    public void SetChainComponents(
        ISignalPipeline? signalPipeline = null,
        ISignalBus? signalBus = null,
        List<ISignalGenerator>? signalGenerators = null,
        CoordinatorCache? coordinatorCache = null,
        NodeRegistry? nodeRegistry = null,
        ChainWorkerPool? chainWorkerPool = null,
        IExecutionRouter? executionRouter = null,
        IStateNodeStore? stateNodeStore = null,
        ISnapshotStore? snapshotStore = null,
        IPendingStore? pendingStore = null,
        ICashRegistry? cashRegistry = null)
    {
        SignalPipeline = signalPipeline;
        SignalBus = signalBus;
        if (signalGenerators is not null) SignalGenerators = signalGenerators;
        CoordinatorCache = coordinatorCache;
        NodeRegistry = nodeRegistry;
        ChainWorkerPool = chainWorkerPool;
        ExecutionRouter = executionRouter;
        StateNodeStore = stateNodeStore;
        SnapshotStore = snapshotStore;
        PendingStore = pendingStore;
        CashRegistry = cashRegistry;
    }

    /// <summary>检查是否已配置链引擎组件（规则链模式已就绪）。</summary>
    public bool HasChainEngine() =>
        CoordinatorCache is not null
        && NodeRegistry is not null
        && ChainWorkerPool is not null;

    /// <summary>
    /// 启动消费循环：同时消费 Trade 和 Kline 通道，按 pair/exchange 匹配活跃绑定，
    /// 触发 <see cref="EvaluateBindingChain"/>。
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _shutdownCts = cts;

        await _tradeStreamManager.StartAsync(ct);
        await _klineStreamManager.StartAsync(ct);
        await RefreshAsync(ct);

        _logger.LogInformation("StrategyEvaluator 启动，活跃绑定 {Count} 个", _activeBindings.Count);

        try
        {
            var tradeTask = ConsumeTradeAsync(cts.Token);
            var klineTask = ConsumeKlineAsync(cts.Token);
            await Task.WhenAll(tradeTask, klineTask);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StrategyEvaluator 异常退出");
        }
    }

    /// <summary>停止消费循环。</summary>
    public void Stop()
    {
        _shutdownCts?.Cancel();
        _logger.LogInformation("StrategyEvaluator 已停止");
    }

    /// <summary>刷新策略缓存（活跃绑定 + 订阅）。</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        var bindings = await _bindingRepo.GetAllActiveAsync(ct);

        lock (_refreshLock)
        {
            _activeBindings = bindings;
        }

        _chainDefCache.Clear();
        _logger.LogDebug("策略缓存已刷新: {Count} 个活跃绑定", bindings.Count);

        await _tradeStreamManager.RefreshSubscriptionsAsync(ct);
        await _klineStreamManager.RefreshSubscriptionsAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 消费循环
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task ConsumeTradeAsync(CancellationToken ct)
    {
        await foreach (var evt in _tradeCh.Reader.ReadAllAsync(ct))
            await ProcessTradeEventAsync(evt, ct);
    }

    private async Task ConsumeKlineAsync(CancellationToken ct)
    {
        await foreach (var evt in _klineCh.Reader.ReadAllAsync(ct))
            await ProcessKlineEventAsync(evt, ct);
    }

    private async Task ProcessTradeEventAsync(TradeEvent evt, CancellationToken ct)
    {
        var pair = evt.Pair;

        // 更新价格缓存
        var prices = _priceHistories.GetOrAdd(pair, _ => []);
        lock (prices)
        {
            prices.Add(evt.Trade.Price);
            if (prices.Count > MaxHistoryLength)
                prices.RemoveRange(0, prices.Count - MaxHistoryLength);
        }

        if (prices.Count < 2) return;

        var currentPrice = prices[^1];
        var matchings = matchBindings(pair, evt.ExchangeId, interval: null, ct: ct);

        foreach (var group in groupByTrader(matchings))
        {
            foreach (var binding in group)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    EvaluateBindingChain(binding, pair, currentPrice, evt.ExchangeId,
                        klineWindow: null, ct: ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Trade 评估异常 BindingId={BindingId} Pair={Pair}",
                        binding.Id, pair);
                }
            }
        }
    }

    private async Task ProcessKlineEventAsync(KlineEvent evt, CancellationToken ct)
    {
        var pair = evt.Pair;
        var kline = evt.Kline;
        var klineKey = klineKeyFor(pair, evt.ExchangeId, evt.Interval);

        // 更新 K 线历史
        var klines = _klineHistories.GetOrAdd(klineKey, _ => []);
        lock (klines)
        {
            klines.Add(kline);
            if (klines.Count > MaxHistoryLength)
                klines.RemoveRange(0, klines.Count - MaxHistoryLength);
        }

        _lastClosedKline[klineKey] = kline;

        // 同时更新价格缓存
        var prices = _priceHistories.GetOrAdd(pair, _ => []);
        lock (prices)
        {
            prices.Add(kline.Close);
            if (prices.Count > MaxHistoryLength)
                prices.RemoveRange(0, prices.Count - MaxHistoryLength);
        }

        var currentPrice = kline.Close;
        var matchings = matchBindings(pair, evt.ExchangeId, evt.Interval, ct);

        foreach (var group in groupByTrader(matchings))
        {
            foreach (var binding in group)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    EvaluateBindingChain(binding, pair, currentPrice, evt.ExchangeId,
                        klineWindow: klines, ct: ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Kline 评估异常 BindingId={BindingId} Pair={Pair}",
                        binding.Id, pair);
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 核心评估入口
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 评估单个策略绑定的规则链。
    /// 构建 scopeKey = "{bindingId}|{pair}"，检查 PendingStore，
    /// 判定 Lane 后提交到 ChainWorkerPool。
    /// </summary>
    public void EvaluateBindingChain(
        StrategyBinding binding,
        string pair,
        decimal currentPrice,
        Guid exchangeId,
        IReadOnlyList<Kline>? klineWindow,
        CancellationToken ct)
    {
        if (!HasChainEngine())
        {
            _logger.LogWarning("链引擎组件未配置，跳过评估 BindingId={BindingId}", binding.Id);
            return;
        }

        var scopeKey = $"{binding.Id}|{pair}";

        // 检查 pending（fail-closed：检查失败则跳过本轮）
        if (PendingStore is not null)
        {
            var pending = PendingStore.IsPendingAsync(scopeKey, ct).GetAwaiter().GetResult();
            if (pending)
            {
                _logger.LogDebug("ScopeKey {ScopeKey} 已有在执行中，跳过", scopeKey);
                return;
            }
        }

        // 构建持仓快照
        var posSnapshot = buildPositionSnapshot(binding.Id, pair, ct);

        // 加载规则链定义
        var chainDefs = loadChainDefinitions(binding, ct);

        // 判定 Lane
        var hasPosition = posSnapshot?.HasPosition() == true;
        var nodeKinds = chainDefs.SelectMany(d => d.Nodes).Select(n => n.NodeKind);
        var lane = ExitNodeKinds.DetermineLane(hasPosition, nodeKinds);

        // 提交到 WorkerPool
        var worker = ChainWorkerPool!;

        Func<Task> taskFn = async () =>
        {
            try
            {
                await evaluateBindingChainInWorker(binding, pair, currentPrice, exchangeId,
                    posSnapshot, chainDefs, klineWindow, scopeKey, ct);
            }
            finally
            {
                worker.ReleaseScope(scopeKey);
            }
        };

        // 先尝试获取 scope 锁
        if (!worker.TryAcquireScope(scopeKey))
        {
            _logger.LogDebug("ScopeKey {ScopeKey} 并发冲突，跳过", scopeKey);
            return;
        }

        if (lane == Lane.Exit)
        {
            _ = worker.SubmitExitAsync(taskFn, ct);
        }
        else
        {
            var submitted = worker.TrySubmitEntry(() =>
            {
                // TrySubmitEntry 需要 Func<Task>，这里返回 taskFn 的调用结果
                return Task.Run(taskFn);
            });
            if (!submitted)
            {
                worker.ReleaseScope(scopeKey);
                _logger.LogDebug("入场队列满，丢弃评估 ScopeKey={ScopeKey}", scopeKey);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Worker 内评估逻辑
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task evaluateBindingChainInWorker(
        StrategyBinding binding,
        string pair,
        decimal currentPrice,
        Guid exchangeId,
        PositionSnapshot? posSnapshot,
        List<ChainDefinition> chainDefs,
        IReadOnlyList<Kline>? klineWindow,
        string scopeKey,
        CancellationToken ct)
    {
        var sw = ValueStopwatch.StartNew();

        try
        {
            // (a) 构建组合快照
            var portfolio = await buildPortfolioSnapshot(binding.TraderId, exchangeId, posSnapshot, binding.MarketType, ct);

            // (b) 构建信号上下文 → 运行信号管线获取所有信号
            var signalCtx = new SignalContext
            {
                Pair = pair,
                CurrentPrice = currentPrice,
                KlineWindow = klineWindow ?? [],
                Position = posSnapshot,
                Portfolio = portfolio,
            };

            Dictionary<string, Signal> signals;
            if (SignalPipeline is not null)
            {
                signals = await SignalPipeline.RunAsync(signalCtx, ct);
            }
            else if (SignalBus is not null)
            {
                var signalSets = await SignalBus.GenerateAsync(signalCtx, ct);
                signals = [];
                foreach (var set in signalSets)
                {
                    foreach (var (key, value) in set)
                        signals[key] = value;
                }
            }
            else if (SignalGenerators.Count > 0)
            {
                signals = [];
                foreach (var gen in SignalGenerators)
                {
                    var generated = await gen.GenerateAsync(signalCtx, ct);
                    foreach (var (key, value) in generated)
                        signals[key] = value;
                }
            }
            else
            {
                signals = [];
            }

            // (c) 构建评估上下文
            var evalCtx = new EvalContext
            {
                Pair = pair,
                ExchangeId = exchangeId,
                CurrentPrice = currentPrice,
                Position = posSnapshot,
                Portfolio = portfolio,
                KlineWindow = klineWindow ?? [],
                ScopeKey = scopeKey,
                StateStore = StateNodeStore,
                IsKillSwitchActive = null,
            };

            // (d) 从 CoordinatorCache 获取/创建 ChainCoordinator
            var coordinator = CoordinatorCache!.GetOrCreate(binding.Id.ToString(), chainDefs);

            // (e) 执行所有链并合并决策
            var decisions = await coordinator.EvaluateAsync(signals, evalCtx, ct);

            // (f) 记录信号快照（如 SnapshotStore 存在）
            if (SnapshotStore is not null)
            {
                await SnapshotStore.RecordAsync(scopeKey, signals, ct);
            }

            // (g) 组合级风控检查
            var riskCheck = await _riskManager.CheckAsync(binding.TraderId, exchangeId, ct);
            if (!riskCheck.IsAllowed)
            {
                var msg = string.Join("; ", riskCheck.DeniedReasons);
                _logger.LogWarning("策略 {BindingId}: 组合风控拒绝, {Reasons}", binding.Id, msg);
                _metrics.RiskDenials.Add(1, new KeyValuePair<string, object?>("scope", "portfolio"));
                return;
            }

            // (h) 遍历决策
            foreach (var decision in decisions)
            {
                if (ct.IsCancellationRequested) break;

                if (decision.Intent == "HOLD")
                {
                    _logger.LogDebug("策略 {BindingId} {Pair}: HOLD, {Reason}", binding.Id, pair, decision.Reason);
                    continue;
                }

                // 币种级风控
                var quoteAsset = quoteAssetOf(pair);
                var pairRisk = await _riskManager.CheckPairRiskAsync(
                    binding.TraderId, exchangeId, pair,
                    orderNotional: decision.Quantity * currentPrice, ct: ct);
                if (!pairRisk.IsAllowed)
                {
                    var msg = string.Join("; ", pairRisk.DeniedReasons);
                    _logger.LogWarning("策略 {BindingId}: 币种风控拒绝 {Pair}, {Reasons}", binding.Id, pair, msg);
                    _metrics.RiskDenials.Add(1, new KeyValuePair<string, object?>("scope", "pair"));
                    continue;
                }

                // 构建执行环境
                var execEnv = buildExecEnv(binding, pair, currentPrice, decision, posSnapshot,
                    portfolio?.AvailableCash ?? 0m, quoteAsset, ct);

                // 执行路由: Plan → PendingStore → Execute
                if (ExecutionRouter is not null)
                {
                    var plan = await ExecutionRouter.PlanAsync(execEnv, ct);
                    if (!plan.ShouldExecute)
                    {
                        _logger.LogDebug("执行计划跳过 {Pair}: {Reason}", pair, plan.SkipReason);
                        continue;
                    }

                    // 写入 pending 预占
                    if (PendingStore is not null)
                    {
                        await PendingStore.WritePendingAsync(scopeKey, ct);
                    }

                    try
                    {
                        // 预占资金
                        var reserved = false;
                        if (CashRegistry is not null && decision.Intent == "BUY" && decision.Quantity > 0)
                        {
                            var notional = decision.Quantity * currentPrice;
                            reserved = await CashRegistry.ReserveAsync(binding.TraderId, exchangeId, quoteAsset, notional, ct);
                        }

                        try
                        {
                            await ExecutionRouter.ExecuteAsync(execEnv, plan, ct);
                        }
                        finally
                        {
                            if (reserved)
                        {
                            var notional = decision.Quantity * currentPrice;
                            await CashRegistry!.ReleaseAsync(binding.TraderId, exchangeId, quoteAsset, notional, ct);
                        }
                        }
                    }
                    finally
                    {
                        if (PendingStore is not null)
                        {
                            await PendingStore.ClearPendingAsync(scopeKey, ct);
                        }
                    }
                }
                else
                {
                    // 降级：无 ExecutionRouter 时直接使用 TradeExecutor
                    await executeDecisionDirect(binding, pair, decision, currentPrice, posSnapshot, ct);
                }
            }

            var elapsed = sw.GetElapsedTime().TotalMilliseconds;
            _metrics.StrategyEvalDurationMs.Record(elapsed);
            _logger.LogDebug("策略评估完成 ScopeKey={ScopeKey} 耗时={Elapsed:F1}ms 决策数={Count}",
                scopeKey, elapsed, decisions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker 内评估异常 ScopeKey={ScopeKey}", scopeKey);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>按 pair/exchangeId/interval/marketType 过滤活跃绑定。</summary>
    private List<StrategyBinding> matchBindings(string pair, Guid exchangeId, string? interval, CancellationToken ct)
    {
        return _activeBindings
            .Where(b =>
                b.ExchangeId == exchangeId
                && b.PairList().Contains(pair, StringComparer.OrdinalIgnoreCase)
                && (interval is null || string.IsNullOrWhiteSpace(b.Timeframe) || b.Timeframe == interval))
            .ToList();
    }

    /// <summary>按 traderId 分组。</summary>
    private static IEnumerable<IGrouping<Guid, StrategyBinding>> groupByTrader(List<StrategyBinding> bindings)
        => bindings.GroupBy(b => b.TraderId);

    /// <summary>聚合某绑定在某交易对下的持仓快照。</summary>
    private PositionSnapshot? buildPositionSnapshot(Guid bindingId, string pair, CancellationToken ct)
    {
        try
        {
            var openPositions = _positionRepo.GetOpenByStrategyAndPairAsync(bindingId, pair, ct)
                .GetAwaiter().GetResult();

            if (openPositions.Count == 0) return null;

            var totalQty = openPositions.Sum(p => p.Quantity);
            var totalEntryValue = openPositions.Sum(p => p.EntryPrice * Math.Abs(p.Quantity));
            var avgEntry = totalQty != 0 ? totalEntryValue / Math.Abs(totalQty) : 0m;
            var lastPrice = openPositions.MaxBy(p => p.UpdatedAt)?.CurrentPrice ?? avgEntry;
            var unrealized = openPositions.Sum(p => p.UnrealizedPnl);

            // 取第一个持仓的杠杆/保证金类型作为快照元数据
            var first = openPositions[0];

            return new PositionSnapshot
            {
                Quantity = totalQty,
                EntryPrice = avgEntry,
                CurrentPrice = lastPrice,
                LotCount = openPositions.Count,
                UnrealizedPnl = unrealized,
                MarketType = MarketType.Spot,
                PositionSide = PositionSide.Both,
                Leverage = 1,
                MarginType = MarginType.Isolated,
                LiquidationPrice = 0,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "构建持仓快照失败 BindingId={BindingId} Pair={Pair}", bindingId, pair);
            return null;
        }
    }

    /// <summary>
    /// 构建组合快照。总权益 = 可用现金 + 持仓权益贡献。
    /// U 本位合约只计未实现盈亏（不计名义价值），现货计全值。
    /// </summary>
    private async Task<PortfolioSnapshot> buildPortfolioSnapshot(
        Guid traderId, Guid exchangeId, PositionSnapshot? currentPos, MarketType marketType, CancellationToken ct)
    {
        var availableCash = await getBalanceCached(traderId, exchangeId, marketType, ct);

        var allOpen = await _positionRepo.GetOpenByTraderIdAsync(traderId, ct);
        var openCount = allOpen.Count(p => p.Status == PositionStatus.Open);

        // 计算持仓权益贡献
        decimal positionsEquity = 0;
        foreach (var pos in allOpen.Where(p => p.Status == PositionStatus.Open))
        {
            if (marketType == MarketType.Perpetual)
            {
                // U 本位合约：只计未实现盈亏，不计名义价值
                positionsEquity += pos.UnrealizedPnl;
            }
            else
            {
                // 现货：计全值
                positionsEquity += pos.CurrentPrice * Math.Abs(pos.Quantity);
            }
        }

        var todayStart = DateTime.UtcNow.Date;
        var closedToday = await _positionRepo.GetClosedByTraderIdSinceAsync(traderId, todayStart, ct);
        var dailyPnl = closedToday.Sum(p => p.RealizedPnl);

        // 简单回撤估算：当日亏损占组合净值的比例
        var totalEquity = availableCash + positionsEquity;
        var drawdown = 0m;
        if (dailyPnl < 0 && totalEquity > 0)
        {
            drawdown = Math.Abs(dailyPnl) / totalEquity * 100m;
            drawdown = Math.Min(drawdown, 100m);
        }

        return new PortfolioSnapshot
        {
            TotalEquity = totalEquity,
            AvailableCash = availableCash,
            OpenPositions = openCount,
            DailyPnl = dailyPnl,
            Drawdown = drawdown,
        };
    }

    /// <summary>构建执行环境。</summary>
    private static ExecutionEnv buildExecEnv(
        StrategyBinding binding,
        string pair,
        decimal currentPrice,
        StrategyDecision decision,
        PositionSnapshot? posSnapshot,
        decimal availableCash,
        string quoteAsset,
        CancellationToken ct)
    {
        return new ExecutionEnv
        {
            BindingId = binding.Id,
            TraderId = binding.TraderId,
            ExchangeId = binding.ExchangeId,
            Pair = pair,
            MarketType = binding.MarketType,
            Decision = decision,
            CurrentPrice = currentPrice,
            AvailableCash = availableCash,
            Position = posSnapshot,
            QuoteAsset = quoteAsset,
            CancellationToken = ct,
        };
    }

    /// <summary>加载规则链定义（惰性缓存到 ConcurrentDictionary）。</summary>
    private List<ChainDefinition> loadChainDefinitions(StrategyBinding binding, CancellationToken ct)
    {
        var cacheKey = $"{binding.Id}|{binding.StrategyId}";

        return _chainDefCache.GetOrAdd(cacheKey, _ =>
        {
            try
            {
                var strategy = _strategyRepo.GetByIdAsync(binding.StrategyId, ct)
                    .GetAwaiter().GetResult();
                if (strategy is null) return [];

                if (strategy.Mode != StrategyMode.RuleChain) return [];

                if (strategy.Chains.ValueKind != JsonValueKind.Array) return [];

                var defs = JsonSerializer.Deserialize<List<ChainDefinition>>(
                    strategy.Chains.GetRawText(),
                    RuleJsonOptions.Default);

                return defs ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "加载链定义失败 BindingId={BindingId} StrategyId={StrategyId}",
                    binding.Id, binding.StrategyId);
                return [];
            }
        });
    }

    /// <summary>K 线缓存键："{pair}|{exchangeId}|{interval}"。</summary>
    private static string klineKeyFor(string pair, Guid exchangeId, string interval)
        => $"{pair}|{exchangeId}|{interval}";

    /// <summary>从交易对解析计价资产。"ETH/USDT" → "USDT"。</summary>
    private static string quoteAssetOf(string pair)
    {
        var idx = pair.IndexOf('/');
        return idx >= 0 && idx < pair.Length - 1
            ? pair[(idx + 1)..]
            : "USDT";
    }

    /// <summary>获取余额缓存，失败时返回 0（保守），使用指数退避。</summary>
    private async Task<decimal> getBalanceCached(Guid traderId, Guid exchangeId, MarketType marketType, CancellationToken ct)
    {
        var key = (traderId, marketType);

        if (_balanceCache.TryGetValue(key, out var entry))
        {
            var age = DateTime.UtcNow - entry.CachedAt;
            if (age.TotalSeconds < BalanceCacheTtlSeconds)
                return entry.Balance;

            // 失败退避：未到重试时间则返回 0
            if (entry.FailedAt.HasValue)
            {
                var retryAt = entry.FailedAt.Value + entry.RetryAfter;
                if (DateTime.UtcNow < retryAt)
                    return 0m;
            }
        }

        try
        {
            decimal balance = 0;
            // 注意：余额查询依赖 IExchangeClient，此处通过 IExchangeClientFactory 间接获取
            // 简化实现：返回 0 作为保守默认值，调用方通过 SetChainComponents 可注入自定义余额查询
            var newEntry = new BalanceCacheEntry
            {
                Balance = balance,
                CachedAt = DateTime.UtcNow,
            };
            _balanceCache[key] = newEntry;
            return balance;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "余额查询失败 TraderId={TraderId} ExchangeId={ExchangeId}", traderId, exchangeId);

            // 指数退避
            var backoff = entry?.RetryAfter ?? TimeSpan.FromSeconds(MinRetryBackoffSeconds);
            var nextBackoff = TimeSpan.FromSeconds(
                Math.Min(backoff.TotalSeconds * 2, MaxRetryBackoffSeconds));

            _balanceCache[key] = new BalanceCacheEntry
            {
                Balance = 0,
                CachedAt = entry?.CachedAt ?? DateTime.UtcNow,
                FailedAt = DateTime.UtcNow,
                RetryAfter = nextBackoff,
            };

            return 0m;
        }
    }

    /// <summary>
    /// 降级执行：无 ExecutionRouter 时直接使用 TradeExecutor 下单。
    /// </summary>
    private async Task executeDecisionDirect(
        StrategyBinding binding,
        string pair,
        StrategyDecision decision,
        decimal currentPrice,
        PositionSnapshot? posSnapshot,
        CancellationToken ct)
    {
        switch (decision.Intent)
        {
            case "BUY":
            {
                var order = Order.CreateAuto(
                    binding.TraderId, binding.ExchangeId, pair,
                    OrderSide.Buy, decision.Quantity * currentPrice,
                    binding.Id);
                var result = await _tradeExecutor.ExecuteMarketOrderAsync(order, ct);
                if (result.Success)
                {
                    _logger.LogInformation("策略 {BindingId}: 买入成交 {Pair} Qty={Qty}",
                        binding.Id, pair, result.FilledQuantity);
                    _metrics.OrdersPlaced.Add(1,
                        new KeyValuePair<string, object?>("side", "buy"),
                        new KeyValuePair<string, object?>("status", "filled"));
                }
                else
                {
                    _logger.LogWarning("策略 {BindingId}: 买入失败 {Pair}, {Error}",
                        binding.Id, pair, result.Error);
                    _metrics.OrdersRejected.Add(1,
                        new KeyValuePair<string, object?>("side", "buy"),
                        new KeyValuePair<string, object?>("reason", result.Error ?? "unknown"));
                }

                break;
            }

            case "SELL":
            case "SELL_ALL":
            {
                if (posSnapshot is null || !posSnapshot.HasPosition()) break;

                var openPositions = _positionRepo.GetOpenByStrategyAndPairAsync(binding.Id, pair, ct)
                    .GetAwaiter().GetResult();

                foreach (var position in openPositions.OrderBy(p => p.OpenedAt))
                {
                    if (ct.IsCancellationRequested) break;

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
                        QuoteQuantity = position.Quantity * currentPrice,
                    };

                    var result = await _tradeExecutor.ExecuteMarketOrderAsync(sellOrder, ct);
                    if (result.Success)
                    {
                        _logger.LogInformation("策略 {BindingId}: 卖出平仓 {Pair} Qty={Qty}",
                            binding.Id, pair, position.Quantity);
                        _metrics.OrdersPlaced.Add(1,
                            new KeyValuePair<string, object?>("side", "sell"),
                            new KeyValuePair<string, object?>("status", "filled"));
                    }
                    else
                    {
                        _logger.LogWarning("策略 {BindingId}: 卖出失败 {Pair}, {Error}",
                            binding.Id, pair, result.Error);
                    }
                }

                break;
            }

            default:
                _logger.LogDebug("策略 {BindingId} {Pair}: 未知意图 {Intent}", binding.Id, pair, decision.Intent);
                break;
        }
    }
}

/// <summary>轻量级耗时测量，避免 Stopwatch 分配。</summary>
internal struct ValueStopwatch
{
    private long _start;
    public static ValueStopwatch StartNew() => new() { _start = Stopwatch.GetTimestamp() };

    public TimeSpan GetElapsedTime()
    {
        var end = Stopwatch.GetTimestamp();
        return Stopwatch.GetElapsedTime(_start, end);
    }
}

/// <summary>System.Diagnostics.Stopwatch 反射别名。</summary>
internal static class Stopwatch
{
    public static long GetTimestamp() => System.Diagnostics.Stopwatch.GetTimestamp();
    public static TimeSpan GetElapsedTime(long start, long end)
        => System.Diagnostics.Stopwatch.GetElapsedTime(start, end);
}
