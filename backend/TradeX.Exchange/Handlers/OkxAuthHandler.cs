using System.Security.Cryptography;
using System.Text;

namespace TradeX.Exchange.Handlers;

public class OkxAuthHandler(string apiKey, string secretKey, string passphrase) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var method = request.Method.Method;
        var path = request.RequestUri?.AbsolutePath ?? "";
        var query = request.RequestUri?.Query.TrimStart('?') ?? "";

        string signPayload;
        string bodyContent = "";

        if (request.Content is { } content)
        {
            bodyContent = await content.ReadAsStringAsync(ct);
            signPayload = $"{timestamp}{method}{path}{bodyContent}";
        }
        else
        {
            signPayload = query.Length > 0
                ? $"{timestamp}{method}{path}?{query}"
                : $"{timestamp}{method}{path}";
        }

        var sign = Sign(signPayload);

        request.Headers.Add("OK-ACCESS-KEY", apiKey);
        request.Headers.Add("OK-ACCESS-SIGN", sign);
        request.Headers.Add("OK-ACCESS-TIMESTAMP", timestamp);
        request.Headers.Add("OK-ACCESS-PASSPHRASE", passphrase);

        return await base.SendAsync(request, ct);
    }

    private string Sign(string payload)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }
}
