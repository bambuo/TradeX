using System.Text.Json;
using Apache.IoTDB;
using Apache.IoTDB.DataStructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Infrastructure.Settings;

namespace TradeX.Infrastructure.Services;

public class IoTDbService(
    IOptions<IoTDbOptions> options,
    ILogger<IoTDbService> logger) : IIoTDbService, IAsyncDisposable
{
    private SessionPool? _pool;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const string StorageGroup = "root.tradex";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private string SeriesPath(string exchange, string symbol, string interval)
        => $"{StorageGroup}.{exchange.ToLowerInvariant()}.{symbol.ToLowerInvariant()}.{interval}";

    private static string AnalysisDevicePath(Guid taskId)
        => $"{StorageGroup}.backtest.{taskId:N}";

    private async Task<SessionPool> GetPoolAsync()
    {
        if (_pool is not null) return _pool;

        await _lock.WaitAsync();
        try
        {
            if (_pool is not null) return _pool;
            _pool = new SessionPool(options.Value.Host, options.Value.Port, 2);
            _pool.Open(false);
            await _pool.SetStorageGroup(StorageGroup);
            return _pool;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task WriteKlinesAsync(string exchange, string symbol, string interval, IReadOnlyList<Candle> candles, CancellationToken ct = default)
    {
        if (candles.Count == 0) return;
        try
        {
            var pool = await GetPoolAsync();
            var deviceId = SeriesPath(exchange, symbol, interval);

            foreach (var c in candles)
            {
                var ts = new DateTimeOffset(c.Timestamp).ToUnixTimeMilliseconds();
                var measures = new List<string> { "open", "high", "low", "close", "volume" };
                var values = new List<object> { (double)c.Open, (double)c.High, (double)c.Low, (double)c.Close, (double)c.Volume };
                await pool.InsertRecordAsync(deviceId, new RowRecord(ts, values, measures));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IoTDB 写入异常: {Exchange}/{Symbol}/{Interval}", exchange, symbol, interval);
        }
    }

    public async Task<Candle[]> GetKlinesAsync(string exchange, string symbol, string interval, DateTime start, DateTime end, CancellationToken ct = default)
    {
        try
        {
            var pool = await GetPoolAsync();
            var path = SeriesPath(exchange, symbol, interval);
            var startMs = new DateTimeOffset(start).ToUnixTimeMilliseconds();
            var endMs = new DateTimeOffset(end).ToUnixTimeMilliseconds();

            var sql = $"SELECT open, high, low, close, volume FROM {path} WHERE time >= {startMs} AND time <= {endMs} ORDER BY time ASC";
            using var dataSet = await pool.ExecuteQueryStatementAsync(sql);

            var result = new List<Candle>();
            while (dataSet.HasNext())
            {
                var row = dataSet.Next();
                result.Add(new Candle(
                    DateTimeOffset.FromUnixTimeMilliseconds(row.Timestamps).UtcDateTime,
                    Convert.ToDecimal(row.Values[0]),
                    Convert.ToDecimal(row.Values[1]),
                    Convert.ToDecimal(row.Values[2]),
                    Convert.ToDecimal(row.Values[3]),
                    Convert.ToDecimal(row.Values[4])
                ));
            }
            return result.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IoTDB 查询异常: {Exchange}/{Symbol}/{Interval}", exchange, symbol, interval);
            return [];
        }
    }

    public async Task WriteBacktestAnalysisAsync(Guid taskId, IReadOnlyList<BacktestCandleAnalysis> analysis, CancellationToken ct = default)
    {
        if (analysis.Count == 0) return;
        try
        {
            var pool = await GetPoolAsync();
            var deviceId = AnalysisDevicePath(taskId);

            foreach (var a in analysis)
            {
                var ts = new DateTimeOffset(a.Timestamp).ToUnixTimeMilliseconds();
                var measures = new List<string>
                {
                    "index", "open", "high", "low", "close", "volume",
                    "indicators_json", "entry_condition", "exit_condition", "in_position", "action",
                    "avg_entry_price", "position_quantity", "position_cost", "position_value",
                    "position_pnl", "position_pnl_percent"
                };
                var values = new List<object>
                {
                    a.Index,
                    (double)a.Open, (double)a.High, (double)a.Low, (double)a.Close, (double)a.Volume,
                    JsonSerializer.Serialize(a.IndicatorValues, JsonOptions),
                    a.EntryConditionResult ?? false, a.ExitConditionResult ?? false, a.InPosition, a.Action,
                    (double?)(a.AvgEntryPrice ?? 0), (double?)(a.PositionQuantity ?? 0),
                    (double?)(a.PositionCost ?? 0), (double?)(a.PositionValue ?? 0),
                    (double?)(a.PositionPnl ?? 0), (double?)(a.PositionPnlPercent ?? 0)
                };
                await pool.InsertRecordAsync(deviceId, new RowRecord(ts, values, measures));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IoTDB 回测分析写入异常: TaskId={TaskId}", taskId);
            throw;
        }
    }

    public async Task<BacktestCandleAnalysis[]> GetBacktestAnalysisPageAsync(Guid taskId, int page, int pageSize, CancellationToken ct = default)
    {
        try
        {
            var pool = await GetPoolAsync();
            var deviceId = AnalysisDevicePath(taskId);
            var offset = (page - 1) * pageSize;
            var columns = "index, open, high, low, close, volume, indicators_json, entry_condition, exit_condition, in_position, action, avg_entry_price, position_quantity, position_cost, position_value, position_pnl, position_pnl_percent";
            var sql = $"SELECT {columns} FROM {deviceId} ORDER BY time ASC LIMIT {pageSize} OFFSET {offset}";
            using var dataSet = await pool.ExecuteQueryStatementAsync(sql);
            return ReadAnalysisRows(dataSet);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IoTDB 回测分析分页查询异常: TaskId={TaskId}, Page={Page}", taskId, page);
            return [];
        }
    }

    public async Task<int> GetBacktestAnalysisCountAsync(Guid taskId, CancellationToken ct = default)
    {
        try
        {
            var pool = await GetPoolAsync();
            var deviceId = AnalysisDevicePath(taskId);
            var sql = $"SELECT COUNT(*) FROM {deviceId}";
            using var dataSet = await pool.ExecuteQueryStatementAsync(sql);
            if (dataSet.HasNext())
            {
                var row = dataSet.Next();
                return Convert.ToInt32(row.Values[0]);
            }
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IoTDB 回测分析计数查询异常: TaskId={TaskId}", taskId);
            return 0;
        }
    }

    public async Task<BacktestCandleAnalysis[]> GetBacktestAnalysisAllAsync(Guid taskId, CancellationToken ct = default)
    {
        try
        {
            var pool = await GetPoolAsync();
            var deviceId = AnalysisDevicePath(taskId);
            var columns = "index, open, high, low, close, volume, indicators_json, entry_condition, exit_condition, in_position, action, avg_entry_price, position_quantity, position_cost, position_value, position_pnl, position_pnl_percent";
            var sql = $"SELECT {columns} FROM {deviceId} ORDER BY time ASC";
            using var dataSet = await pool.ExecuteQueryStatementAsync(sql);
            return ReadAnalysisRows(dataSet);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IoTDB 回测分析全量查询异常: TaskId={TaskId}", taskId);
            return [];
        }
    }

    public async Task DeleteBacktestAnalysisAsync(Guid taskId, CancellationToken ct = default)
    {
        try
        {
            var pool = await GetPoolAsync();
            var deviceId = AnalysisDevicePath(taskId);
            var sql = $"DELETE FROM {deviceId} WHERE time >= 0";
            await pool.ExecuteQueryStatementAsync(sql);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IoTDB 回测分析删除异常: TaskId={TaskId}", taskId);
        }
    }

    private static BacktestCandleAnalysis[] ReadAnalysisRows(Apache.IoTDB.DataStructure.SessionDataSet dataSet)
    {
        var result = new List<BacktestCandleAnalysis>();
        while (dataSet.HasNext())
        {
            var row = dataSet.Next();
            var v = row.Values;
            // Column order: index(0), open(1), high(2), low(3), close(4), volume(5),
            // indicators_json(6), entry_condition(7), exit_condition(8), in_position(9), action(10),
            // avg_entry_price(11), position_quantity(12), position_cost(13), position_value(14),
            // position_pnl(15), position_pnl_percent(16)
            var indicatorsJson = v[6] as string ?? "{}";
            var indicators = JsonSerializer.Deserialize<Dictionary<string, decimal>>(indicatorsJson, JsonOptions) ?? [];

            result.Add(new BacktestCandleAnalysis(
                Convert.ToInt32(v[0]),
                DateTimeOffset.FromUnixTimeMilliseconds(row.Timestamps).UtcDateTime,
                Convert.ToDecimal(v[1]), Convert.ToDecimal(v[2]), Convert.ToDecimal(v[3]),
                Convert.ToDecimal(v[4]), Convert.ToDecimal(v[5]),
                indicators,
                v[7] as bool?, v[8] as bool?,
                v[9] as bool? == true,
                v[10] as string ?? "none",
                v[11] is not null ? Convert.ToDecimal(v[11]) : null,
                v[12] is not null ? Convert.ToDecimal(v[12]) : null,
                v[13] is not null ? Convert.ToDecimal(v[13]) : null,
                v[14] is not null ? Convert.ToDecimal(v[14]) : null,
                v[15] is not null ? Convert.ToDecimal(v[15]) : null,
                v[16] is not null ? Convert.ToDecimal(v[16]) : null
            ));
        }
        return result.ToArray();
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            var pool = await GetPoolAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_pool is not null)
        {
            _pool.Close();
            _pool = null;
        }
    }
}
