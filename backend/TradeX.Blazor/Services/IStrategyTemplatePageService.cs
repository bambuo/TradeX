using TradeX.Core.Attributes;
using TradeX.Core.Models;

namespace TradeX.Blazor.Services;

public interface IStrategyTemplatePageService
{
    Task<IReadOnlyList<Strategy>> GetAllAsync(CancellationToken ct = default);

    [AuditAction("POST", "策略", "新建策略")]
    Task<Strategy> CreateAsync(string name, string entryConditionJson, string exitConditionJson, string executionRuleJson, CancellationToken ct = default);

    [AuditAction("PUT", "策略", "编辑策略")]
    Task UpdateAsync(Guid id, string name, string entryConditionJson, string exitConditionJson, string executionRuleJson, CancellationToken ct = default);

    [AuditAction("DELETE", "策略", "删除策略")]
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
