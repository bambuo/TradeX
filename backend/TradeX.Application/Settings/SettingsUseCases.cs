using TradeX.Application.Common;
using TradeX.Core.Interfaces;

namespace TradeX.Application.Settings;

public sealed record GetSettingsQuery;

public sealed class GetSettingsUseCase(ISystemConfigRepository configRepo)
    : IUseCase<GetSettingsQuery, Result<Dictionary<string, string>>>
{
    public async Task<Result<Dictionary<string, string>>> ExecuteAsync(GetSettingsQuery query, CancellationToken ct = default)
    {
        var settings = await configRepo.GetAllAsync(ct);
        var dict = settings.ToDictionary(s => s.Key, s => s.Value);
        return Result<Dictionary<string, string>>.Ok(dict);
    }
}

public sealed record UpdateSettingCommand(string Key, string Value);

public sealed class UpdateSettingUseCase(ISystemConfigRepository configRepo)
    : IUseCase<UpdateSettingCommand, Result>
{
    private static readonly string[] ReadOnlyKeys = ["jwt.secret"];

    public async Task<Result> ExecuteAsync(UpdateSettingCommand cmd, CancellationToken ct = default)
    {
        if (ReadOnlyKeys.Contains(cmd.Key))
            return Result.BadRequest($"key {cmd.Key} is read-only");

        await configRepo.UpsertAsync(cmd.Key, cmd.Value, ct);
        return Result.Ok();
    }
}
