using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Application.Common;
using TradeX.Application.Notifications;

namespace TradeX.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications/channels")]
public class NotificationChannelsController(
    IUseCase<GetNotificationChannelsQuery, Result<List<NotificationChannelDto>>> getChannelsUseCase,
    IUseCase<CreateNotificationChannelCommand, Result<NotificationChannelDto>> createChannelUseCase,
    IUseCase<UpdateChannelStatusCommand, Result> updateChannelStatusUseCase,
    IUseCase<TestNotificationChannelCommand, Result> testChannelUseCase) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await getChannelsUseCase.ExecuteAsync(new GetNotificationChannelsQuery(), ct);
        return Ok(new { data = result.Data });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateChannelRequest request, CancellationToken ct)
    {
        var result = await createChannelUseCase.ExecuteAsync(
            new CreateNotificationChannelCommand(request.Name, request.Type, request.Config, request.IsDefault), ct);
        if (!result.Success)
            return this.BadRequest(result.Error!);

        return CreatedAtAction(nameof(GetAll), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request, CancellationToken ct)
    {
        var result = await updateChannelStatusUseCase.ExecuteAsync(
            new UpdateChannelStatusCommand(id, request.Enabled), ct);
        if (!result.Success)
            return this.BadRequest(result.Error!);

        return Ok(new { message = result.Success ? "状态已更新" : result.Error });
    }

    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> Test(Guid id, CancellationToken ct)
    {
        var result = await testChannelUseCase.ExecuteAsync(new TestNotificationChannelCommand(id), ct);
        if (!result.Success)
            return this.BadRequest(result.Error!);

        return Ok(new { success = true, message = "测试消息已发送" });
    }

    public record CreateChannelRequest(string Name, string Type, Dictionary<string, string> Config, bool IsDefault = false);
    public record UpdateStatusRequest(bool Enabled);
}
