using System.Security.Claims;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Blazor.Services;

public sealed class ExchangePageService(
    IExchangeRepository exchangeRepo,
    ITraderRepository traderRepo,
    IExchangeClientFactory clientFactory,
    IEncryptionService encryption)
{
    public async Task<IReadOnlyList<ExchangeItem>> GetAllAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = GetUserId(user);
        var traders = await traderRepo.GetByUserIdAsync(userId, ct);
        var traderMap = traders.ToDictionary(t => t.Id, t => t.Name);
        var exchanges = await exchangeRepo.GetAllByUserIdAsync(userId, ct);

        return exchanges
            .OrderBy(e => e.Name)
            .Select(e => new ExchangeItem(
                e.Id,
                e.TraderId,
                e.TraderId.HasValue ? traderMap.GetValueOrDefault(e.TraderId.Value, "未知") : "全局",
                e.Name,
                e.Type,
                e.Status == ExchangeStatus.Enabled,
                e.LastTestedAt,
                e.TestResult,
                e.CreatedAt,
                e.UpdatedAt))
            .ToArray();
    }

    public async Task CreateAsync(ClaimsPrincipal user, ExchangeFormModel form, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(form.Name) || string.IsNullOrWhiteSpace(form.ApiKey) || string.IsNullOrWhiteSpace(form.SecretKey))
        {
            throw new InvalidOperationException("名称、API Key 和 Secret Key 不能为空");
        }

        if (!await exchangeRepo.IsNameUniqueAsync(form.Name, ct))
        {
            throw new InvalidOperationException("交易所名称已存在");
        }

        await exchangeRepo.AddAsync(new TradeX.Core.Models.Exchange
        {
            TraderId = null,
            Name = form.Name,
            Type = form.ExchangeType,
            ApiKeyEncrypted = encryption.Encrypt(form.ApiKey),
            SecretKeyEncrypted = encryption.Encrypt(form.SecretKey),
            PassphraseEncrypted = string.IsNullOrWhiteSpace(form.Passphrase) ? null : encryption.Encrypt(form.Passphrase),
            CreatedBy = GetUserId(user)
        }, ct);
    }

    public async Task UpdateAsync(Guid id, ExchangeFormModel form, CancellationToken ct = default)
    {
        var exchange = await GetExchangeAsync(id, ct);
        if (!string.IsNullOrWhiteSpace(form.Name))
        {
            if (exchange.Name != form.Name && !await exchangeRepo.IsNameUniqueAsync(form.Name, ct))
            {
                throw new InvalidOperationException("交易所名称已存在");
            }

            exchange.Name = form.Name;
        }

        if (!string.IsNullOrWhiteSpace(form.ApiKey))
        {
            exchange.ApiKeyEncrypted = encryption.Encrypt(form.ApiKey);
        }

        if (!string.IsNullOrWhiteSpace(form.SecretKey))
        {
            exchange.SecretKeyEncrypted = encryption.Encrypt(form.SecretKey);
        }

        if (form.Passphrase is not null)
        {
            exchange.PassphraseEncrypted = string.IsNullOrWhiteSpace(form.Passphrase) ? null : encryption.Encrypt(form.Passphrase);
        }

        await exchangeRepo.UpdateAsync(exchange, ct);
    }

    public async Task ToggleAsync(Guid id, bool enable, CancellationToken ct = default)
    {
        var exchange = await GetExchangeAsync(id, ct);
        exchange.Status = enable ? ExchangeStatus.Enabled : ExchangeStatus.Disabled;
        await exchangeRepo.UpdateAsync(exchange, ct);
    }

    public async Task<ConnectionTestView> TestConnectionAsync(Guid id, CancellationToken ct = default)
    {
        var exchange = await GetExchangeAsync(id, ct);
        if (exchange.Status == ExchangeStatus.Disabled)
        {
            throw new InvalidOperationException("交易所已禁用");
        }

        try
        {
            var client = CreateClient(exchange);
            var result = await client.TestConnectionAsync(ct);
            exchange.LastTestedAt = DateTime.UtcNow;
            exchange.TestResult = result.Message;
            await exchangeRepo.UpdateAsync(exchange, ct);
            return new ConnectionTestView(result.Success, result.Success ? null : result.Message);
        }
        catch (Exception ex)
        {
            return new ConnectionTestView(false, ex.Message);
        }
    }

    public async Task<IReadOnlyList<AssetBalanceView>> GetAssetsAsync(Guid id, CancellationToken ct = default)
    {
        var exchange = await GetEnabledExchangeAsync(id, ct);
        var assets = await CreateClient(exchange).GetAssetBalancesAsync(ct);
        return assets
            .Select(a => new AssetBalanceView(a.Key, a.Value))
            .OrderByDescending(a => a.Balance)
            .ToArray();
    }

    public async Task<IReadOnlyList<ExchangeOrderDto>> GetOrdersAsync(Guid id, OrderListType type, CancellationToken ct = default)
    {
        var exchange = await GetEnabledExchangeAsync(id, ct);
        var client = CreateClient(exchange);
        return type == OrderListType.History
            ? await client.GetOrderHistoryAsync(ct)
            : await client.GetOpenOrdersAsync(ct);
    }

    private async Task<TradeX.Core.Models.Exchange> GetEnabledExchangeAsync(Guid id, CancellationToken ct)
    {
        var exchange = await GetExchangeAsync(id, ct);
        if (exchange.Status == ExchangeStatus.Disabled)
        {
            throw new InvalidOperationException("交易所已禁用");
        }

        return exchange;
    }

    private async Task<TradeX.Core.Models.Exchange> GetExchangeAsync(Guid id, CancellationToken ct)
    {
        return await exchangeRepo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("交易所不存在");
    }

    private IExchangeClient CreateClient(TradeX.Core.Models.Exchange exchange)
    {
        var apiKey = encryption.Decrypt(exchange.ApiKeyEncrypted);
        var secretKey = encryption.Decrypt(exchange.SecretKeyEncrypted);
        var passphrase = exchange.PassphraseEncrypted is not null ? encryption.Decrypt(exchange.PassphraseEncrypted) : null;
        return clientFactory.CreateClient(exchange.Type, apiKey, secretKey, passphrase);
    }

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out var userId) ? userId : throw new UnauthorizedAccessException("用户未登录");
    }
}

public sealed record ExchangeItem(
    Guid Id,
    Guid? TraderId,
    string TraderName,
    string Label,
    ExchangeType ExchangeType,
    bool IsEnabled,
    DateTime? LastTestedAt,
    string? TestResult,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record AssetBalanceView(string Currency, decimal Balance);
public sealed record ConnectionTestView(bool Connected, string? Error);

public enum OrderListType
{
    Open,
    History
}

public sealed class ExchangeFormModel
{
    public string Name { get; set; } = string.Empty;
    public ExchangeType ExchangeType { get; set; } = ExchangeType.Binance;
    public string ApiKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string? Passphrase { get; set; }
}
