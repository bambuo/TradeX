using TradeX.Application.Common;
using TradeX.Core.Interfaces;

namespace TradeX.Application.Strategies;

public sealed record StrategyDto(
    Guid Id,
    string Name,
    int Version,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? ChainsJson = null);

public sealed record GetStrategiesQuery;

/// <summary>获取全部策略用例。</summary>
public sealed class GetStrategiesUseCase(
    IStrategyRepository strategyRepo) : IUseCase<GetStrategiesQuery, Result<List<StrategyDto>>>
{
    public async Task<Result<List<StrategyDto>>> ExecuteAsync(GetStrategiesQuery query, CancellationToken ct = default)
    {
        var strategies = await strategyRepo.GetAllAsync(ct);
        var dtos = strategies.Select(MapToDto).ToList();
        return Result<List<StrategyDto>>.Ok(dtos);
    }

    private static StrategyDto MapToDto(Core.Models.Strategy s) => new(
        s.Id, s.Name,
        s.Version, s.CreatedAt, s.UpdatedAt,
        s.Chains.ValueKind == global::System.Text.Json.JsonValueKind.Array ? global::System.Text.Json.JsonSerializer.Serialize(s.Chains) : null);
}

public sealed record GetStrategyByIdQuery(Guid Id);

/// <summary>获取单个策略详情用例。</summary>
public sealed class GetStrategyByIdUseCase(
    IStrategyRepository strategyRepo) : IUseCase<GetStrategyByIdQuery, Result<StrategyDto>>
{
    public async Task<Result<StrategyDto>> ExecuteAsync(GetStrategyByIdQuery query, CancellationToken ct = default)
    {
        var strategy = await strategyRepo.GetByIdAsync(query.Id, ct);
        if (strategy is null)
            return Result<StrategyDto>.NotFound("策略不存在");

        return Result<StrategyDto>.Ok(new StrategyDto(
            strategy.Id, strategy.Name,
            strategy.Version, strategy.CreatedAt, strategy.UpdatedAt));
    }
}

public sealed record CreateStrategyCommand(string Name, string? ChainsJson = null);

/// <summary>创建策略用例。</summary>
public sealed class CreateStrategyUseCase(
    IStrategyRepository strategyRepo) : IUseCase<CreateStrategyCommand, Result<StrategyDto>>
{
    public async Task<Result<StrategyDto>> ExecuteAsync(CreateStrategyCommand cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Name))
            return Result<StrategyDto>.BadRequest("策略名称不能为空");

        var strategy = string.IsNullOrWhiteSpace(cmd.ChainsJson)
            ? Core.Models.Strategy.Create(cmd.Name, Guid.Empty)
            : Core.Models.Strategy.CreateRuleChain(cmd.Name, global::System.Text.Json.JsonDocument.Parse(cmd.ChainsJson).RootElement, Guid.Empty);

        await strategyRepo.AddAsync(strategy, ct);

        return Result<StrategyDto>.Ok(new StrategyDto(
            strategy.Id, strategy.Name,
            strategy.Version, strategy.CreatedAt, strategy.UpdatedAt));
    }
}

public sealed record UpdateStrategyCommand(Guid Id, string? Name = null, string? ChainsJson = null);

/// <summary>更新策略用例。</summary>
public sealed class UpdateStrategyUseCase(
    IStrategyRepository strategyRepo) : IUseCase<UpdateStrategyCommand, Result<StrategyDto>>
{
    public async Task<Result<StrategyDto>> ExecuteAsync(UpdateStrategyCommand cmd, CancellationToken ct = default)
    {
        var strategy = await strategyRepo.GetByIdAsync(cmd.Id, ct);
        if (strategy is null)
            return Result<StrategyDto>.NotFound("策略不存在");

        if (cmd.Name is not null)
            strategy.Name = cmd.Name;

        if (cmd.ChainsJson is not null)
            strategy.Chains = global::System.Text.Json.JsonDocument.Parse(cmd.ChainsJson).RootElement;

        strategy.Version++;
        await strategyRepo.UpdateAsync(strategy, ct);

        return Result<StrategyDto>.Ok(new StrategyDto(
            strategy.Id, strategy.Name,
            strategy.Version, strategy.CreatedAt, strategy.UpdatedAt));
    }
}

public sealed record DeleteStrategyCommand(Guid Id);

/// <summary>删除策略用例。</summary>
public sealed class DeleteStrategyUseCase(
    IStrategyRepository strategyRepo) : IUseCase<DeleteStrategyCommand, Result>
{
    public async Task<Result> ExecuteAsync(DeleteStrategyCommand cmd, CancellationToken ct = default)
    {
        var strategy = await strategyRepo.GetByIdAsync(cmd.Id, ct);
        if (strategy is null)
            return Result.NotFound("策略不存在");

        await strategyRepo.DeleteAsync(strategy, ct);
        return Result.NoContent();
    }
}
