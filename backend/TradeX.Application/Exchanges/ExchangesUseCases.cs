using TradeX.Application.Common;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Application.Exchanges;

// ─── DTOs ───

public sealed record ExchangeDto(
    Guid Id, string Name, string Type, string Status,
    DateTime? LastTestedAt, string? TestResult,
    DateTime CreatedAt, DateTime UpdatedAt);

public sealed record ExchangeAssetDto(string Currency, decimal Balance);

public sealed record ExchangePairDto(
    string Pair, int PricePrecision, int QuantityPrecision, decimal MinNotional,
    decimal Price, decimal PriceChangePercent, decimal Volume,
    decimal HighPrice, decimal LowPrice);

public sealed record ExchangeTestResultDto(
    bool Connected, string? Error, string? Message,
    IReadOnlyDictionary<string, bool>? Permissions, bool HasWarning);

// ─── Queries ───

public sealed record GetExchangesQuery(Guid CurrentUserId);
public sealed record GetExchangeByIdQuery(Guid Id, Guid CurrentUserId);
public sealed record GetExchangeOrdersQuery(
    Guid Id, Guid CurrentUserId, string Type = "open",
    int Page = 1, int PageSize = 20, string? Pair = null,
    string? Side = null, string? OrderType = null, string? OrderStatus = null);

public sealed record PagedExchangeOrderDto(
    List<ExchangeOrderDto> Data, int Total, int Page, int PageSize);

// ─── Commands ───

public sealed record CreateExchangeCommand(
    Guid CurrentUserId, string Name, string Type,
    string ApiKey, string SecretKey, string? Passphrase, string? Password);

public sealed record UpdateExchangeCommand(
    Guid Id, Guid CurrentUserId,
    string? Name = null, string? ApiKey = null, string? SecretKey = null, string? Passphrase = null, string? Password = null);

public sealed record DeleteExchangeCommand(Guid Id, Guid CurrentUserId);
public sealed record TestExchangeCommand(Guid Id, Guid CurrentUserId);
public sealed record GetExchangeAssetsCommand(Guid Id, Guid CurrentUserId);
public sealed record GetExchangePairsCommand(Guid Id, Guid CurrentUserId, string? QuoteCurrency = "USDT");
public sealed record ToggleExchangeCommand(Guid Id, Guid CurrentUserId, bool Enable);

// ══════════════════════════════════════════════════════════════════════════════
// UseCases
// ══════════════════════════════════════════════════════════════════════════════

public sealed class GetExchangesUseCase(
    IExchangeRepository exchangeRepo) : IUseCase<GetExchangesQuery, Result<List<ExchangeDto>>>
{
    public async Task<Result<List<ExchangeDto>>> ExecuteAsync(GetExchangesQuery q, CancellationToken ct = default)
    {
        var exchanges = await exchangeRepo.GetAllByUserIdAsync(q.CurrentUserId, ct);
        var list = exchanges.Select(ToDto).ToList();
        return Result<List<ExchangeDto>>.Ok(list);
    }

    internal static ExchangeDto ToDto(Exchange e) => new(
        e.Id, e.Name, e.Type.ToString(), e.Status.ToString(),
        e.LastTestedAt, e.TestResult, e.CreatedAt, e.UpdatedAt);
}

public sealed class GetExchangeByIdUseCase(
    IExchangeRepository exchangeRepo) : IUseCase<GetExchangeByIdQuery, Result<ExchangeDto>>
{
    public async Task<Result<ExchangeDto>> ExecuteAsync(GetExchangeByIdQuery q, CancellationToken ct = default)
    {
        var exchange = await exchangeRepo.GetByIdAsync(q.Id, ct);
        if (exchange is null)
            return Result<ExchangeDto>.NotFound("交易所不存在");

        return Result<ExchangeDto>.Ok(GetExchangesUseCase.ToDto(exchange));
    }
}

