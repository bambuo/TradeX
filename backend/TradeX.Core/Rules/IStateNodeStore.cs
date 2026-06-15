namespace TradeX.Core.Rules;

/// <summary>有状态节点的持久化存储接口。</summary>
/// <remarks>
/// 设计原则：
/// 1. 接口定义在 domain/rules，实现在 infra/persistence/
/// 2. 每个 ScopeKey 对应一个策略绑定 + 交易对的独立状态空间
/// 3. 读写分离：Evaluate 阶段只读（ReadState），决策执行阶段写入
/// 4. fail-closed 铁律：ReadState 对非"键不存在"错误必须上抛，绝不返回空状态
/// </remarks>
public interface IStateNodeStore
{
    /// <summary>读取某个 ScopeKey 下某节点的状态快照。</summary>
    Task<NodeState?> ReadStateAsync(string scopeKey, string nodeKind, CancellationToken ct = default);

    /// <summary>写入节点状态（仅在决策实际执行后调用）。</summary>
    Task WriteStateAsync(string scopeKey, string nodeKind, NodeState state, CancellationToken ct = default);

    /// <summary>批量写入（一轮评估中的所有状态变更批量提交）。</summary>
    Task BatchWriteAsync(IReadOnlyList<StateEntry> entries, CancellationToken ct = default);
}

/// <summary>提供执行前 pending 预占能力（R2）。</summary>
public interface IPendingStore
{
    /// <summary>写入指定 scope 的短 TTL 预占。</summary>
    Task WritePendingAsync(string scopeKey, CancellationToken ct = default);

    /// <summary>清除指定 scope 的预占。</summary>
    Task ClearPendingAsync(string scopeKey, CancellationToken ct = default);

    /// <summary>检查指定 scope 是否已有预占。</summary>
    Task<bool> IsPendingAsync(string scopeKey, CancellationToken ct = default);
}
