using System.Security.Claims;
using TradeX.Core.Attributes;
using TradeX.Core.Models;

namespace TradeX.Blazor.Services;

public interface IStrategyPageService
{
    Task<IReadOnlyList<DeploymentItem>> GetAllAsync(ClaimsPrincipal user, Guid traderId, CancellationToken ct = default);
    Task<IReadOnlyList<Strategy>> GetAllStrategiesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ExchangeOption>> GetExchangesAsync(ClaimsPrincipal user, CancellationToken ct = default);

    [AuditAction("POST", "策略部署", "新建部署")]
    Task CreateAsync(ClaimsPrincipal user, Guid traderId, StrategyDeploymentFormModel form, CancellationToken ct = default);

    [AuditAction("PUT", "策略部署", "编辑部署")]
    Task UpdateAsync(Guid id, StrategyDeploymentFormModel form, CancellationToken ct = default);

    [AuditAction("PUT", "启用/禁用策略", "切换状态")]
    Task ToggleAsync(Guid id, bool enable, CancellationToken ct = default);

    [AuditAction("DELETE", "策略部署", "删除部署")]
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
