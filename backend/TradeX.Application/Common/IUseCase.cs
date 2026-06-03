namespace TradeX.Application.Common;

/// <summary>
/// 命令模式接口 — 封装单个业务用例。
/// </summary>
public interface IUseCase<in TRequest, TResponse>
{
    Task<TResponse> ExecuteAsync(TRequest request, CancellationToken ct = default);
}
