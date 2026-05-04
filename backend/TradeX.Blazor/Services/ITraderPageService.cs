using System.Security.Claims;
using TradeX.Core.Attributes;
using TradeX.Core.Enums;

namespace TradeX.Blazor.Services;

public interface ITraderPageService
{
    Task<IReadOnlyList<TraderItem>> GetAllAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<TraderStatsView> GetStatsAsync(Guid id, CancellationToken ct = default);

    [AuditAction("POST", "交易员", "新建交易员")]
    Task<TraderItem> CreateAsync(ClaimsPrincipal user, TraderFormModel form, CancellationToken ct = default);

    [AuditAction("PUT", "交易员", "编辑交易员")]
    Task UpdateAsync(Guid id, TraderFormModel form, CancellationToken ct = default);

    [AuditAction("PUT", "启用/禁用策略", "切换状态")]
    Task ToggleAsync(Guid id, bool enable, CancellationToken ct = default);
}
