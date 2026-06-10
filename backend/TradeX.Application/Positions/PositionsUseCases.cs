using TradeX.Application.Common;
using TradeX.Core.Interfaces;

namespace TradeX.Application.Positions;

public sealed record PositionDto(
    Guid Id,
    Guid TraderId,
    string Pair,
    decimal Quantity,
    decimal EntryPrice,
    decimal CurrentPrice,
    decimal UnrealizedPnl,
    decimal RealizedPnl,
    string Status,
    DateTime OpenedAt,
    DateTime? ClosedAt);

public sealed record GetOpenPositionsQuery(Guid TraderId, Guid CurrentUserId);

/// <summary>获取交易员持仓列表用例。</summary>
public sealed class GetOpenPositionsUseCase(
    ITraderRepository traderRepo,
    IPositionRepository positionRepo) : IUseCase<GetOpenPositionsQuery, Result<List<PositionDto>>>
{
    public async Task<Result<List<PositionDto>>> ExecuteAsync(GetOpenPositionsQuery query, CancellationToken ct = default)
    {
        var trader = await traderRepo.GetByIdAsync(query.TraderId, ct);
        if (trader is null || trader.UserId != query.CurrentUserId)
            return Result<List<PositionDto>>.NotFound("交易员不存在");

        var positions = await positionRepo.GetOpenByTraderIdAsync(query.TraderId, ct);
        var dtos = positions.Select(MapToDto).ToList();
        return Result<List<PositionDto>>.Ok(dtos);
    }

    private static PositionDto MapToDto(Core.Models.Position p) => new(
        p.Id, p.TraderId, p.Pair,
        p.Quantity, p.EntryPrice, p.CurrentPrice,
        p.UnrealizedPnl, p.RealizedPnl,
        p.Status.ToString(), p.OpenedAt, p.ClosedAt);
}

public sealed record GetPositionByIdQuery(Guid TraderId, Guid PositionId, Guid CurrentUserId);

/// <summary>获取单个持仓详情用例。</summary>
public sealed class GetPositionByIdUseCase(
    ITraderRepository traderRepo,
    IPositionRepository positionRepo) : IUseCase<GetPositionByIdQuery, Result<PositionDto>>
{
    public async Task<Result<PositionDto>> ExecuteAsync(GetPositionByIdQuery query, CancellationToken ct = default)
    {
        var trader = await traderRepo.GetByIdAsync(query.TraderId, ct);
        if (trader is null || trader.UserId != query.CurrentUserId)
            return Result<PositionDto>.NotFound("交易员不存在");

        var position = await positionRepo.GetByIdAsync(query.PositionId, ct);
        if (position is null || position.TraderId != query.TraderId)
            return Result<PositionDto>.NotFound("持仓不存在");

        return Result<PositionDto>.Ok(new PositionDto(
            position.Id, position.TraderId, position.Pair,
            position.Quantity, position.EntryPrice, position.CurrentPrice,
            position.UnrealizedPnl, position.RealizedPnl,
            position.Status.ToString(), position.OpenedAt, position.ClosedAt));
    }
}
