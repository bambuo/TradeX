using System.Security.Cryptography;
using System.Text;

namespace TradeX.Exchange.Handlers;

public class BybitAuthHandler(string apiKey, string secretKey) : DelegatingHandler
{
    private const int RecvWindow = 5000;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        string payload;
        if (request.Content is { } content)
        {
            payload = await content.ReadAsStringAsync(ct);
        }
        else
        {
            payload = request.RequestUri?.Query.TrimStart('?') ?? "";
        }

        var signPayload = $"{timestamp}{apiKey}{RecvWindow}{payload}";
        var sign = Sign(signPayload);

        request.Headers.Add("X-BAPI-API-KEY", apiKey);
        request.Headers.Add("X-BAPI-TIMESTAMP", timestamp.ToString());
        request.Headers.Add("X-BAPI-SIGN", sign);
        request.Headers.Add("X-BAPI-RECV-WINDOW", RecvWindow.ToString());

        return await base.SendAsync(request, ct);
    }

    private string Sign(string payload)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }
}
