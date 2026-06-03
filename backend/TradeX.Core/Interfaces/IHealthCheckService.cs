namespace TradeX.Core.Interfaces;

/// <summary>健康检查服务接口 — 用于探测外部依赖连通性。</summary>
public interface IHealthCheckService
{
    Task<HealthCheckResult> CheckAsync(CancellationToken ct = default);
}

public sealed record HealthCheckResult(
    string Status,
    string Database,
    DateTime Timestamp);
