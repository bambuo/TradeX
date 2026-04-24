using TradeX.Core.Enums;

namespace TradeX.Core.Interfaces;

public interface IExchangeClientFactory
{
    IExchangeClient CreateClient(ExchangeType type, string apiKey, string secretKey, string? passphrase = null, bool isTestnet = false);
}
