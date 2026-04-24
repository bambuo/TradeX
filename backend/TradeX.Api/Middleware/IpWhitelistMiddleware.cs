using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace TradeX.Api.Middleware;

public class IpWhitelistMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> _whitelist = [];
    private static bool _enabled;

    public static void Configure(bool enabled, IEnumerable<string> allowedCidr)
    {
        _enabled = enabled;
        _whitelist.Clear();
        foreach (var cidr in allowedCidr)
        {
            var parts = cidr.Split('/');
            var ip = IPAddress.Parse(parts[0]);
            var prefix = parts.Length > 1 && int.TryParse(parts[1], out var len) ? len : 32;
            _whitelist.Add($"{ip}/{prefix}");
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_enabled && !IsAllowed(context.Connection.RemoteIpAddress))
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                code = "IP_NOT_ALLOWED",
                message = "IP 地址不在白名单中"
            }));
            return;
        }

        await next(context);
    }

    private static bool IsAllowed(IPAddress? remoteIp)
    {
        if (remoteIp is null || _whitelist.Count == 0) return true;

        foreach (var entry in _whitelist)
        {
            var parts = entry.Split('/');
            var network = IPAddress.Parse(parts[0]);
            var prefixLen = int.Parse(parts[1]);

            if (remoteIp.AddressFamily == network.AddressFamily)
            {
                var remoteBytes = remoteIp.GetAddressBytes();
                var networkBytes = network.GetAddressBytes();
                var fullBytes = prefixLen / 8;
                var remainingBits = prefixLen % 8;

                var match = true;
                for (var i = 0; i < fullBytes; i++)
                {
                    if (remoteBytes[i] != networkBytes[i]) { match = false; break; }
                }
                if (match && remainingBits > 0)
                {
                    var mask = (byte)(0xFF << (8 - remainingBits));
                    if ((remoteBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
                        match = false;
                }
                if (match) return true;
            }
        }

        return false;
    }
}
