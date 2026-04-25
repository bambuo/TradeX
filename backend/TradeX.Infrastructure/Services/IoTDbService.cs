using Apache.IoTDB;
using Apache.IoTDB.DataStructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradeX.Core.Interfaces;
using TradeX.Infrastructure.Settings;

namespace TradeX.Infrastructure.Services;

public class IoTDbService(
    IOptions<IoTDbOptions> options,
    ILogger<IoTDbService> logger) : IIoTDbService, IAsyncDisposable
{
    private SessionPool? _pool;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const string StorageGroup = "root.tradex";

    private string SeriesPath(string exchange, string symbol, string interval)
        => $"{StorageGroup}.{exchange.ToLowerInvariant()}.{symbol.ToLowerInvariant()}.{interval}";

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
