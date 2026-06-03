using TradeX.Application.Common;
using TradeX.Application.Traders.DTOs;
using TradeX.Core.Enums;
using TradeX.Core.ErrorCodes;
using TradeX.Core.Interfaces;

namespace TradeX.Application.Traders;

public sealed record GetTraderByIdQuery(Guid TraderId, Guid CurrentUserId);

/// <summary>获取单个交易员详情用例。</summary>
public sealed class GetTraderByIdUseCase(
    ITraderRepository traderRepo) : IUseCase<GetTraderByIdQuery, Result<TraderDetailDto>>
{
    public async Task<Result<TraderDetailDto>> ExecuteAsync(GetTraderByIdQuery query, CancellationToken ct = default)
    {
        var trader = await traderRepo.GetByIdAsync(query.TraderId, ct);
        if (trader is null || trader.UserId != query.CurrentUserId)
            return Result<TraderDetailDto>.NotFound("交易员不存在");

        return Result<TraderDetailDto>.Ok(new TraderDetailDto(
            trader.Id, trader.Name, trader.Status.ToString(),
            trader.AvatarColor, trader.AvatarUrl, trader.Style,
            trader.CreatedAt, trader.UpdatedAt));
    }
}

public sealed record TraderDetailDto(
    Guid Id, string Name, string Status,
    string? AvatarColor, string? AvatarUrl, string? Style,
    DateTime CreatedAt, DateTime? UpdatedAt);

public sealed record UpdateTraderCommand(
    Guid TraderId, Guid CurrentUserId,
    string? Name = null, TraderStatus? Status = null,
    string? AvatarColor = null, string? Style = null,
    string? AvatarUrl = null);

/// <summary>更新交易员用例。</summary>
public sealed class UpdateTraderUseCase(
    ITraderRepository traderRepo,
    IStrategyBindingRepository bindingRepo) : IUseCase<UpdateTraderCommand, Result<TraderDetailDto>>
{
    public async Task<Result<TraderDetailDto>> ExecuteAsync(UpdateTraderCommand cmd, CancellationToken ct = default)
    {
        var trader = await traderRepo.GetByIdAsync(cmd.TraderId, ct);
        if (trader is null || trader.UserId != cmd.CurrentUserId)
            return Result<TraderDetailDto>.NotFound("交易员不存在");

        if (!string.IsNullOrWhiteSpace(cmd.Name))
        {
            var isUnique = await traderRepo.IsNameUniqueAsync(cmd.CurrentUserId, cmd.Name, cmd.TraderId, ct);
            if (!isUnique)
                return Result<TraderDetailDto>.Conflict("交易员名称已存在");
            trader.Name = cmd.Name;
        }

        if (cmd.Status.HasValue)
        {
            if (cmd.Status.Value == TraderStatus.Disabled && trader.Status == TraderStatus.Active)
            {
                var activeBindings = await bindingRepo.GetByTraderIdAsync(trader.Id, ct);
                foreach (var binding in activeBindings.Where(d => d.Status == BindingStatus.Active))
                {
                    binding.Deactivate();
                    await bindingRepo.UpdateAsync(binding, ct);
                }
            }
            trader.Status = cmd.Status.Value;
        }

        if (cmd.AvatarColor is not null)
            trader.AvatarColor = cmd.AvatarColor;

        if (cmd.Style is not null)
            trader.Style = cmd.Style;

        if (cmd.AvatarUrl is not null)
            trader.AvatarUrl = cmd.AvatarUrl;

        await traderRepo.UpdateAsync(trader, ct);

        return Result<TraderDetailDto>.Ok(new TraderDetailDto(
            trader.Id, trader.Name, trader.Status.ToString(),
            trader.AvatarColor, trader.AvatarUrl, trader.Style,
            trader.CreatedAt, trader.UpdatedAt));
    }
}

public sealed record DeleteTraderCommand(Guid TraderId, Guid CurrentUserId);

/// <summary>删除交易员用例。</summary>
public sealed class DeleteTraderUseCase(
    ITraderRepository traderRepo,
    IStrategyBindingRepository bindingRepo) : IUseCase<DeleteTraderCommand, Result>
{
    public async Task<Result> ExecuteAsync(DeleteTraderCommand cmd, CancellationToken ct = default)
    {
        var trader = await traderRepo.GetByIdAsync(cmd.TraderId, ct);
        if (trader is null || trader.UserId != cmd.CurrentUserId)
            return Result.NotFound("交易员不存在");

        var activeBindings = await bindingRepo.GetByTraderIdAsync(trader.Id, ct);
        if (activeBindings.Any(d => d.Status == BindingStatus.Active))
            return Result.Conflict("交易员存在活跃策略，无法删除，请先禁用所有策略");

        await traderRepo.DeleteAsync(trader, ct);
        return Result.NoContent();
    }
}
