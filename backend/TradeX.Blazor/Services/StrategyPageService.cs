using System.Security.Claims;
using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Blazor.Services;

public sealed class StrategyPageService(
    ITraderRepository traderRepo,
    IStrategyRepository strategyRepo,
    IStrategyDeploymentRepository deploymentRepo,
    IExchangeRepository exchangeRepo) : IStrategyPageService
{
    public async Task<IReadOnlyList<DeploymentItem>> GetAllAsync(ClaimsPrincipal user, Guid traderId, CancellationToken ct = default)
    {
        var userId = GetUserId(user);
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != userId)
            throw new InvalidOperationException("交易员不存在");

        var deployments = await deploymentRepo.GetByTraderIdAsync(traderId, ct);
        var strategies = await strategyRepo.GetAllAsync(ct);
        var strategyMap = strategies.ToDictionary(s => s.Id, s => s.Name);
        var exchanges = await exchangeRepo.GetAllByUserIdAsync(userId, ct);
        var exchangeMap = exchanges.ToDictionary(e => e.Id, e => e.Name);

        return deployments
            .Select(d => new DeploymentItem(
                d.Id, d.StrategyId, d.Name, d.TraderId, d.ExchangeId,
                d.SymbolIds, d.Timeframe, d.Status,
                ResolveScope(d.SymbolIds, d.ExchangeId),
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

    public async Task CreateAsync(ClaimsPrincipal user, Guid traderId, StrategyDeploymentFormModel form, CancellationToken ct = default)
    {
        var userId = GetUserId(user);
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != userId)
            throw new InvalidOperationException("交易员不存在");

        var strategy = await strategyRepo.GetByIdAsync(form.StrategyId, ct);
        if (strategy is null)
            throw new InvalidOperationException("策略模板不存在");

        var exchangeId = form.ExchangeId ?? Guid.Empty;

        var deployment = new StrategyDeployment
        {
            TraderId = traderId,
            StrategyId = form.StrategyId,
            Name = strategy.Name,
            ExchangeId = exchangeId,
            SymbolIds = form.SymbolIds,
            Timeframe = form.Timeframe,
            CreatedBy = userId
        };

        await deploymentRepo.AddAsync(deployment, ct);
    }

    public async Task UpdateAsync(Guid id, StrategyDeploymentFormModel form, CancellationToken ct = default)
    {
        var deployment = await GetDeploymentAsync(id, ct);
        if (deployment.Status == DeploymentStatus.Active)
            throw new InvalidOperationException("活跃策略不可编辑，请先禁用");

        deployment.SymbolIds = form.SymbolIds;
        deployment.Timeframe = form.Timeframe;

        await deploymentRepo.UpdateAsync(deployment, ct);
    }

    public async Task ToggleAsync(Guid id, bool enable, CancellationToken ct = default)
    {
        var deployment = await GetDeploymentAsync(id, ct);

        if (enable)
        {
            if (deployment.Status == DeploymentStatus.Draft)
                throw new InvalidOperationException("草稿策略必须先通过回测才能启用");

            var symbolIds = ParseSymbolIds(deployment.SymbolIds);
            foreach (var symbolId in symbolIds)
            {
                var hasConflict = await deploymentRepo.ExistsActiveAsync(
                    deployment.TraderId, deployment.ExchangeId, symbolId, id, ct);
                if (hasConflict)
                    throw new InvalidOperationException($"交易对 {symbolId} 上已有活跃策略");
            }

            deployment.Status = DeploymentStatus.Active;
        }
        else
        {
            deployment.Status = DeploymentStatus.Disabled;
        }

        await deploymentRepo.UpdateAsync(deployment, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var deployment = await GetDeploymentAsync(id, ct);
        if (deployment.Status == DeploymentStatus.Active)
            throw new InvalidOperationException("活跃策略不可删除，请先禁用");

        await deploymentRepo.DeleteAsync(deployment, ct);
    }

    private async Task<StrategyDeployment> GetDeploymentAsync(Guid id, CancellationToken ct)
    {
        return await deploymentRepo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("策略部署不存在");
    }

    private static string ResolveScope(string symbolIds, Guid exchangeId)
    {
        var hasSymbols = !string.IsNullOrWhiteSpace(symbolIds)
            && symbolIds != "[]"
            && symbolIds.Replace("\"", "").Replace("[", "").Replace("]", "").Trim().Length > 0;
        if (hasSymbols) return "Symbol";
        if (exchangeId != Guid.Empty) return "Exchange";
        return "Trader";
    }

    private static string[] ParseSymbolIds(string raw)
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

public sealed record DeploymentItem(
    Guid Id,
    Guid StrategyId,
    string Name,
    Guid TraderId,
    Guid ExchangeId,
    string SymbolIds,
    string Timeframe,
    DeploymentStatus Status,
    string Scope,
    string StrategyLabel,
    string ExchangeLabel,
    string CreatedAt,
    string UpdatedAt);

public sealed record ExchangeOption(Guid Id, string Name, ExchangeType Type);

public sealed class StrategyDeploymentFormModel
{
    public Guid StrategyId { get; set; }
    public Guid? ExchangeId { get; set; }
    public string SymbolIds { get; set; } = "[]";
    public string Timeframe { get; set; } = "15m";
}
