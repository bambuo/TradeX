using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading.Streaming;

/// <summary>
/// K 线流订阅管理器。不作为 BackgroundService 注册，生命周期由
/// <see cref="StrategyEvaluationConsumer"/> 通过 <c>StartAsync</c> / <c>StopAsync</c> 驱动。
///
/// 与 <see cref="TradeStreamManager"/> 独立：K 线数据按分钟/小时推送，生命周期不同，
/// 独立管理可避免互相影响且订阅策略更灵活（可按 interval 筛选）。
///
/// 职责：
/// 1. 启动时加载活跃策略绑定，推导 (exchange, pair, interval) 订阅集合
/// 2. 为每个订阅打开 <see cref="IMarketDataClient.SubscribeKlinesAsync"/> 流
/// 3. 检测 K 线闭合（新 candle OpenTime 变化 → 前一根收盘），推送到 <c>Channel&lt;KlineEvent&gt;</c>
/// 4. 策略变更时 <see cref="RefreshSubscriptionsAsync"/> 重新计算并调整连接
/// 5. 连接断开自动重连（指数退避）
/// </summary>
public sealed class KlineStreamManager(
    IServiceScopeFactory scopeFactory,
    Channel<KlineEvent> klineChannel,
    ILogger<KlineStreamManager> logger)
{
    private readonly ConcurrentDictionary<string, KlineSubscriptionState> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _shutdownCts;
    private readonly object _sync = new();

    /// <summary>启动所有订阅。由消费者在 BackgroundService.StartAsync 中调用。</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        lock (_sync)
            _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await RefreshSubscriptionsAsync(ct);
        logger.LogInformation("KlineStreamManager 启动, 已建立 {Count} 个 K 线订阅", _subscriptions.Count);
    }

    /// <summary>停止所有订阅。由消费者在 BackgroundService.StopAsync 中调用。</summary>
    public Task StopAsync()
    {
        CancellationTokenSource? cts;
        lock (_sync) { cts = _shutdownCts; _shutdownCts = null; }

        cts?.Cancel();
        foreach (var kvp in _subscriptions)
            kvp.Value.Dispose();
        _subscriptions.Clear();
        logger.LogInformation("KlineStreamManager 已停止");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 重新计算订阅集合。新增的启动、移除的断开、已有的保留。
    /// 由 WorkerCommandSubscriber 在收到 RefreshSubscriptions 命令时调用。
    /// </summary>
    public async Task RefreshSubscriptionsAsync(CancellationToken ct)
    {
        List<StrategyBinding> activeBindings;
        using (var scope = scopeFactory.CreateScope())
        {
            var bindingRepo = scope.ServiceProvider.GetRequiredService<IStrategyBindingRepository>();
            activeBindings = await bindingRepo.GetAllActiveAsync(ct);
        }

        // 推导需要的订阅集合 (exchangeId, pair, interval)
        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var subInfos = new Dictionary<string, (Guid ExchangeId, ExchangeType Type, string Pair, string Interval)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var binding in activeBindings)
        {
            var pairs = binding.Pairs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var timeframe = string.IsNullOrWhiteSpace(binding.Timeframe) ? "15m" : binding.Timeframe;

            foreach (var pair in pairs)
            {
                var key = BuildKey(binding.ExchangeId, pair, timeframe);
                needed.Add(key);
                if (!subInfos.ContainsKey(key))
                {
                    var exchangeType = await ResolveExchangeTypeAsync(binding.ExchangeId, ct);
                    subInfos[key] = (binding.ExchangeId, exchangeType, pair, timeframe);
                }
            }
        }

        // 移除不再需要的订阅
        foreach (var kvp in _subscriptions)
        {
            if (!needed.Contains(kvp.Key))
            {
                kvp.Value.Dispose();
                _subscriptions.TryRemove(kvp.Key, out _);
                logger.LogInformation("K 线订阅已断开: {Key}", kvp.Key);
            }
        }

        // 新增缺失的订阅
        foreach (var key in needed)
        {
            if (_subscriptions.ContainsKey(key))
                continue;

            if (!subInfos.TryGetValue(key, out var info))
                continue;

            var state = new KlineSubscriptionState(info.ExchangeId, info.Type, info.Pair, info.Interval);
            _subscriptions[key] = state;
            _ = RunSubscriptionLoopAsync(key, state, ct);
            logger.LogInformation("K 线订阅已建立: ExchangeId={ExchangeId}, Pair={Pair}, Interval={Interval}",
                info.ExchangeId, info.Pair, info.Interval);
        }
    }

    /// <summary>从 ExchangeRepository 解析交易所的实际类型。</summary>
    private async Task<ExchangeType> ResolveExchangeTypeAsync(Guid exchangeId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var exchangeRepo = scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
        var exchange = await exchangeRepo.GetByIdAsync(exchangeId, ct);
        return exchange?.Type ?? ExchangeType.Binance;
    }

    private async Task RunSubscriptionLoopAsync(string key, KlineSubscriptionState state, CancellationToken ct)
    {
        await Task.Yield();
        var retryDelay = TimeSpan.FromSeconds(1);
        const int maxRetryDelaySeconds = 30;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                state.MarkConnecting();
                using var scope = scopeFactory.CreateScope();
                var clientFactory = scope.ServiceProvider.GetRequiredService<IExchangeClientFactory>();
                var client = clientFactory.CreateClient(state.ExchangeType, "", "");

                // 追踪上一根 K 线用于收盘检测
                DateTime? lastOpenTime = null;
                Kline? lastKline = null;

                await foreach (var kline in client.SubscribeKlinesStreamAsync(state.Pair, state.Interval, ct))
                {
                    retryDelay = TimeSpan.FromSeconds(1);
                    state.MarkConnected();

                    // 相同 OpenTime → 同一根 K 线正在更新，跳过
                    if (lastOpenTime.HasValue && kline.Timestamp == lastOpenTime.Value)
                        continue;

                    // 首根 K 线：存入等待下一根到来后再判断闭合
                    if (lastOpenTime is null)
                    {
                        lastOpenTime = kline.Timestamp;
                        lastKline = kline;
                        continue;
                    }

                    // OpenTime 变化 → 前一根已闭合 → 推送 KlineEvent
                    var evt = new KlineEvent(state.Pair, state.ExchangeType, state.ExchangeId, state.Interval, lastKline!);
                    await klineChannel.Writer.WriteAsync(evt, ct);

                    // 更新为当前 K 线
                    lastOpenTime = kline.Timestamp;
                    lastKline = kline;
                }

                state.MarkDisconnected();
                try { await Task.Delay(retryDelay, ct); }
                catch (OperationCanceledException) { break; }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "K 线流断开, {Retry}s 后重连: Key={Key}", retryDelay.TotalSeconds, key);
                state.MarkDisconnected();

                try { await Task.Delay(retryDelay, ct); }
                catch (OperationCanceledException) { break; }

                retryDelay = TimeSpan.FromSeconds(Math.Min((int)retryDelay.TotalSeconds * 2, maxRetryDelaySeconds));
            }
        }
    }

    /// <summary>仅供测试/诊断之用。</summary>
    internal KlineSubscriptionState? GetState(string key)
        => _subscriptions.GetValueOrDefault(key);

    private static string BuildKey(Guid exchangeId, string pair, string interval)
        => $"{exchangeId:N}:{pair.ToUpperInvariant()}:{interval.ToLowerInvariant()}";
}

internal sealed class KlineSubscriptionState(
    Guid exchangeId,
    ExchangeType exchangeType,
    string pair,
    string interval) : IDisposable
{
    public Guid ExchangeId { get; } = exchangeId;
    public ExchangeType ExchangeType { get; } = exchangeType;
    public string Pair { get; } = pair;
    public string Interval { get; } = interval;

    public KlineSubscriptionStatus Status { get; private set; } = KlineSubscriptionStatus.Initial;
    public void MarkConnecting() => Status = KlineSubscriptionStatus.Connecting;
    public void MarkConnected() => Status = KlineSubscriptionStatus.Connected;
    public void MarkDisconnected() => Status = KlineSubscriptionStatus.Disconnected;

    public void Dispose() { }
}

internal enum KlineSubscriptionStatus { Initial, Connecting, Connected, Disconnected }
