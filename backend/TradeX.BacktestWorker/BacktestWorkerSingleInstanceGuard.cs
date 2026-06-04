using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradeX.Infrastructure.Data;

namespace TradeX.BacktestWorker;

public sealed class BacktestWorkerSingleInstanceGuard(
    IServiceScopeFactory scopeFactory,
    ILogger<BacktestWorkerSingleInstanceGuard> logger) : IHostedService, IAsyncDisposable
{
    // 不同的锁 ID，与 TradeX.Worker 的 WorkerSingleInstanceGuard 不冲突
    private const long LockId = 0x4241434B54455354; // "BACKTEST" 的 ASCII 哈希
    private AsyncServiceScope? _scope;
    private IDbConnection? _connection;
    private bool _lockAcquired;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _scope = scopeFactory.CreateAsyncScope();
        var db = _scope.Value.ServiceProvider.GetRequiredService<TradeXDbContext>();
        _connection = db.Database.GetDbConnection();

        if (_connection.State != ConnectionState.Open)
            await ((System.Data.Common.DbConnection)_connection).OpenAsync(cancellationToken);

        var result = await ExecuteScalarAsync("SELECT pg_try_advisory_lock(@id)", cancellationToken);
        _lockAcquired = result is bool b && b;

        if (!_lockAcquired)
        {
            logger.LogCritical("回测 Worker 单实例锁获取失败，已有 BacktestWorker 实例正在运行。LockId={LockId}", LockId);
            throw new InvalidOperationException("已有 TradeX.BacktestWorker 实例正在运行，拒绝启动第二个。");
        }

        logger.LogInformation("回测 Worker 单实例锁获取成功。LockId={LockId}", LockId);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_lockAcquired && _connection?.State == ConnectionState.Open)
        {
            await ExecuteScalarAsync("SELECT pg_advisory_unlock(@id)", cancellationToken);
            logger.LogInformation("回测 Worker 单实例锁已释放。LockId={LockId}", LockId);
        }

        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_scope is { } scope)
        {
            await scope.DisposeAsync();
            _scope = null;
            _connection = null;
        }
    }

    private async Task<object?> ExecuteScalarAsync(string sql, CancellationToken ct)
    {
        if (_connection is not System.Data.Common.DbConnection connection)
            throw new InvalidOperationException("数据库连接不支持异步命令。");

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var id = command.CreateParameter();
        id.ParameterName = "@id";
        id.Value = LockId;
        command.Parameters.Add(id);

        return await command.ExecuteScalarAsync(ct);
    }
}
