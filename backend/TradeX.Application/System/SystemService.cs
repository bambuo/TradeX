using Microsoft.Extensions.Logging;
using TradeX.Application.Common;
using TradeX.Core.Interfaces;

namespace TradeX.Application.System;

/// <summary>
/// 系统服务实现 — 封装紧急停止等涉及多个基础设施服务的操作。
/// </summary>
public sealed class SystemService(
    IExchangeRepository exchangeRepo,
    IExchangeClientFactory clientFactory,
    IEncryptionService encryptionService,
    ILogger<SystemService> logger) : ISystemService
{
    public async Task<EmergencyStopResultDto> EmergencyStopAsync(Guid currentUserId, CancellationToken ct = default)
    {
        var disabledCount = 0;

        try
        {
            var exchanges = await exchangeRepo.GetAllEnabledAsync(ct);
            foreach (var exchange in exchanges)
            {
                try
                {
                    var apiKey = encryptionService.Decrypt(exchange.ApiKeyEncrypted);
                    var secretKey = encryptionService.Decrypt(exchange.SecretKeyEncrypted);
                    var client = clientFactory.CreateClient(exchange.Type, apiKey, secretKey);

                    exchange.Status = Core.Models.ExchangeStatus.Disabled;
                    await exchangeRepo.UpdateAsync(exchange, ct);

                    disabledCount++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "紧急停止: 禁用交易所失败, ExchangeId={ExchangeId}", exchange.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "紧急停止执行异常");
            throw;
        }

        return new EmergencyStopResultDto(
            true, disabledCount, 0,
            $"紧急停止完成: 已禁用 {disabledCount} 个交易所");
    }
}
