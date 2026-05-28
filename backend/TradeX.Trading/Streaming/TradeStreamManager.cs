using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading.Streaming;

/// <summary>
/// 逐笔成交（Trade）WebSocket 订阅管理器。不作为 BackgroundService 注册，生命周期由
/// <see cref="StrategyEvaluationConsumer"/> 通过 <c>StartAsync</c> / <c>StopAsync</c> 驱动。
///
/// 职责：
/// 1. 启动时加载活跃策略绑定，推导 (exchange, pair) 订阅集合
/// 2. 为每个订阅打开 <see cref="IMarketDataClient.SubscribeTradesAsync"/> 流
/// 3. 每条 Trade 推送到 <c>Channel&lt;TradeEvent&gt;</c>
/// 4. 策略变更时 <see cref="RefreshSubscriptionsAsync"/> 重新计算并调整连接
/// 5. 连接断开自动重连（指数退避）
/// </summary>
public sealed class TradeStreamManager(
    IServiceScopeFactory scopeFactory,
    Channel<TradeEvent> tradeChannel,
    ILogger<TradeStreamManager> logger)
{
    private readonly ConcurrentDictionary<string, SubscriptionState> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _shutdownCts;
    private readonly object _sync = new();

    /// <summary>启动所有订阅。由消费者在 BackgroundService.StartAsync 中调用。</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        lock (_sync)
        {
            _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }

        await RefreshSubscriptionsAsync(ct);
        logger.LogInformation("TradeStreamManager 启动, 已建立 {Count} 个 Trade 订阅", _subscriptions.Count);
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

        logger.LogInformation("TradeStreamManager 已停止");
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

        // 推导需要的订阅集合 (exchangeId, pair)
        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exchangePairs = new Dictionary<string, (Guid ExchangeId, ExchangeType Type, string Pair)>(StringComparer.OrdinalIgnoreCase);

        foreach (var binding in activeBindings)
        {
            var pairs = binding.Pairs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var pair in pairs)
            {
                var key = BuildSubscriptionKey(binding.ExchangeId, pair);
                needed.Add(key);
                if (!exchangePairs.ContainsKey(key))
                {
                    var exchangeType = await ResolveExchangeTypeAsync(binding.ExchangeId, ct);
                    exchangePairs[key] = (binding.ExchangeId, exchangeType, pair);
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
                logger.LogInformation("Trade 订阅已断开: {Key}", kvp.Key);
            }
        }

        // 新增缺失的订阅
        foreach (var key in needed)
        {
            if (_subscriptions.ContainsKey(key))
                continue;

            if (!exchangePairs.TryGetValue(key, out var info))
                continue;

            var state = await CreateSubscriptionAsync(info.ExchangeId, info.Type, info.Pair, ct);
            if (state is not null)
            {
                _subscriptions[key] = state;
                _ = RunSubscriptionLoopAsync(key, state, ct);
                logger.LogInformation("Trade 订阅已建立: ExchangeId={ExchangeId}, Pair={Pair}",
                    info.ExchangeId, info.Pair);
            }
        }
    }

    private async Task<SubscriptionState?> CreateSubscriptionAsync(
        Guid exchangeId, ExchangeType exchangeType, string pair, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var clientFactory = scope.ServiceProvider.GetRequiredService<IExchangeClientFactory>();

            // 公开 Trade 接口 — 无需认证
            var client = clientFactory.CreateClient(exchangeType, "", "");

            return new SubscriptionState(exchangeId, exchangeType, pair, null, client);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "创建 Trade 订阅失败: ExchangeType={ExchangeType}, Pair={Pair}", exchangeType, pair);
            return null;
        }
    }

    private async Task RunSubscriptionLoopAsync(string key, SubscriptionState state, CancellationToken ct)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        const int maxRetryDelaySeconds = 30;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                state.MarkConnecting();
                await foreach (var trade in state.Client.SubscribeTradesAsync(state.Pair, ct))
                {
                    retryDelay = TimeSpan.FromSeconds(1);
                    state.MarkConnected();

                    var evt = new TradeEvent(state.Pair, state.ExchangeType, state.ExchangeId, trade);
                    await tradeChannel.Writer.WriteAsync(evt, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Trade 流断开, {Retry}s 后重连: Key={Key}", retryDelay.TotalSeconds, key);
                state.MarkDisconnected();

                try { await Task.Delay(retryDelay, ct); }
                catch (OperationCanceledException) { break; }

                retryDelay = TimeSpan.FromSeconds(Math.Min((int)retryDelay.TotalSeconds * 2, maxRetryDelaySeconds));
            }
        }
    }

    private static string BuildSubscriptionKey(Guid exchangeId, string pair)
        => $"{exchangeId:N}:{pair.ToUpperInvariant()}";

    /// <summary>从 ExchangeRepository 解析交易所的实际类型。</summary>
    private async Task<ExchangeType> ResolveExchangeTypeAsync(Guid exchangeId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var exchangeRepo = scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
        var exchange = await exchangeRepo.GetByIdAsync(exchangeId, ct);
        return exchange?.Type ?? ExchangeType.Binance;
    }

    internal SubscriptionState? GetState(string key)
        => _subscriptions.GetValueOrDefault(key);
}

internal sealed class SubscriptionState(
    Guid exchangeId,
    ExchangeType exchangeType,
    string pair,
    string? timeframe,
    IExchangeClient client) : IDisposable
{
    public Guid ExchangeId { get; } = exchangeId;
    public ExchangeType ExchangeType { get; } = exchangeType;
    public string Pair { get; } = pair;
    public string? Timeframe { get; } = timeframe;
    public IExchangeClient Client { get; } = client;

    public SubscriptionStatus Status { get; private set; } = SubscriptionStatus.Initial;
    public void MarkConnecting() => Status = SubscriptionStatus.Connecting;
    public void MarkConnected() => Status = SubscriptionStatus.Connected;
    public void MarkDisconnected() => Status = SubscriptionStatus.Disconnected;

    public void Dispose()
    {
        if (Client is IDisposable d) d.Dispose();
    }
}

internal enum SubscriptionStatus { Initial, Connecting, Connected, Disconnected }
