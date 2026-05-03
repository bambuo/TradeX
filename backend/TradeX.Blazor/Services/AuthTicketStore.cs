using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace TradeX.Blazor.Services;

public sealed class AuthTicketStore
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, AuthTicket> tickets = [];

    public string Issue(AuthTokens tokens)
    {
        RemoveExpiredTickets();

        var ticket = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        tickets[ticket] = new AuthTicket(tokens, DateTimeOffset.UtcNow.Add(TicketLifetime));
        return ticket;
    }

    public AuthTokens? Redeem(string ticket)
    {
        if (!tickets.TryRemove(ticket, out var authTicket))
        {
            return null;
        }

        return authTicket.ExpiresAt < DateTimeOffset.UtcNow ? null : authTicket.Tokens;
    }

    private void RemoveExpiredTickets()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, ticket) in tickets)
        {
            if (ticket.ExpiresAt < now)
            {
                tickets.TryRemove(key, out _);
            }
        }
    }

    private sealed record AuthTicket(AuthTokens Tokens, DateTimeOffset ExpiresAt);
}