public sealed class CreateExchangeUseCase(
    IExchangeRepository exchangeRepo,
    IEncryptionService encryption) : IUseCase<CreateExchangeCommand, Result<ExchangeDto>>
{
    public async Task<Result<ExchangeDto>> ExecuteAsync(CreateExchangeCommand cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Name) || string.IsNullOrWhiteSpace(cmd.ApiKey) || string.IsNullOrWhiteSpace(cmd.SecretKey))
            return Result<ExchangeDto>.BadRequest("名称、API Key 和 Secret Key 不能为空");

        if (!Enum.TryParse<ExchangeType>(cmd.Type, true, out var exchangeType))
            return Result<ExchangeDto>.BadRequest($"不支持的交易所类型: {cmd.Type}");

        if (!await exchangeRepo.IsNameUniqueAsync(cmd.Name, ct))
            return Result<ExchangeDto>.Conflict("交易所名称已存在");

        var exchange = Exchange.Create(
            cmd.CurrentUserId, cmd.Name, exchangeType,
            encryption.Encrypt(cmd.ApiKey),
            encryption.Encrypt(cmd.SecretKey),
            cmd.Passphrase is not null ? encryption.Encrypt(cmd.Passphrase) : null);

        await exchangeRepo.AddAsync(exchange, ct);

        return Result<ExchangeDto>.Created(GetExchangesUseCase.ToDto(exchange));
    }
}

public sealed class UpdateExchangeUseCase(
    IExchangeRepository exchangeRepo,
    IEncryptionService encryption) : IUseCase<UpdateExchangeCommand, Result<ExchangeDto>>
{
    public async Task<Result<ExchangeDto>> ExecuteAsync(UpdateExchangeCommand cmd, CancellationToken ct = default)
    {
        var exchange = await exchangeRepo.GetByIdAsync(cmd.Id, ct);
        if (exchange is null)
            return Result<ExchangeDto>.NotFound("交易所不存在");

        if (!string.IsNullOrWhiteSpace(cmd.Name))
        {
            if (exchange.Name != cmd.Name && !await exchangeRepo.IsNameUniqueAsync(cmd.Name, ct))
                return Result<ExchangeDto>.Conflict("交易所名称已存在");
            exchange.Name = cmd.Name;
        }

        if (!string.IsNullOrWhiteSpace(cmd.ApiKey))
            exchange.ApiKeyEncrypted = encryption.Encrypt(cmd.ApiKey);
        if (!string.IsNullOrWhiteSpace(cmd.SecretKey))
            exchange.SecretKeyEncrypted = encryption.Encrypt(cmd.SecretKey);
        if (cmd.Passphrase is not null)
            exchange.PassphraseEncrypted = encryption.Encrypt(cmd.Passphrase);

        exchange.UpdatedAt = DateTime.UtcNow;
        await exchangeRepo.UpdateAsync(exchange, ct);

        return Result<ExchangeDto>.Ok(GetExchangesUseCase.ToDto(exchange));
    }
}

public sealed class DeleteExchangeUseCase(
    IExchangeRepository exchangeRepo) : IUseCase<DeleteExchangeCommand, Result>
{
    public async Task<Result> ExecuteAsync(DeleteExchangeCommand cmd, CancellationToken ct = default)
    {
        var exchange = await exchangeRepo.GetByIdAsync(cmd.Id, ct);
        if (exchange is null)
            return Result.NotFound("交易所不存在");

        await exchangeRepo.DeleteAsync(exchange, ct);
        return Result.NoContent();
    }
}

