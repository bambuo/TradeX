using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Core.Interfaces;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController(ISystemConfigRepository configRepo) : ControllerBase
{
    private static readonly string[] ReadOnlyKeys = ["jwt.secret"];

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var settings = await configRepo.GetAllAsync(ct);
        return Ok(new { data = settings.Select(s => new { s.Key, s.Value }) });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateSettingsRequest request, CancellationToken ct)
    {
        foreach (var setting in request.Settings)
        {
            if (ReadOnlyKeys.Contains(setting.Key))
                return BadRequest(new { code = "VALIDATION_ERROR", message = $"key {setting.Key} is read-only" });

            await configRepo.UpsertAsync(setting.Key, setting.Value, ct);
        }

        return Ok();
    }

    public record UpdateSettingItem(string Key, string Value);
    public record UpdateSettingsRequest(List<UpdateSettingItem> Settings);
}
