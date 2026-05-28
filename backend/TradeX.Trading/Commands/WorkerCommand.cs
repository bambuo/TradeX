namespace TradeX.Trading.Commands;

/// <summary>API → Worker 跨进程命令包络。频道 <see cref="WorkerCommandChannels.Commands"/>。</summary>
public sealed record WorkerCommand(string Type, string ArgsJson);

public static class WorkerCommandTypes
{
    public const string ReconcileNow = "ReconcileNow";
    public const string RefreshSubscriptions = "RefreshSubscriptions";
}

public static class WorkerCommandChannels
{
    public const string Commands = "tradex:cmd";
}

public interface IWorkerCommandPublisher
{
    Task PublishAsync(string type, object? args = null, CancellationToken ct = default);
}

public interface IWorkerCommandHandler
{
    /// <summary>处理器关联的命令类型，对应 <see cref="WorkerCommand.Type"/>。</summary>
    string CommandType { get; }
    Task HandleAsync(string argsJson, CancellationToken ct);
}
