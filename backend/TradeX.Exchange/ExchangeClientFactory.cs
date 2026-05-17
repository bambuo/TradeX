using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Exchange.Adapters;

namespace TradeX.Exchange;

public class ExchangeClientFactory : IExchangeClientFactory
{
    public IExchangeClient CreateClient(ExchangeType type, string apiKey, string secretKey, string? passphrase = null, bool isTestnet = false) =>
        type switch
        {
            ExchangeType.Binance => new BinanceClientAdapter(apiKey, secretKey, isTestnet),
            ExchangeType.OKX => new OkxClientAdapter(apiKey, secretKey, passphrase),
            ExchangeType.Gate => new GateIoClientAdapter(apiKey, secretKey),
            ExchangeType.Bybit => new BybitClientAdapter(apiKey, secretKey, isTestnet),
            ExchangeType.HTX => new HtxClientAdapter(apiKey, secretKey),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, $"不支持的交易所类型: {type}")
        };
}
