using System.Text.Json;
using TradeX.Application.Common;
using TradeX.Core.Interfaces;

namespace TradeX.Application.StrategyBindings;

public sealed record BindingDto(
    Guid Id,
    string Name,
    Guid StrategyId,
    string Pairs,
    string Timeframe,
    string Status,
    DateTime CreatedAt);

public sealed record GetBindingsQuery(Guid TraderId, Guid CurrentUserId);

/// <summary>获取策略绑定列表用例。</summary>
public sealed class GetBindingsUseCase(
    ITraderRepository traderRepo,
    IStrategyBindingRepository bindingRepo) : IUseCase<GetBindingsQuery, Result<List<BindingDto>>>
{
    public async Task<Result<List<BindingDto>>> ExecuteAsync(GetBindingsQuery query, CancellationToken ct = default)
    {
        var trader = await traderRepo.GetByIdAsync(query.TraderId, ct);
        if (trader is null || trader.UserId != query.CurrentUserId)
            return Result<List<BindingDto>>.NotFound("交易员不存在");

        var bindings = await bindingRepo.GetByTraderIdAsync(query.TraderId, ct);
        var dtos = bindings.Select(MapToDto).ToList();
        return Result<List<BindingDto>>.Ok(dtos);
    }

    private static BindingDto MapToDto(Core.Models.StrategyBinding b) => new(
        b.Id, b.Name, b.StrategyId, b.Pairs, b.Timeframe,
        b.Status.ToString(), b.CreatedAt);
}

public sealed record GetBindingByIdQuery(Guid Id, Guid TraderId, Guid CurrentUserId);

/// <summary>获取单个策略绑定详情用例。</summary>
public sealed class GetBindingByIdUseCase(
    ITraderRepository traderRepo,
    IStrategyBindingRepository bindingRepo) : IUseCase<GetBindingByIdQuery, Result<BindingDto>>
{
    public async Task<Result<BindingDto>> ExecuteAsync(GetBindingByIdQuery query, CancellationToken ct = default)
    {
        var trader = await traderRepo.GetByIdAsync(query.TraderId, ct);
        if (trader is null || trader.UserId != query.CurrentUserId)
            return Result<BindingDto>.NotFound("交易员不存在");

        var binding = await bindingRepo.GetByIdAsync(query.Id, ct);
        if (binding is null || binding.TraderId != query.TraderId)
            return Result<BindingDto>.NotFound("绑定策略不存在");

        return Result<BindingDto>.Ok(new BindingDto(
            binding.Id, binding.Name, binding.StrategyId, binding.Pairs,
            binding.Timeframe, binding.Status.ToString(), binding.CreatedAt));
    }
}

public sealed record CreateBindingCommand(
    Guid TraderId, Guid CurrentUserId, Guid StrategyId, Guid ExchangeId,
    string Pairs, string Timeframe, string Name);

/// <summary>创建策略绑定用例。</summary>
public sealed class CreateBindingUseCase(
    ITraderRepository traderRepo,
    IStrategyBindingRepository bindingRepo) : IUseCase<CreateBindingCommand, Result<BindingDto>>
{
    public async Task<Result<BindingDto>> ExecuteAsync(CreateBindingCommand cmd, CancellationToken ct = default)
    {
        var trader = await traderRepo.GetByIdAsync(cmd.TraderId, ct);
        if (trader is null || trader.UserId != cmd.CurrentUserId)
            return Result<BindingDto>.NotFound("交易员不存在");

        var binding = Core.Models.StrategyBinding.Create(
            cmd.StrategyId, cmd.Name, cmd.TraderId,
            cmd.ExchangeId, cmd.Pairs, cmd.Timeframe,
            Core.Enums.MarketType.Spot, cmd.CurrentUserId);

        await bindingRepo.AddAsync(binding, ct);

        return Result<BindingDto>.Created(new BindingDto(
            binding.Id, binding.Name, binding.StrategyId, binding.Pairs,
            binding.Timeframe, binding.Status.ToString(), binding.CreatedAt));
    }
}

public sealed record UpdateBindingCommand(
    Guid Id, Guid TraderId, Guid CurrentUserId,
    string? Pairs, string? Timeframe, string? Name);

