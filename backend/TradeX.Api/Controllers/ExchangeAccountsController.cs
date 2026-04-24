using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/traders/{traderId:guid}/exchanges")]
[Authorize]
public class ExchangeAccountsController(
    IExchangeAccountRepository exchangeRepo,
    IExchangeClientFactory clientFactory,
    IEncryptionService encryption) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(
        Guid traderId,
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var accounts = await exchangeRepo.GetByTraderIdAsync(traderId, ct);
        var result = accounts.Select(a => new
        {
            a.Id, a.TraderId, a.Name, exchangeType = a.Type.ToString(),
            status = a.Status.ToString(),
            a.LastTestedAtUtc, a.TestResult, a.CreatedAtUtc
        });

        return Ok(new { data = result, total = accounts.Count });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var account = await exchangeRepo.GetByIdAsync(id, ct);
        if (account is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所配置不存在" });

        return Ok(new
        {
            account.Id, account.TraderId, account.Name, exchangeType = account.Type.ToString(),
            status = account.Status.ToString(),
            account.LastTestedAtUtc, account.TestResult, account.CreatedAtUtc
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid traderId,
        [FromBody] CreateExchangeRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ApiKey) || string.IsNullOrWhiteSpace(request.SecretKey))
            return BadRequest(new { code = "VALIDATION_ERROR", message = "名称、API Key 和 Secret Key 不能为空" });

        if (!Enum.TryParse<ExchangeType>(request.ExchangeType, true, out var exchangeType))
            return BadRequest(new { code = "VALIDATION_ERROR", message = $"不支持的交易所类型: {request.ExchangeType}" });

        var isNameUnique = await exchangeRepo.IsNameUniqueAsync(traderId, request.Name, ct);
        if (!isNameUnique)
            return Conflict(new { code = "VALIDATION_ERROR", message = "交易所名称已存在" });

        var account = new ExchangeAccount
        {
            TraderId = traderId,
            Name = request.Name,
            Type = exchangeType,
            ApiKeyEncrypted = encryption.Encrypt(request.ApiKey),
            SecretKeyEncrypted = encryption.Encrypt(request.SecretKey),
            PassphraseEncrypted = request.Passphrase is not null ? encryption.Encrypt(request.Passphrase) : null,
            CreatedBy = UserId
        };

        await exchangeRepo.AddAsync(account, ct);

        return CreatedAtAction(nameof(GetById), new { traderId, id = account.Id }, new
        {
            account.Id, account.TraderId, account.Name, exchangeType = account.Type.ToString(),
            status = account.Status.ToString(), account.CreatedAtUtc
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateExchangeRequest request, CancellationToken ct)
    {
        var account = await exchangeRepo.GetByIdAsync(id, ct);
        if (account is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所配置不存在" });

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var isNameUnique = await exchangeRepo.IsNameUniqueAsync(UserId, request.Name, ct);
            if (!isNameUnique)
                return Conflict(new { code = "VALIDATION_ERROR", message = "交易所名称已存在" });
            account.Name = request.Name;
        }
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
            account.ApiKeyEncrypted = encryption.Encrypt(request.ApiKey);
        if (!string.IsNullOrWhiteSpace(request.SecretKey))
            account.SecretKeyEncrypted = encryption.Encrypt(request.SecretKey);
        if (request.Passphrase is not null)
            account.PassphraseEncrypted = encryption.Encrypt(request.Passphrase);
        if (request.Status is not null && Enum.TryParse<ExchangeAccountStatus>(request.Status, true, out var status))
            account.Status = status;

        await exchangeRepo.UpdateAsync(account, ct);
        return Ok(new { account.Id, account.Name, status = account.Status.ToString(), account.UpdatedAtUtc });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var account = await exchangeRepo.GetByIdAsync(id, ct);
        if (account is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所配置不存在" });

        await exchangeRepo.DeleteAsync(account, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken ct)
    {
        var account = await exchangeRepo.GetByIdAsync(id, ct);
        if (account is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所配置不存在" });

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

            if (!result.Success)
                return BadRequest(new { code = "EXCHANGE_TEST_FAILED", message = result.Message ?? "连接失败" });

            return Ok(new
            {
                success = true,
                permissions = result.Permissions is not null ? new
                {
                    spotTrade = result.Permissions.GetValueOrDefault("spotTrade", false),
                    ipWhitelistRecommended = result.Permissions.GetValueOrDefault("ipWhitelistRecommended", true),
                    withdrawDisabled = result.Permissions.GetValueOrDefault("withdrawDisabled", false)
                } : null,
                message = result.Message
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, error = ex.Message });
        }
    }

    [HttpGet("{id:guid}/rules")]
    public async Task<IActionResult> GetRules(Guid id, CancellationToken ct)
    {
        var account = await exchangeRepo.GetByIdAsync(id, ct);
        if (account is null)
            return NotFound(new { code = "EXCHANGE_NOT_FOUND", message = "交易所配置不存在" });

        if (account.Status == ExchangeAccountStatus.Disabled)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "交易所已禁用" });

        try
        {
            var apiKey = encryption.Decrypt(account.ApiKeyEncrypted);
            var secretKey = encryption.Decrypt(account.SecretKeyEncrypted);
            var passphrase = account.PassphraseEncrypted is not null ? encryption.Decrypt(account.PassphraseEncrypted) : null;

            var client = clientFactory.CreateClient(account.Type, apiKey, secretKey, passphrase);
            var rules = await client.GetSymbolRulesAsync(ct);

            return Ok(new
            {
                data = rules.Select(r => new
                {
                    r.Symbol, r.PricePrecision, r.QuantityPrecision,
                    r.MinNotional, r.MinQuantity, r.TickSize, r.StepSize
                }),
                cachedAtUtc = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { code = "EXCHANGE_RULES_FETCH_FAILED", message = ex.Message });
        }
    }

    public record CreateExchangeRequest(string Name, string ExchangeType, string ApiKey, string SecretKey, string? Passphrase = null);
    public record UpdateExchangeRequest(string? Name = null, string? ApiKey = null, string? SecretKey = null, string? Passphrase = null, string? Status = null);
}
