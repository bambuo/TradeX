using System.Diagnostics;
using System.Text;
using BscSmartMoneyBot.Configuration;
using BscSmartMoneyBot.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace BscSmartMoneyBot.Services.Implementations.Clients;

public class OnchainOSClient(ILogger<OnchainOSClient> logger, IOptions<BotSettings> settingsOptions)
{
    private readonly BotSettings _settings = settingsOptions.Value;
    private readonly string _binaryPath = settingsOptions.Value.OnchainOS.BinaryPath;
    private readonly int _timeoutSeconds = settingsOptions.Value.OnchainOS.TimeoutSeconds;

    public async Task<List<Signal>> GetSignalsAsync(string chain, CancellationToken ct)
    {
        try
        {
            var output = await ExecuteCommandAsync($"signal list --chain {chain}", ct);
            var root = ParseJson(output);
            var data = ExtractDataToken(root);

            var listToken = data.Type == JTokenType.Array
                ? data
                : data["list"] ?? data["rows"] ?? new JArray();

            List<Signal> result = [];
            foreach (var item in listToken.Children<JToken>())
            {
                var tokenAddress = (item.Value<string>("tokenAddress")
                                    ?? item.Value<string>("address")
                                    ?? item.SelectToken("token.address")?.ToString()
                                    ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(tokenAddress)) continue;

                var signal = new Signal
                {
                    TokenAddress = tokenAddress,
                    TokenSymbol = item.Value<string>("symbol") ?? item.Value<string>("tokenSymbol") ?? "UNKNOWN",
                    TokenName = item.Value<string>("name") ?? item.Value<string>("tokenName") ?? string.Empty,
                    MarketCap = ReadDecimal(item, "marketCap", "market_cap", "mc"),
                    Liquidity = ReadDecimal(item, "liquidity", "liquidityUsd", "lpUsd"),
                    SoldRatio = ReadDecimal(item, "soldRatio", "sold_ratio"),
                    SmartMoneyWallets = (int)ReadDecimal(item, "smartMoneyWallets", "triggeredWallets", "walletCount"),
                    SignalTime = DateTime.UtcNow,
                    PriceUSD = ReadDecimal(item, "price", "priceUsd", "priceUSD")
                };

                result.Add(signal);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取信号失败");
            return [];
        }
    }

    public async Task<SecurityScanResult> SecurityScanAsync(string chain, string tokenAddress, CancellationToken ct)
    {
        try
        {
            var output = await ExecuteCommandAsync($"security token-scan --tokens \"56:{tokenAddress}\"", ct);
            var root = ParseJson(output);
            var data = ExtractDataToken(root);

            var riskLevel = data.SelectToken("riskLevel")?.ToString()
                            ?? data.SelectToken("risk.level")?.ToString()
                            ?? data.SelectToken("overallRiskLevel")?.ToString()
                            ?? "UNKNOWN";

            var labels = data.SelectToken("riskLabels") ?? data.SelectToken("risk.labels") ?? new JArray();

            return new SecurityScanResult
            {
                RiskLevel = riskLevel.ToUpperInvariant(),
                RiskLabels = labels.Type == JTokenType.Array
                    ? labels.Values<string?>().Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList()
                    : []
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "安全扫描失败: {Token}", tokenAddress);
            return new SecurityScanResult();
        }
    }

    public async Task<SwapResult> ExecuteSwapAsync(string chain, string fromToken, string toToken, decimal amount, string wallet, decimal slippage, CancellationToken ct)
    {
        try
        {
            var output = await ExecuteCommandAsync($"swap execute --chain {chain} --from {fromToken} --to {toToken} --readable-amount {amount} --wallet {wallet} --slippage {slippage}", ct);
            var root = ParseJson(output);
            var data = ExtractDataToken(root);

            return new SwapResult
            {
                SwapTxHash = data.SelectToken("swapTxHash")?.ToString()
                             ?? data.SelectToken("txHash")?.ToString()
                             ?? data.SelectToken("hash")?.ToString()
                             ?? string.Empty,
                FromAmount = ReadDecimal(data, "fromAmount", "fromTokenAmount", "inputAmount", "amountIn"),
                ToAmount = ReadDecimal(data, "toAmount", "toTokenAmount", "outputAmount", "amountOut"),
                PriceImpact = ReadDecimal(data, "priceImpact", "impact"),
                GasUsed = ReadDecimal(data, "gasUsed", "gas")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "交易失败: {From} -> {To}", fromToken, toToken);
            throw;
        }
    }

    public async Task<decimal> GetTokenPriceAsync(string chain, string tokenAddress, CancellationToken ct)
    {
        try
        {
            var output = await ExecuteCommandAsync($"market price --chain {chain} --address {tokenAddress}", ct);
            var root = ParseJson(output);
            var data = ExtractDataToken(root);
            return ReadDecimal(data, "price", "priceUsd", "priceUSD");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取价格失败: {Token}", tokenAddress);
            return 0;
        }
    }

    public async Task<decimal> GetTokenBalanceAsync(string chain, string tokenAddress, string wallet, CancellationToken ct)
    {
        try
        {
            var output = await ExecuteCommandAsync($"wallet balance --chain {chain} --address {wallet} --token {tokenAddress}", ct);
            var root = ParseJson(output);
            var data = ExtractDataToken(root);
            return ReadDecimal(data, "balance", "readableBalance", "available");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取余额失败: {Token} {Wallet}", tokenAddress, wallet);
            return 0;
        }
    }

    public async Task<TransactionDetail> GetTransactionDetailAsync(string chain, string txHash, CancellationToken ct)
    {
        try
        {
            var output = await ExecuteCommandAsync($"wallet history --tx-hash {txHash} --chain {chain}", ct);
            var root = ParseJson(output);
            var data = ExtractDataToken(root);

            return new TransactionDetail
            {
                TxHash = txHash,
                ServiceChargeUsd = ReadDecimal(data, "serviceChargeUsd", "feeUsd", "gasUsd"),
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取交易详情失败: {TxHash}", txHash);
            return new TransactionDetail();
        }
    }

    private async Task<string> ExecuteCommandAsync(string arguments, CancellationToken ct)
    {
        var maxRetries = Math.Max(1, _settings.OnchainOS.RetryCount);
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var process = new Process();
                process.StartInfo.FileName = _binaryPath;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        outputBuilder.AppendLine(e.Data);
                };

                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        errorBuilder.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));
                await process.WaitForExitAsync(timeoutCts.Token);

                if (process.ExitCode != 0)
                {
                    var error = errorBuilder.ToString();
                    throw new InvalidOperationException($"命令执行失败 (exit={process.ExitCode}): {error}");
                }

                return outputBuilder.ToString().Trim();
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                logger.LogWarning(ex, "命令执行失败，重试 {Attempt}/{MaxRetries}: {Command}", attempt, maxRetries, arguments);
                var delayMs = Math.Max(100, _settings.OnchainOS.RetryDelayMs * attempt);
                await Task.Delay(delayMs, ct);
            }
        }

        throw new InvalidOperationException($"命令执行失败，达到最大重试次数: {_binaryPath} {arguments}");
    }

    private static JObject ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JObject();
        return JObject.Parse(json);
    }

    private static JToken ExtractDataToken(JToken root)
    {
        var data = root["data"];
        if (data is not null && data.Type != JTokenType.Null)
        {
            return data;
        }

        return root;
    }

    private static decimal ReadDecimal(JToken token, params string[] paths)
    {
        foreach (var path in paths)
        {
            var value = token.SelectToken(path);
            if (value is null || value.Type == JTokenType.Null) continue;

            if (decimal.TryParse(value.ToString(), out var number))
            {
                return number;
            }
        }

        return 0m;
    }
}
