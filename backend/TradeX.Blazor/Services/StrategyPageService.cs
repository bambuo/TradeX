using System.Security.Claims;
using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Blazor.Services;

public sealed class StrategyPageService(
    ITraderRepository traderRepo,
    IStrategyRepository strategyRepo,
    IStrategyBindingRepository bindingRepo,
    IExchangeRepository exchangeRepo) : IStrategyPageService
{
    public async Task<IReadOnlyList<BindingItem>> GetAllAsync(ClaimsPrincipal user, Guid traderId, CancellationToken ct = default)
    {
        var userId = GetUserId(user);
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != userId)
            throw new InvalidOperationException("交易员不存在");

        var bindings = await bindingRepo.GetByTraderIdAsync(traderId, ct);
        var strategies = await strategyRepo.GetAllAsync(ct);
        var strategyMap = strategies.ToDictionary(s => s.Id, s => s.Name);
        var exchanges = await exchangeRepo.GetAllByUserIdAsync(userId, ct);
        var exchangeMap = exchanges.ToDictionary(e => e.Id, e => e.Name);

        return bindings
            .Select(d => new BindingItem(
                d.Id, d.StrategyId, d.Name, d.TraderId, d.ExchangeId,
                d.Pairs, d.Timeframe, d.Status,
                ResolveScope(d.Pairs, d.ExchangeId),
                strategyMap.GetValueOrDefault(d.StrategyId, "未知策略"),
                d.ExchangeId != Guid.Empty
                    ? exchangeMap.GetValueOrDefault(d.ExchangeId, "未知交易所")
                    : "-",
                FormatUtc(d.CreatedAt), FormatUtc(d.UpdatedAt)))
            .ToArray();
    }

    public async Task<IReadOnlyList<Strategy>> GetAllStrategiesAsync(CancellationToken ct = default)
    {
        return await strategyRepo.GetAllAsync(ct);
    }

    public async Task<IReadOnlyList<ExchangeOption>> GetExchangesAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = GetUserId(user);
        var exchanges = await exchangeRepo.GetAllByUserIdAsync(userId, ct);
        return exchanges
            .Select(e => new ExchangeOption(e.Id, e.Name, e.Type))
            .ToArray();
    }

    public async Task CreateAsync(ClaimsPrincipal user, Guid traderId, StrategyBindingFormModel form, CancellationToken ct = default)
    {
        var userId = GetUserId(user);
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != userId)
            throw new InvalidOperationException("交易员不存在");

        var strategy = await strategyRepo.GetByIdAsync(form.StrategyId, ct);
        if (strategy is null)
            throw new InvalidOperationException("策略模板不存在");

        var exchangeId = form.ExchangeId ?? Guid.Empty;

        var binding = new StrategyBinding
        {
            TraderId = traderId,
            StrategyId = form.StrategyId,
            Name = strategy.Name,
            ExchangeId = exchangeId,
            Pairs = form.Pairs,
            Timeframe = form.Timeframe,
            CreatedBy = userId
        };

        await bindingRepo.AddAsync(binding, ct);
    }

    public async Task UpdateAsync(Guid id, StrategyBindingFormModel form, CancellationToken ct = default)
    {
        var binding = await GetBindingAsync(id, ct);
        if (binding.Status == BindingStatus.Active)
            throw new InvalidOperationException("活跃策略不可编辑，请先禁用");

        binding.Pairs = form.Pairs;
        binding.Timeframe = form.Timeframe;

        await bindingRepo.UpdateAsync(binding, ct);
    }

    public async Task ToggleAsync(Guid id, bool enable, CancellationToken ct = default)
    {
        var binding = await GetBindingAsync(id, ct);

        if (enable)
        {
            var pairs = ParsePairs(binding.Pairs);
            foreach (var pair in pairs)
            {
                var hasConflict = await bindingRepo.ExistsActiveAsync(binding.TraderId, binding.ExchangeId, pair, id, ct);
                if (hasConflict)
                    throw new InvalidOperationException($"交易对 {pair} 上已有活跃策略");
            }

            binding.Status = BindingStatus.Active;
        }
        else
        {
            binding.Status = BindingStatus.Disabled;
        }

        await bindingRepo.UpdateAsync(binding, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var binding = await GetBindingAsync(id, ct);
        if (binding.Status == BindingStatus.Active)
            throw new InvalidOperationException("活跃策略不可删除，请先禁用");

        await bindingRepo.DeleteAsync(binding, ct);
    }

    private async Task<StrategyBinding> GetBindingAsync(Guid id, CancellationToken ct)
    {
        return await bindingRepo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("策略部署不存在");
    }

    private static string ResolveScope(string pairs, Guid exchangeId)
    {
        var hasPairs = !string.IsNullOrWhiteSpace(pairs)
            && pairs != "[]"
            && pairs.Replace("\"", "").Replace("[", "").Replace("]", "").Trim().Length > 0;
        if (hasPairs) return "Pair";
        if (exchangeId != Guid.Empty) return "Exchange";
        return "Trader";
    }

    private static string[] ParsePairs(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "[]") return [];
        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(raw);
            return parsed ?? [];
        }
        catch
        {
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    private static string FormatUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc
            ? dt.ToString("yyyy-MM-dd HH:mm:ss")
            : DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss");

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("用户未登录");
    }
}

public sealed record BindingItem(
    Guid Id,
    Guid StrategyId,
    string Name,
    Guid TraderId,
    Guid ExchangeId,
    string Pairs,
    string Timeframe,
    BindingStatus Status,
    string Scope,
    string StrategyLabel,
    string ExchangeLabel,
    string CreatedAt,
    string UpdatedAt);

public sealed record ExchangeOption(Guid Id, string Name, ExchangeType Type);

public sealed class StrategyBindingFormModel
{
    public Guid StrategyId { get; set; }
    public Guid? ExchangeId { get; set; }
    public string Pairs { get; set; } = "[]";
    public string Timeframe { get; set; } = "15m";
}
