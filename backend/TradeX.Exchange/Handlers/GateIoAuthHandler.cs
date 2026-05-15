using System.Security.Cryptography;
using System.Text;

namespace TradeX.Exchange.Handlers;

public class GateIoAuthHandler(string apiKey, string secretKey) : DelegatingHandler
{
    private static readonly string _emptyBodyHash = Convert.ToHexStringLower(SHA512.HashData([]));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var method = request.Method.Method;
        var path = request.RequestUri?.AbsolutePath ?? "";
        var query = request.RequestUri?.Query.TrimStart('?') ?? "";

        string bodyHash;
        if (request.Content is { } content)
        {
            var body = await content.ReadAsStringAsync(ct);
            bodyHash = Convert.ToHexStringLower(SHA512.HashData(Encoding.UTF8.GetBytes(body)));
        }
        else
        {
            bodyHash = _emptyBodyHash;
        }

        var signPayload = $"{method}\n{path}\n{query}\n{bodyHash}\n{timestamp}";
        var sign = Sign(signPayload);

        request.Headers.Add("KEY", apiKey);
        request.Headers.Add("SIGN", sign);
        request.Headers.Add("Timestamp", timestamp);

        return await base.SendAsync(request, ct);
    }

    private string Sign(string payload)
    {
        var hash = HMACSHA512.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }
}
