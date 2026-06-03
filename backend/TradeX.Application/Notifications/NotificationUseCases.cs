using System.Text.Json;
using TradeX.Application.Common;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Application.Notifications;

public sealed record GetNotificationChannelsQuery;

public sealed record NotificationChannelDto(
    Guid Id,
    string Name,
    string Type,
    string Status,
    bool IsDefault,
    DateTime? LastTestedAt);

public sealed class GetChannelsUseCase(INotificationChannelRepository channelRepo)
    : IUseCase<GetNotificationChannelsQuery, Result<List<NotificationChannelDto>>>
{
    public async Task<Result<List<NotificationChannelDto>>> ExecuteAsync(
        GetNotificationChannelsQuery query, CancellationToken ct = default)
    {
        var channels = await channelRepo.GetAllAsync(ct);
        var dtos = channels.Select(c => new NotificationChannelDto(
            c.Id, c.Name, c.Type.ToString(),
            c.Status.ToString(), c.IsDefault, c.LastTestedAt)).ToList();

        return Result<List<NotificationChannelDto>>.Ok(dtos);
    }
}

public sealed record CreateNotificationChannelCommand(
    string Name,
    string Type,
    Dictionary<string, string> Config,
    bool IsDefault = false);

public sealed class CreateChannelUseCase(
    INotificationChannelRepository channelRepo,
    IEncryptionService encryption)
    : IUseCase<CreateNotificationChannelCommand, Result<NotificationChannelDto>>
{
    public async Task<Result<NotificationChannelDto>> ExecuteAsync(
        CreateNotificationChannelCommand cmd, CancellationToken ct = default)
    {
        var configJson = JsonSerializer.Serialize(cmd.Config);
        var channel = NotificationChannel.Create(
            Enum.Parse<NotificationChannelType>(cmd.Type, true),
            cmd.Name,
            encryption.Encrypt(configJson));
        channel.IsDefault = cmd.IsDefault;

        await channelRepo.AddAsync(channel, ct);

        return Result<NotificationChannelDto>.Ok(new NotificationChannelDto(
            channel.Id, channel.Name, channel.Type.ToString(),
            channel.Status.ToString(), channel.IsDefault, channel.LastTestedAt));
    }
}

public sealed record UpdateChannelStatusCommand(Guid Id, bool Enabled);

public sealed class UpdateChannelStatusUseCase(INotificationChannelRepository channelRepo)
    : IUseCase<UpdateChannelStatusCommand, Result>
{
    public async Task<Result> ExecuteAsync(UpdateChannelStatusCommand cmd, CancellationToken ct = default)
    {
        var channel = await channelRepo.GetByIdAsync(cmd.Id, ct);
        if (channel is null)
            return Result.NotFound("通知渠道不存在");

        if (cmd.Enabled)
            channel.Enable();
        else
            channel.Disable();

        await channelRepo.UpdateAsync(channel, ct);
        return Result.Ok();
    }
}

public sealed record TestNotificationChannelCommand(Guid Id);

public sealed class TestChannelUseCase(
    INotificationChannelRepository channelRepo,
    INotificationService notificationService)
    : IUseCase<TestNotificationChannelCommand, Result>
{
    public async Task<Result> ExecuteAsync(TestNotificationChannelCommand cmd, CancellationToken ct = default)
    {
        var channel = await channelRepo.GetByIdAsync(cmd.Id, ct);
        if (channel is null)
            return Result.NotFound("通知渠道不存在");

        try
        {
            await notificationService.SendTestAsync(cmd.Id, ct);
            channel.RecordTestResult();
            await channelRepo.UpdateAsync(channel, ct);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.BadRequest(ex.Message);
        }
    }
}
