using System.Security.Cryptography;
using System.Text;

namespace TradeX.Exchange.Handlers;

public class BinanceAuthHandler(string apiKey, string secretKey) : DelegatingHandler
{
    private static readonly HashSet<string> _signedPaths =
    [
        "/api/v3/order",
        "/api/v3/allOrders",
        "/api/v3/openOrders",
        "/api/v3/account"
    ];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Add("X-MBX-APIKEY", apiKey);

        if (request.RequestUri is { } uri && IsSignedPath(uri.AbsolutePath))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var query = uri.Query.TrimStart('?');
            query = string.IsNullOrEmpty(query)
                ? $"timestamp={timestamp}"
                : $"{query}&timestamp={timestamp}";

            var sign = Sign(query);
            var uriStr = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}?{query}&signature={sign}";
            if (uri.Port is not 80 and not 443)
                uriStr = $"{uri.Scheme}://{uri.Host}:{uri.Port}{uri.AbsolutePath}?{query}&signature={sign}";
            request.RequestUri = new Uri(uriStr);
        }

        return await base.SendAsync(request, ct);
    }

    private static bool IsSignedPath(string path) => _signedPaths.Contains(path);

    private string Sign(string query)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(query));
        return Convert.ToHexStringLower(hash);
    }
}
