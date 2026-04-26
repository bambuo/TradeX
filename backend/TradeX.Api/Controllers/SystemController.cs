using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Core.Interfaces;
using TradeX.Trading;

namespace TradeX.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/system")]
public class SystemController(
    ITradingEventBus eventBus,
    IExchangeRepository accountRepo,
    IExchangeClientFactory clientFactory,
    IEncryptionService encryptionService,
    ILogger<SystemController> logger) : ControllerBase
{
    [HttpPost("emergency-stop")]
    public async Task<IActionResult> EmergencyStop(CancellationToken ct)
    {
        logger.LogWarning("系统紧急停止触发, UserId={UserId}", User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);

        var disabledCount = 0;
        var cancelledOrders = 0;

        try
        {
            var accounts = await accountRepo.GetAllEnabledAsync(ct);
            foreach (var account in accounts)
            {
                try
                {
                    var apiKey = encryptionService.Decrypt(account.ApiKeyEncrypted);
                    var secretKey = encryptionService.Decrypt(account.SecretKeyEncrypted);
                    var client = clientFactory.CreateClient(account.Type, apiKey, secretKey);

                    account.Status = Core.Models.ExchangeStatus.Disabled;
                    await accountRepo.UpdateAsync(account, ct);

                    disabledCount++;

                    if (account.TraderId is not null)
                        await eventBus.RiskAlertAsync(account.TraderId.Value, "Critical", "EmergencyStop", null,
                            $"系统紧急停止: 交易所 {account.Name} 已禁用", ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "紧急停止: 禁用交易所失败, ExchangeId={ExchangeId}", account.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "紧急停止执行异常");
            return StatusCode(500, new { code = "EMERGENCY_STOP_FAILED", message = "紧急停止执行异常" });
        }

        return Ok(new
        {
            success = true,
            disabledExchanges = disabledCount,
            cancelledOrders,
            message = $"紧急停止完成: 已禁用 {disabledCount} 个交易所"
        });
    }
}
