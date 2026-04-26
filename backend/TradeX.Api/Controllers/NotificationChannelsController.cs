using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications/channels")]
public class NotificationChannelsController(
    INotificationChannelRepository channelRepo,
    IEncryptionService encryption,
    INotificationService notificationService,
    ILogger<NotificationChannelsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var channels = await channelRepo.GetAllAsync(ct);
        return Ok(new
        {
            data = channels.Select(c => new
            {
                c.Id, c.Name, type = c.Type.ToString(),
                status = c.Status.ToString(), c.IsDefault,
                c.LastTestedAt, c.CreatedAt
            })
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateChannelRequest request, CancellationToken ct)
    {
        var configJson = System.Text.Json.JsonSerializer.Serialize(request.Config);
        var channel = new NotificationChannel
        {
            Name = request.Name,
            Type = Enum.Parse<NotificationChannelType>(request.Type, true),
            ConfigEncrypted = encryption.Encrypt(configJson),
            IsDefault = request.IsDefault
        };

        await channelRepo.AddAsync(channel, ct);
        return CreatedAtAction(nameof(GetAll), new { id = channel.Id }, new
        {
            channel.Id, channel.Name, type = channel.Type.ToString(),
            status = channel.Status.ToString(), channel.IsDefault,
            channel.CreatedAt
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var channel = await channelRepo.GetByIdAsync(id, ct);
        if (channel is null) return NotFound(new { code = "NOTIFICATION_NOT_FOUND", message = "通知渠道不存在" });

        await channelRepo.DeleteAsync(channel, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> Test(Guid id, CancellationToken ct)
    {
        try
        {
            await notificationService.SendTestAsync(id, ct);
            return Ok(new { success = true, message = "测试消息已发送" });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "通知测试失败, ChannelId={ChannelId}", id);
            return BadRequest(new { code = "NOTIFICATION_TEST_FAILED", message = ex.Message });
        }
    }

    public record CreateChannelRequest(string Name, string Type, Dictionary<string, string> Config, bool IsDefault = false);
}
