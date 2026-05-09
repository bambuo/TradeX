using System.Security.Claims;
using TradeX.Core.Attributes;
using TradeX.Core.Models;

namespace TradeX.Blazor.Services;

public interface IStrategyPageService
{
    Task<IReadOnlyList<BindingItem>> GetAllAsync(ClaimsPrincipal user, Guid traderId, CancellationToken ct = default);
    Task<IReadOnlyList<Strategy>> GetAllStrategiesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ExchangeOption>> GetExchangesAsync(ClaimsPrincipal user, CancellationToken ct = default);

    [AuditAction("POST", "绑定策略", "新建部署")]
    Task CreateAsync(ClaimsPrincipal user, Guid traderId, StrategyBindingFormModel form, CancellationToken ct = default);

    [AuditAction("PUT", "绑定策略", "编辑绑定")]
    Task UpdateAsync(Guid id, StrategyBindingFormModel form, CancellationToken ct = default);

    [AuditAction("PUT", "启用/禁用策略", "切换状态")]
    Task ToggleAsync(Guid id, bool enable, CancellationToken ct = default);

    [AuditAction("DELETE", "绑定策略", "删除绑定")]
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
