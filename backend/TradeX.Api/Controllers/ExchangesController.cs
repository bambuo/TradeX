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
    IExchangeRepository exchangeRepo,
    IExchangeClientFactory clientFactory,
    IEncryptionService encryption) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var traders = await traderRepo.GetByUserIdAsync(UserId, ct);
        var traderMap = traders.ToDictionary(t => t.Id, t => t.Name);

        var exchanges = await exchangeRepo.GetAllByUserIdAsync(UserId, ct);

        var result = exchanges.Select(e => new
        {
            e.Id,
            e.TraderId,
            traderName = e.TraderId.HasValue ? traderMap.GetValueOrDefault(e.TraderId.Value, "未知") : "全局",
            label = e.Name,
            exchangeType = e.Type.ToString(),
            isEnabled = e.Status == ExchangeStatus.Enabled,
            e.LastTestedAt,
            testResult = e.TestResult,
            createdAt = e.CreatedAt,
            updatedAt = e.UpdatedAt
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

        var exchange = new TradeX.Core.Models.Exchange
        {
            TraderId = null,
            Name = request.Name,
            Type = exchangeType,
            ApiKeyEncrypted = encryption.Encrypt(request.ApiKey),
            SecretKeyEncrypted = encryption.Encrypt(request.SecretKey),
            PassphraseEncrypted = request.Passphrase is not null ? encryption.Encrypt(request.Passphrase) : null,
            CreatedBy = UserId
        };

        await exchangeRepo.AddAsync(exchange, ct);

        return CreatedAtAction(nameof(GetAll), null, new
        {
            exchange.Id,
            traderName = "全局",
            label = exchange.Name,
            exchangeType = exchange.Type.ToString(),
            isEnabled = exchange.Status == ExchangeStatus.Enabled,
            createdAt = exchange.CreatedAt,
            updatedAt = exchange.UpdatedAt
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateExchangeRequest request, CancellationToken ct)
    {
        var exchange = await exchangeRepo.GetByIdAsync(id, ct);
        if (exchange is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所不存在" });

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            if (exchange.Name != request.Name && !await exchangeRepo.IsNameUniqueAsync(request.Name, ct))
                return Conflict(new { code = "VALIDATION_ERROR", message = "交易所名称已存在" });
            exchange.Name = request.Name;
        }
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
            exchange.ApiKeyEncrypted = encryption.Encrypt(request.ApiKey);
        if (!string.IsNullOrWhiteSpace(request.SecretKey))
            exchange.SecretKeyEncrypted = encryption.Encrypt(request.SecretKey);
        if (request.Passphrase is not null)
            exchange.PassphraseEncrypted = encryption.Encrypt(request.Passphrase);

        await exchangeRepo.UpdateAsync(exchange, ct);
        return Ok(new { exchange.Id, exchange.Name, exchange.UpdatedAt });
    }

    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken ct)
    {
        var exchange = await exchangeRepo.GetByIdAsync(id, ct);
        if (exchange is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所不存在" });

        if (exchange.Status == ExchangeStatus.Disabled)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "交易所已禁用" });

        try
        {
            var apiKey = encryption.Decrypt(exchange.ApiKeyEncrypted);
            var secretKey = encryption.Decrypt(exchange.SecretKeyEncrypted);
            var passphrase = exchange.PassphraseEncrypted is not null ? encryption.Decrypt(exchange.PassphraseEncrypted) : null;

            var client = clientFactory.CreateClient(exchange.Type, apiKey, secretKey, passphrase);
            var result = await client.TestConnectionAsync(ct);

            exchange.LastTestedAt = DateTime.UtcNow;
            exchange.TestResult = result.Message;
            await exchangeRepo.UpdateAsync(exchange, ct);

            return Ok(new { connected = result.Success, error = result.Success ? null : result.Message });
        }
        catch (Exception ex)
        {
            return Ok(new { connected = false, error = ex.Message });
        }
    }

    [HttpGet("{id:guid}/orders")]
    public async Task<IActionResult> GetOrders(Guid id, [FromQuery] string type = "open", CancellationToken ct = default)
    {
        var exchange = await exchangeRepo.GetByIdAsync(id, ct);
        if (exchange is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所不存在" });

        if (exchange.Status == ExchangeStatus.Disabled)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "交易所已禁用" });

        try
        {
            var apiKey = encryption.Decrypt(exchange.ApiKeyEncrypted);
            var secretKey = encryption.Decrypt(exchange.SecretKeyEncrypted);
            var passphrase = exchange.PassphraseEncrypted is not null ? encryption.Decrypt(exchange.PassphraseEncrypted) : null;

            var client = clientFactory.CreateClient(exchange.Type, apiKey, secretKey, passphrase);

            var orders = type == "history"
                ? await client.GetOrderHistoryAsync(ct)
                : await client.GetOpenOrdersAsync(ct);

            return Ok(new { data = orders });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { code = "EXCHANGE_ERROR", message = $"获取订单失败: {ex.Message}" });
        }
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<IActionResult> ToggleStatus(Guid id, CancellationToken ct)
    {
        var exchange = await exchangeRepo.GetByIdAsync(id, ct);
        if (exchange is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所不存在" });

        exchange.Status = exchange.Status == ExchangeStatus.Enabled ? ExchangeStatus.Disabled : ExchangeStatus.Enabled;
        await exchangeRepo.UpdateAsync(exchange, ct);

        return Ok(new { exchange.Id, isEnabled = exchange.Status == ExchangeStatus.Enabled });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var exchange = await exchangeRepo.GetByIdAsync(id, ct);
        if (exchange is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所不存在" });

        await exchangeRepo.DeleteAsync(exchange, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/assets")]
    public async Task<IActionResult> GetAssets(Guid id, CancellationToken ct)
    {
        var exchange = await exchangeRepo.GetByIdAsync(id, ct);
        if (exchange is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所不存在" });

        if (exchange.Status == ExchangeStatus.Disabled)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "交易所已禁用" });

        try
        {
            var apiKey = encryption.Decrypt(exchange.ApiKeyEncrypted);
            var secretKey = encryption.Decrypt(exchange.SecretKeyEncrypted);
            var passphrase = exchange.PassphraseEncrypted is not null ? encryption.Decrypt(exchange.PassphraseEncrypted) : null;

            var client = clientFactory.CreateClient(exchange.Type, apiKey, secretKey, passphrase);

            var assets = await client.GetAssetBalancesAsync(ct);
            var list = assets.Select(a => new { currency = a.Key, balance = a.Value }).OrderByDescending(a => a.balance).ToArray();
            return Ok(new { data = list });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { code = "EXCHANGE_ERROR", message = $"获取资产失败: {ex.Message}" });
        }
    }

    [HttpGet("{id:guid}/symbols")]
    public async Task<IActionResult> GetSymbols(Guid id, CancellationToken ct)
    {
        var exchange = await exchangeRepo.GetByIdAsync(id, ct);
        if (exchange is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所不存在" });

        if (exchange.Status == ExchangeStatus.Disabled)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "交易所已禁用" });

        try
        {
            var apiKey = encryption.Decrypt(exchange.ApiKeyEncrypted);
            var secretKey = encryption.Decrypt(exchange.SecretKeyEncrypted);
            var passphrase = exchange.PassphraseEncrypted is not null ? encryption.Decrypt(exchange.PassphraseEncrypted) : null;

            var client = clientFactory.CreateClient(exchange.Type, apiKey, secretKey, passphrase);

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