/// <summary>更新策略绑定用例。</summary>
public sealed class UpdateBindingUseCase(
    ITraderRepository traderRepo,
    IStrategyBindingRepository bindingRepo) : IUseCase<UpdateBindingCommand, Result<BindingDto>>
{
    public async Task<Result<BindingDto>> ExecuteAsync(UpdateBindingCommand cmd, CancellationToken ct = default)
    {
        var trader = await traderRepo.GetByIdAsync(cmd.TraderId, ct);
        if (trader is null || trader.UserId != cmd.CurrentUserId)
            return Result<BindingDto>.NotFound("交易员不存在");

        var binding = await bindingRepo.GetByIdAsync(cmd.Id, ct);
        if (binding is null || binding.TraderId != cmd.TraderId)
            return Result<BindingDto>.NotFound("绑定策略不存在");

        if (binding.Status == Core.Enums.BindingStatus.Active)
            return Result<BindingDto>.BadRequest("活跃策略不可编辑，请先禁用");

        if (cmd.Pairs is not null)
            binding.Pairs = cmd.Pairs;
        if (cmd.Timeframe is not null)
            binding.Timeframe = cmd.Timeframe;
        if (cmd.Name is not null)
            binding.Name = cmd.Name;

        binding.UpdatedAt = DateTime.UtcNow;
        await bindingRepo.UpdateAsync(binding, ct);

        return Result<BindingDto>.Ok(new BindingDto(
            binding.Id, binding.Name, binding.StrategyId, binding.Pairs,
            binding.Timeframe, binding.Status.ToString(), binding.CreatedAt));
    }
}

public sealed record DeleteBindingCommand(Guid Id, Guid TraderId, Guid CurrentUserId);

/// <summary>删除策略绑定用例。</summary>
public sealed class DeleteBindingUseCase(
    ITraderRepository traderRepo,
    IStrategyBindingRepository bindingRepo) : IUseCase<DeleteBindingCommand, Result>
{
    public async Task<Result> ExecuteAsync(DeleteBindingCommand cmd, CancellationToken ct = default)
    {
        var trader = await traderRepo.GetByIdAsync(cmd.TraderId, ct);
        if (trader is null || trader.UserId != cmd.CurrentUserId)
            return Result.NotFound("交易员不存在");

        var binding = await bindingRepo.GetByIdAsync(cmd.Id, ct);
        if (binding is null || binding.TraderId != cmd.TraderId)
            return Result.NotFound("绑定策略不存在");

        if (binding.Status == Core.Enums.BindingStatus.Active)
            return Result.BadRequest("活跃策略不可删除，请先禁用");

        await bindingRepo.DeleteAsync(binding, ct);
        return Result.NoContent();
    }
}

public sealed record ActivateBindingCommand(Guid Id, Guid TraderId, Guid CurrentUserId);

/// <summary>激活策略绑定用例。</summary>
public sealed class ActivateBindingUseCase(
    ITraderRepository traderRepo,
    IStrategyBindingRepository bindingRepo) : IUseCase<ActivateBindingCommand, Result<BindingDto>>
{
    public async Task<Result<BindingDto>> ExecuteAsync(ActivateBindingCommand cmd, CancellationToken ct = default)
    {
        var trader = await traderRepo.GetByIdAsync(cmd.TraderId, ct);
        if (trader is null || trader.UserId != cmd.CurrentUserId)
            return Result<BindingDto>.NotFound("交易员不存在");

        var binding = await bindingRepo.GetByIdAsync(cmd.Id, ct);
        if (binding is null || binding.TraderId != cmd.TraderId)
            return Result<BindingDto>.NotFound("绑定策略不存在");

        var pairs = ParsePairs(binding.Pairs);
        foreach (var pair in pairs)
        {
            var hasConflict = await bindingRepo.ExistsActiveAsync(cmd.TraderId, binding.ExchangeId, pair, cmd.Id, ct);
            if (hasConflict)
                return Result<BindingDto>.Conflict($"交易对 {pair} 上已有活跃策略");
        }

        binding.Activate();
        await bindingRepo.UpdateAsync(binding, ct);

        return Result<BindingDto>.Ok(new BindingDto(
            binding.Id, binding.Name, binding.StrategyId, binding.Pairs,
            binding.Timeframe, binding.Status.ToString(), binding.CreatedAt));
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
}

public sealed record DeactivateBindingCommand(Guid Id, Guid TraderId, Guid CurrentUserId);

/// <summary>禁用策略绑定用例。</summary>
public sealed class DeactivateBindingUseCase(
    ITraderRepository traderRepo,
    IStrategyBindingRepository bindingRepo) : IUseCase<DeactivateBindingCommand, Result<BindingDto>>
{
    public async Task<Result<BindingDto>> ExecuteAsync(DeactivateBindingCommand cmd, CancellationToken ct = default)
    {
        var trader = await traderRepo.GetByIdAsync(cmd.TraderId, ct);
        if (trader is null || trader.UserId != cmd.CurrentUserId)
            return Result<BindingDto>.NotFound("交易员不存在");

        var binding = await bindingRepo.GetByIdAsync(cmd.Id, ct);
        if (binding is null || binding.TraderId != cmd.TraderId)
            return Result<BindingDto>.NotFound("绑定策略不存在");

        binding.Deactivate();
        await bindingRepo.UpdateAsync(binding, ct);

        return Result<BindingDto>.Ok(new BindingDto(
            binding.Id, binding.Name, binding.StrategyId, binding.Pairs,
            binding.Timeframe, binding.Status.ToString(), binding.CreatedAt));
    }
}
