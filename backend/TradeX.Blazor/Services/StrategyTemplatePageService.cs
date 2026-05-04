using System.Security.Claims;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Blazor.Services;

public sealed class StrategyTemplatePageService(
    IStrategyRepository strategyRepo) : IStrategyTemplatePageService
{
    public async Task<IReadOnlyList<Strategy>> GetAllAsync(CancellationToken ct = default)
    {
        return await strategyRepo.GetAllAsync(ct);
    }

    public async Task<Strategy> CreateAsync(string name, string entryConditionJson, string exitConditionJson, string executionRuleJson, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("策略名称不能为空");

        var strategy = new Strategy
        {
            Name = name,
            EntryConditionJson = string.IsNullOrWhiteSpace(entryConditionJson) ? "{}" : entryConditionJson,
            ExitConditionJson = string.IsNullOrWhiteSpace(exitConditionJson) ? "{}" : exitConditionJson,
            ExecutionRuleJson = string.IsNullOrWhiteSpace(executionRuleJson) ? "{}" : executionRuleJson
        };

        await strategyRepo.AddAsync(strategy, ct);
        return strategy;
    }

    public async Task UpdateAsync(Guid id, string name, string entryConditionJson, string exitConditionJson, string executionRuleJson, CancellationToken ct = default)
    {
        var strategy = await strategyRepo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("策略不存在");

        if (!string.IsNullOrWhiteSpace(name))
            strategy.Name = name;

        if (entryConditionJson is not null)
            strategy.EntryConditionJson = string.IsNullOrWhiteSpace(entryConditionJson) ? "{}" : entryConditionJson;

        if (exitConditionJson is not null)
            strategy.ExitConditionJson = string.IsNullOrWhiteSpace(exitConditionJson) ? "{}" : exitConditionJson;

        if (executionRuleJson is not null)
            strategy.ExecutionRuleJson = string.IsNullOrWhiteSpace(executionRuleJson) ? "{}" : executionRuleJson;

        await strategyRepo.UpdateAsync(strategy, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var strategy = await strategyRepo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("策略不存在");

        await strategyRepo.DeleteAsync(strategy, ct);
    }
}
