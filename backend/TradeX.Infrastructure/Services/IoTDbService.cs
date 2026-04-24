using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradeX.Core.Interfaces;

namespace TradeX.Infrastructure.Services;

public class IoTDbService(
    HttpClient http,
    ILogger<IoTDbService> logger) : IIoTDbService
{
    private const string StorageGroup = "root.tradex";

    public async Task WriteKlinesAsync(string exchange, string symbol, string interval, IReadOnlyList<Candle> candles, CancellationToken ct = default)
    {
        if (candles.Count == 0) return;

        try
        {
            var points = new List<object>();
            foreach (var c in candles)
            {
                var timestamp = new DateTimeOffset(c.Timestamp).ToUnixTimeMilliseconds();
                var path = $"{StorageGroup}.{exchange}.{symbol}.{interval}";
                points.Add(new
                {
                    timestamp,
                    measurements = new[]
                    {
                        new { name = "open", dataType = "DOUBLE", value = c.Open },
                        new { name = "high", dataType = "DOUBLE", value = c.High },
                        new { name = "low", dataType = "DOUBLE", value = c.Low },
                        new { name = "close", dataType = "DOUBLE", value = c.Close },
                        new { name = "volume", dataType = "DOUBLE", value = c.Volume }
                    }
                });
            }

            var payload = new
            {
                timestamps = points.Select(p => ((dynamic)p).timestamp).ToArray(),
                measurements = new[] { "open", "high", "low", "close", "volume" },
                dataTypes = new[] { "DOUBLE", "DOUBLE", "DOUBLE", "DOUBLE", "DOUBLE" },
                values = candles.Select(c => new[] { c.Open, c.High, c.Low, c.Close, c.Volume }).ToArray()
            };

            var seriesPath = $"{StorageGroup}.{exchange}.{symbol}.{interval}";
            var body = JsonSerializer.Serialize(new
            {
                paths = new[] { seriesPath },
                payload
            });

            var resp = await http.PostAsync("/api/v1/insert", new StringContent(body, Encoding.UTF8, "application/json"), ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("IoTDB 写入失败: {Status}, Path={Path}", resp.StatusCode, seriesPath);
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
            var seriesPath = $"{StorageGroup}.{exchange}.{symbol}.{interval}";
            var startMs = new DateTimeOffset(start).ToUnixTimeMilliseconds();
            var endMs = new DateTimeOffset(end).ToUnixTimeMilliseconds();

            var sql = $"SELECT open, high, low, close, volume FROM {seriesPath} WHERE time >= {startMs} AND time <= {endMs} ORDER BY time ASC";
            var resp = await http.PostAsJsonAsync("/api/v1/query", new { sql }, ct);
            if (!resp.IsSuccessStatusCode) return [];

            var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(ct);
            if (doc is null) return [];

            var expressions = doc.RootElement.GetProperty("expressions").EnumerateArray().Select(e => e.GetString()).ToArray();
            var timestamps = doc.RootElement.GetProperty("timestamps").EnumerateArray().Select(t => t.GetInt64()).ToArray();
            var values = doc.RootElement.GetProperty("values").EnumerateArray().ToArray();

            var openIdx = Array.IndexOf(expressions, "open");
            var highIdx = Array.IndexOf(expressions, "high");
            var lowIdx = Array.IndexOf(expressions, "low");
            var closeIdx = Array.IndexOf(expressions, "close");
            var volumeIdx = Array.IndexOf(expressions, "volume");

            var result = new List<Candle>();
            for (var i = 0; i < timestamps.Length; i++)
            {
                var getVal = (int idx) =>
                {
                    if (idx < 0) return 0m;
                    var arr = values[idx].EnumerateArray().ToArray();
                    return i < arr.Length && arr[i].ValueKind != JsonValueKind.Null
                        ? decimal.Parse(arr[i].GetString()!, CultureInfo.InvariantCulture)
                        : 0m;
                };

                result.Add(new Candle(
                    DateTimeOffset.FromUnixTimeMilliseconds(timestamps[i]).UtcDateTime,
                    getVal(openIdx), getVal(highIdx), getVal(lowIdx),
                    getVal(closeIdx), getVal(volumeIdx)));
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
            var resp = await http.GetAsync("/api/v1/health", ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
