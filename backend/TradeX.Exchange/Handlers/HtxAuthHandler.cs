using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TradeX.Exchange.Handlers;

public class HtxAuthHandler(string apiKey, string secretKey) : DelegatingHandler
{
    private static readonly HashSet<string> _signedPathPrefixes =
    [
        "/v1/account/accounts",
        "/v1/order/orders"
    ];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is { } uri && NeedsSigning(uri.AbsolutePath))
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            var method = request.Method.Method;
            var host = uri.Host;
            var path = uri.AbsolutePath;

            var existing = ParseQueryParams(uri.Query);
            var authParams = new Dictionary<string, string>
            {
                ["AccessKeyId"] = apiKey,
                ["SignatureMethod"] = "HmacSHA256",
                ["SignatureVersion"] = "2",
                ["Timestamp"] = timestamp
            };

            var allParams = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, v) in existing)
                allParams[k] = v;
            foreach (var (k, v) in authParams)
                allParams[k] = v;

            var queryString = string.Join("&", allParams.Select(kv =>
                $"{UrlEncode(kv.Key)}={UrlEncode(kv.Value)}"));

            var signStr = $"{method}\n{host}\n{path}\n{queryString}";
            var signature = Sign(signStr);

            var finalQuery = $"{queryString}&Signature={UrlEncode(signature)}";
            request.RequestUri = new Uri($"{uri.Scheme}://{host}:{uri.Port}{path}?{finalQuery}");
        }

        return await base.SendAsync(request, ct);
    }

    private static bool NeedsSigning(string path) =>
        _signedPathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private string Sign(string payload)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }

    private static string UrlEncode(string s) =>
        Uri.EscapeDataString(s).Replace("%20", "+", StringComparison.Ordinal);

    private static Dictionary<string, string> ParseQueryParams(string query)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(query) || query == "?") return result;

        var trimmed = query.TrimStart('?');
        foreach (var part in trimmed.Split('&'))
        {
            var eqIdx = part.IndexOf('=');
            if (eqIdx > 0)
            {
                var key = Uri.UnescapeDataString(part[..eqIdx]);
                var value = Uri.UnescapeDataString(part[(eqIdx + 1)..]);
                result[key] = value;
            }
        }
        return result;
    }
}
