namespace TradeX.Core.Interfaces;

/// <summary>
/// 标记实体启用乐观并发控制。EF Core 在 UPDATE 时会把 Version 列加入 WHERE 子句，
/// 若并发修改导致行版本不匹配则抛 DbUpdateConcurrencyException。
/// 版本号由 <c>VersionInterceptor</c> 在保存前自动重新生成。
/// </summary>
public interface IVersioned
{
    Guid Version { get; set; }
}