public sealed class TestExchangeUseCase(
    IExchangeRepository exchangeRepo,
    IEncryptionService encryption,
    IExchangeClientFactory clientFactory) : IUseCase<TestExchangeCommand, Result<ExchangeTestResultDto>>
{
    public async Task<Result<ExchangeTestResultDto>> ExecuteAsync(TestExchangeCommand cmd, CancellationToken ct = default)
    {
        var exchange = await exchangeRepo.GetByIdAsync(cmd.Id, ct);
        if (exchange is null)
            return Result<ExchangeTestResultDto>.NotFound("交易所不存在");

        if (exchange.Status == ExchangeStatus.Disabled)
            return Result<ExchangeTestResultDto>.BadRequest("交易所已禁用");

        try
        {
            var apiKey = encryption.Decrypt(exchange.ApiKeyEncrypted);
            var secretKey = encryption.Decrypt(exchange.SecretKeyEncrypted);
            var passphrase = exchange.PassphraseEncrypted is not null ? encryption.Decrypt(exchange.PassphraseEncrypted) : null;

            var client = clientFactory.CreateClient(exchange.Type, apiKey, secretKey, passphrase);
            var result = await client.TestConnectionAsync(ct);

            exchange.RecordTestResult(result.Success, result.Message);
            await exchangeRepo.UpdateAsync(exchange, ct);

            var perms = result.Permissions ?? [];
            var hasWarning = perms.GetValueOrDefault("withdraw") || (perms.TryGetValue("ipRestrict", out var ipRestrict) && !ipRestrict);

            return Result<ExchangeTestResultDto>.Ok(new ExchangeTestResultDto(
                result.Success, result.Success ? null : result.Message, result.Message,
                perms, hasWarning));
        }
        catch (Exception ex)
        {
            exchange.RecordTestResult(false, ex.Message);
            await exchangeRepo.UpdateAsync(exchange, ct);
            return Result<ExchangeTestResultDto>.Ok(new ExchangeTestResultDto(
                false, ex.Message, ex.Message, null, false));
        }
    }
}

public sealed class GetExchangeAssetsUseCase(
    IExchangeRepository exchangeRepo,
    IEncryptionService encryption,
    IExchangeClientFactory clientFactory) : IUseCase<GetExchangeAssetsCommand, Result<List<ExchangeAssetDto>>>
{
    public async Task<Result<List<ExchangeAssetDto>>> ExecuteAsync(GetExchangeAssetsCommand cmd, CancellationToken ct = default)
    {
        var exchange = await exchangeRepo.GetByIdAsync(cmd.Id, ct);
        if (exchange is null)
            return Result<List<ExchangeAssetDto>>.NotFound("交易所不存在");

        if (exchange.Status == ExchangeStatus.Disabled)
            return Result<List<ExchangeAssetDto>>.BadRequest("交易所已禁用");

        try
        {
            var apiKey = encryption.Decrypt(exchange.ApiKeyEncrypted);
            var secretKey = encryption.Decrypt(exchange.SecretKeyEncrypted);
            var passphrase = exchange.PassphraseEncrypted is not null ? encryption.Decrypt(exchange.PassphraseEncrypted) : null;

            var client = clientFactory.CreateClient(exchange.Type, apiKey, secretKey, passphrase);
            var assets = await client.GetAssetBalancesAsync(ct);
            var list = assets
                .Select(a => new ExchangeAssetDto(a.Key, a.Value))
                .OrderByDescending(a => a.Balance)
                .ToList();

            return Result<List<ExchangeAssetDto>>.Ok(list);
        }
        catch (Exception ex)
        {
            return Result<List<ExchangeAssetDto>>.BadRequest($"获取资产失败: {ex.Message}");
        }
    }
}

public sealed class GetExchangePairsUseCase(
    IExchangeRepository exchangeRepo,
    IEncryptionService encryption,
    IExchangeClientFactory clientFactory) : IUseCase<GetExchangePairsCommand, Result<List<ExchangePairDto>>>
{
    public async Task<Result<List<ExchangePairDto>>> ExecuteAsync(GetExchangePairsCommand cmd, CancellationToken ct = default)
    {
        var exchange = await exchangeRepo.GetByIdAsync(cmd.Id, ct);
        if (exchange is null)
            return Result<List<ExchangePairDto>>.NotFound("交易所不存在");

        if (exchange.Status == ExchangeStatus.Disabled)
            return Result<List<ExchangePairDto>>.BadRequest("交易所已禁用");

        try
        {
            var apiKey = encryption.Decrypt(exchange.ApiKeyEncrypted);
            var secretKey = encryption.Decrypt(exchange.SecretKeyEncrypted);
            var passphrase = exchange.PassphraseEncrypted is not null ? encryption.Decrypt(exchange.PassphraseEncrypted) : null;

            var client = clientFactory.CreateClient(exchange.Type, apiKey, secretKey, passphrase);

            var rulesTask = client.GetPairRulesAsync(ct);
            var tickerTask = client.GetTickerPricesAsync(ct);
            await Task.WhenAll(rulesTask, tickerTask);

            var rules = await rulesTask;
            var tickers = await tickerTask;
            var tickerMap = tickers.ToDictionary(t => t.Pair, t => t);

            var quote = cmd.QuoteCurrency;
            var pairs = rules
                .Where(r => quote is null ||
                    r.Pair.EndsWith(quote, StringComparison.OrdinalIgnoreCase))
                .Select(r =>
                {
                    var t = tickerMap.GetValueOrDefault(r.Pair);
                    return new ExchangePairDto(
                        r.Pair, r.PricePrecision, r.QuantityPrecision, r.MinNotional,
                        t?.Price ?? 0, t?.PriceChangePercent ?? 0, t?.Volume ?? 0,
                        t?.HighPrice ?? 0, t?.LowPrice ?? 0);
                })
                .OrderBy(r => r.Pair)
                .ToList();

            return Result<List<ExchangePairDto>>.Ok(pairs);
        }
        catch (Exception ex)
        {
            return Result<List<ExchangePairDto>>.BadRequest($"获取交易对数据失败: {ex.Message}");
        }
    }
}

