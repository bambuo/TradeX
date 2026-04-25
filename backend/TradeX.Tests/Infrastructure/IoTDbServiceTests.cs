using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TradeX.Infrastructure.Services;
using TradeX.Infrastructure.Settings;

namespace TradeX.Tests.Infrastructure;

public class IoTDbServiceTests
{
    private static IoTDbService CreateService(string host, int port)
    {
        var options = Options.Create(new IoTDbOptions { Host = host, Port = port });
        var logger = Substitute.For<ILogger<IoTDbService>>();
        return new IoTDbService(options, logger);
    }

    [Fact]
    public async Task WriteKlinesAsync_EmptyCandles_NoOp()
    {
        await using var service = CreateService("localhost", 6667);
        var ex = await Record.ExceptionAsync(() =>
            service.WriteKlinesAsync("Binance", "BTCUSDT", "15m", []));
        Assert.Null(ex);
    }

    [Fact]
    public async Task GetKlinesAsync_WithCorrectPort_ReturnsData()
    {
        await using var service = CreateService("localhost", 6667);
        var result = await service.GetKlinesAsync("Binance", "AXSUSDT", "15m",
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);
        Assert.NotNull(result);
    }
}
