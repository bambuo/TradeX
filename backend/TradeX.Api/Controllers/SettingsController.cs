using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Application.Common;
using TradeX.Application.Settings;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController(
    IUseCase<GetSettingsQuery, Result<Dictionary<string, string>>> getSettingsUseCase,
    IUseCase<UpdateSettingCommand, Result> updateSettingUseCase) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await getSettingsUseCase.ExecuteAsync(new GetSettingsQuery(), ct);
        return Ok(new { data = result.Data?.Select(kv => new { Key = kv.Key, Value = kv.Value }) });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateSettingsRequest request, CancellationToken ct)
    {
        foreach (var setting in request.Settings)
        {
            var result = await updateSettingUseCase.ExecuteAsync(
                new UpdateSettingCommand(setting.Key, setting.Value), ct);
            if (!result.Success)
                return this.BadRequest(result.Error!);
        }

        return Ok();
    }

    public record UpdateSettingItem(string Key, string Value);
    public record UpdateSettingsRequest(List<UpdateSettingItem> Settings);
}
