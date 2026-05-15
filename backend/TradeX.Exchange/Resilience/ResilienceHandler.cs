using Polly;
using TradeX.Core.Enums;

namespace TradeX.Exchange.Resilience;

public sealed class ResilienceHandler(ExchangeType type) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var safeToRetry = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;
        var pipeline = safeToRetry
            ? ExchangePipelines.ForRead(type)
            : ExchangePipelines.ForWrite(type);

        if (!safeToRetry)
            return await pipeline.ExecuteAsync(async token => await base.SendAsync(request, token), ct);

        var template = await CloneAsync(request, ct);
        return await pipeline.ExecuteAsync(async token =>
        {
            var attempt = await CloneAsync(template, token);
            return await base.SendAsync(attempt, token);
        }, ct);
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage src, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(src.Method, src.RequestUri) { Version = src.Version };
        foreach (var h in src.Headers)
            clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        foreach (var kv in src.Options)
            ((IDictionary<string, object?>)clone.Options)[kv.Key] = kv.Value;

        if (src.Content is not null)
        {
            var ms = new MemoryStream();
            await src.Content.CopyToAsync(ms, ct);
            ms.Position = 0;
            var content = new StreamContent(ms);
            foreach (var h in src.Content.Headers)
                content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            clone.Content = content;
        }
        return clone;
    }
}
