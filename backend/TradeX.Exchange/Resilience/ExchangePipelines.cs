using System.Collections.Concurrent;
using System.Net;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using TradeX.Core.Enums;

namespace TradeX.Exchange.Resilience;

public static class ExchangePipelines
{
    private static readonly ConcurrentDictionary<ExchangeType, ResiliencePipeline<HttpResponseMessage>> Read = new();
    private static readonly ConcurrentDictionary<ExchangeType, ResiliencePipeline<HttpResponseMessage>> Write = new();

    public static ResiliencePipeline<HttpResponseMessage> ForRead(ExchangeType type)
        => Read.GetOrAdd(type, BuildRead);

    public static ResiliencePipeline<HttpResponseMessage> ForWrite(ExchangeType type)
        => Write.GetOrAdd(type, BuildWrite);

    private static ResiliencePipeline<HttpResponseMessage> BuildRead(ExchangeType _)
        => new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(IsTransientStatus),
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(200)
            })
            .AddCircuitBreaker(BuildBreakerOptions())
            .AddTimeout(TimeSpan.FromSeconds(15))
            .Build();

    private static ResiliencePipeline<HttpResponseMessage> BuildWrite(ExchangeType _)
        => new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(BuildBreakerOptions())
            .AddTimeout(TimeSpan.FromSeconds(10))
            .Build();

    private static CircuitBreakerStrategyOptions<HttpResponseMessage> BuildBreakerOptions() => new()
    {
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TimeoutRejectedException>()
            .HandleResult(r => (int)r.StatusCode >= 500),
        FailureRatio = 0.5,
        MinimumThroughput = 10,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(15)
    };

    private static bool IsTransientStatus(HttpResponseMessage r) =>
        (int)r.StatusCode >= 500
        || r.StatusCode == HttpStatusCode.RequestTimeout
        || r.StatusCode == HttpStatusCode.TooManyRequests;
}