public sealed class GetExchangeOrdersUseCase(
    IExchangeRepository exchangeRepo,
    IEncryptionService encryption,
    IExchangeClientFactory clientFactory,
    IExchangeOrderHistoryRepository historyRepo) : IUseCase<GetExchangeOrdersQuery, Result<PagedExchangeOrderDto>>
{
    public async Task<Result<PagedExchangeOrderDto>> ExecuteAsync(GetExchangeOrdersQuery q, CancellationToken ct = default)
    {
        var exchange = await exchangeRepo.GetByIdAsync(q.Id, ct);
        if (exchange is null)
            return Result<PagedExchangeOrderDto>.NotFound("交易所不存在");

        if (exchange.Status == ExchangeStatus.Disabled)
            return Result<PagedExchangeOrderDto>.BadRequest("交易所已禁用");

        try
        {
            if (q.Type == "history")
            {
                var (items, total) = await historyRepo.GetPagedAsync(
                    q.Id, q.Page, q.PageSize, q.Pair, q.Side, q.OrderType, q.OrderStatus, ct);

                var dtos = items.Select(i => new ExchangeOrderDto(
                    i.Pair, i.Side, i.Type, i.Status,
                    i.Price, i.Quantity, i.FilledQuantity,
                    i.ExchangeOrderId, i.PlacedAt)).ToList();

                return Result<PagedExchangeOrderDto>.Ok(new PagedExchangeOrderDto(
                    dtos, total, q.Page, q.PageSize));
            }

            // 挂单走 SDK 实时查询
            var apiKey = encryption.Decrypt(exchange.ApiKeyEncrypted);
            var secretKey = encryption.Decrypt(exchange.SecretKeyEncrypted);
            var passphrase = exchange.PassphraseEncrypted is not null ? encryption.Decrypt(exchange.PassphraseEncrypted) : null;

            var client = clientFactory.CreateClient(exchange.Type, apiKey, secretKey, passphrase);
            var orders = await client.GetOpenOrdersAsync(ct);

            return Result<PagedExchangeOrderDto>.Ok(new PagedExchangeOrderDto(
                [.. orders], orders.Length, 1, orders.Length));
        }
        catch (Exception ex)
        {
            return Result<PagedExchangeOrderDto>.BadRequest($"获取订单失败: {ex.Message}");
        }
    }
}

public sealed class ToggleExchangeUseCase(
    IExchangeRepository exchangeRepo) : IUseCase<ToggleExchangeCommand, Result>
{
    public async Task<Result> ExecuteAsync(ToggleExchangeCommand cmd, CancellationToken ct = default)
    {
        var exchange = await exchangeRepo.GetByIdAsync(cmd.Id, ct);
        if (exchange is null)
            return Result.NotFound("交易所不存在");

        if (cmd.Enable)
            exchange.Enable();
        else
            exchange.Disable();

        await exchangeRepo.UpdateAsync(exchange, ct);
        return Result.Ok();
    }
}
