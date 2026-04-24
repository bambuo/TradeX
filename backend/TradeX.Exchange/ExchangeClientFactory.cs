using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Exchange;

public class ExchangeClientFactory : IExchangeClientFactory
{
    public IExchangeClient CreateClient(ExchangeType type, string apiKey, string secretKey, string? passphrase = null, bool isTestnet = false) =>
        type switch
        {
            ExchangeType.Binance => new BinanceClient(apiKey, secretKey, isTestnet),
            ExchangeType.OKX => new OkxClient(apiKey, secretKey, passphrase),
            ExchangeType.Gate => new GateIoClient(apiKey, secretKey),
            ExchangeType.Bybit => new BybitClient(apiKey, secretKey, isTestnet),
            ExchangeType.HTX => new HtxClient(apiKey, secretKey),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, $"不支持的交易所类型: {type}")
        };
}
