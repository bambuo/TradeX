using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/exchanges")]
[Authorize]
public class ExchangesController(
    ITraderRepository traderRepo,
    IExchangeAccountRepository exchangeRepo,
    IExchangeClientFactory clientFactory,
    IEncryptionService encryption) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var traders = await traderRepo.GetByUserIdAsync(UserId, ct);
        var traderMap = traders.ToDictionary(t => t.Id, t => t.Name);

        var accounts = await exchangeRepo.GetAllByUserIdAsync(UserId, ct);

        var result = accounts.Select(a => new
        {
            a.Id,
            a.TraderId,
            traderName = a.TraderId.HasValue ? traderMap.GetValueOrDefault(a.TraderId.Value, "未知") : "全局",
            label = a.Name,
            exchangeType = a.Type.ToString(),
            isEnabled = a.Status == ExchangeAccountStatus.Enabled,
            a.LastTestedAtUtc,
            testResult = a.TestResult,
            createdAt = a.CreatedAt,
            updatedAt = a.UpdatedAt
        });

        return Ok(new { data = result });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateExchangeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ApiKey) || string.IsNullOrWhiteSpace(request.SecretKey))
            return BadRequest(new { code = "VALIDATION_ERROR", message = "名称、API Key 和 Secret Key 不能为空" });

        if (!Enum.TryParse<ExchangeType>(request.ExchangeType, true, out var exchangeType))
            return BadRequest(new { code = "VALIDATION_ERROR", message = $"不支持的交易所类型: {request.ExchangeType}" });

        if (!await exchangeRepo.IsNameUniqueAsync(request.Name, ct))
            return Conflict(new { code = "VALIDATION_ERROR", message = "交易所名称已存在" });

        var account = new ExchangeAccount
        {
            TraderId = null,
            Name = request.Name,
            Type = exchangeType,
            ApiKeyEncrypted = encryption.Encrypt(request.ApiKey),
            SecretKeyEncrypted = encryption.Encrypt(request.SecretKey),
            PassphraseEncrypted = request.Passphrase is not null ? encryption.Encrypt(request.Passphrase) : null,
            CreatedBy = UserId
        };

        await exchangeRepo.AddAsync(account, ct);

        return CreatedAtAction(nameof(GetAll), null, new
        {
            account.Id,
            traderName = "全局",
            label = account.Name,
            exchangeType = account.Type.ToString(),
            isEnabled = account.Status == ExchangeAccountStatus.Enabled,
            createdAt = account.CreatedAt,
            updatedAt = account.UpdatedAt
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateExchangeRequest request, CancellationToken ct)
    {
        var account = await exchangeRepo.GetByIdAsync(id, ct);
        if (account is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所不存在" });

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            if (account.Name != request.Name && !await exchangeRepo.IsNameUniqueAsync(request.Name, ct))
                return Conflict(new { code = "VALIDATION_ERROR", message = "交易所名称已存在" });
            account.Name = request.Name;
        }
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
            account.ApiKeyEncrypted = encryption.Encrypt(request.ApiKey);
        if (!string.IsNullOrWhiteSpace(request.SecretKey))
            account.SecretKeyEncrypted = encryption.Encrypt(request.SecretKey);
        if (request.Passphrase is not null)
            account.PassphraseEncrypted = encryption.Encrypt(request.Passphrase);

        await exchangeRepo.UpdateAsync(account, ct);
        return Ok(new { account.Id, account.Name, account.UpdatedAt });
    }

    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken ct)
    {
        var account = await exchangeRepo.GetByIdAsync(id, ct);
        if (account is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所不存在" });

        if (account.Status == ExchangeAccountStatus.Disabled)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "交易所已禁用" });

        try
        {
            var apiKey = encryption.Decrypt(account.ApiKeyEncrypted);
            var secretKey = encryption.Decrypt(account.SecretKeyEncrypted);
            var passphrase = account.PassphraseEncrypted is not null ? encryption.Decrypt(account.PassphraseEncrypted) : null;

            var client = clientFactory.CreateClient(account.Type, apiKey, secretKey, passphrase);
            var result = await client.TestConnectionAsync(ct);

            account.LastTestedAtUtc = DateTime.UtcNow;
            account.TestResult = result.Message;
            await exchangeRepo.UpdateAsync(account, ct);

            return Ok(new { connected = result.Success, error = result.Success ? null : result.Message });
        }
        catch (Exception ex)
        {
            return Ok(new { connected = false, error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var account = await exchangeRepo.GetByIdAsync(id, ct);
        if (account is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所不存在" });

        await exchangeRepo.DeleteAsync(account, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/symbols")]
    public async Task<IActionResult> GetSymbols(Guid id, CancellationToken ct)
    {
        var account = await exchangeRepo.GetByIdAsync(id, ct);
        if (account is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所不存在" });

        if (account.Status == ExchangeAccountStatus.Disabled)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "交易所已禁用" });

        try
        {
            var apiKey = encryption.Decrypt(account.ApiKeyEncrypted);
            var secretKey = encryption.Decrypt(account.SecretKeyEncrypted);
            var passphrase = account.PassphraseEncrypted is not null ? encryption.Decrypt(account.PassphraseEncrypted) : null;

            var client = clientFactory.CreateClient(account.Type, apiKey, secretKey, passphrase);

            var rulesTask = client.GetSymbolRulesAsync(ct);
            var tickerTask = client.GetTickerPricesAsync(ct);

            await Task.WhenAll(rulesTask, tickerTask);

            var rules = rulesTask.Result;
            var tickers = tickerTask.Result;
            var tickerMap = tickers.ToDictionary(t => t.Symbol, t => t);

            var symbols = rules
                .Where(r => r.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
                .Select(r =>
                {
                    var t = tickerMap.GetValueOrDefault(r.Symbol);
                    return new
                    {
                        symbol = r.Symbol,
                        pricePrecision = r.PricePrecision,
                        quantityPrecision = r.QuantityPrecision,
                        minNotional = r.MinNotional,
                        price = t?.Price ?? 0,
                        priceChangePercent = t?.PriceChangePercent ?? 0,
                        volume = t?.Volume ?? 0,
                        highPrice = t?.HighPrice ?? 0,
                        lowPrice = t?.LowPrice ?? 0
                    };
                })
                .OrderBy(r => r.symbol)
                .ToList();

            return Ok(new { data = symbols });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { code = "EXCHANGE_ERROR", message = $"获取交易对数据失败: {ex.Message}" });
        }
    }

    public record CreateExchangeRequest(string Name, string ExchangeType, string ApiKey, string SecretKey, string? Passphrase = null);
    public record UpdateExchangeRequest(string? Name = null, string? ApiKey = null, string? SecretKey = null, string? Passphrase = null);
}
