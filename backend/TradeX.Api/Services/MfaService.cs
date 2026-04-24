using System.Security.Cryptography;
using TradeX.Core.Models;

namespace TradeX.Api.Services;

public class MfaService
{
    private const int SecretLength = 20;
    private const int RecoveryCodeCount = 8;
    private const int RecoveryCodeLength = 9;
    private const int TotpIntervalSeconds = 30;
    private const int TotpCodeLength = 6;

    private static readonly char[] Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();

    public MfaSecret GenerateSecret(Guid userId)
    {
        var bytes = new byte[SecretLength];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);

        var secretKey = BytesToBase32(bytes);

        return new MfaSecret
        {
            UserId = userId,
            SecretKey = secretKey
        };
    }

    public bool ValidateTotp(string base32Secret, string code, int timeTolerance = 1)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != TotpCodeLength)
            return false;

        if (!int.TryParse(code, out var providedCode))
            return false;

        var secretBytes = Base32ToBytes(base32Secret);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        for (var offset = -timeTolerance; offset <= timeTolerance; offset++)
        {
            var counter = timestamp / TotpIntervalSeconds + offset;
            var computedCode = GenerateTotpCode(secretBytes, counter);
            if (computedCode == providedCode)
                return true;
        }

        return false;
    }

    private static int GenerateTotpCode(byte[] secret, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counterBytes);

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes);

        var offset = hash[^1] & 0x0F;
        var binaryCode = (hash[offset] & 0x7F) << 24
                       | (hash[offset + 1] & 0xFF) << 16
                       | (hash[offset + 2] & 0xFF) << 8
                       | (hash[offset + 3] & 0xFF);

        return binaryCode % (int)Math.Pow(10, TotpCodeLength);
    }

    public List<RecoveryCode> GenerateRecoveryCodes(Guid userId)
    {
        var codes = new List<RecoveryCode>(RecoveryCodeCount);
        using var rng = RandomNumberGenerator.Create();

        for (var i = 0; i < RecoveryCodeCount; i++)
        {
            var bytes = new byte[RecoveryCodeLength];
            rng.GetBytes(bytes);
            var hex = Convert.ToHexString(bytes)[..Math.Min(RecoveryCodeLength, 16)];
            var formatted = string.Concat(hex.AsSpan(0, 4), "-", hex.AsSpan(4, 4));
            codes.Add(new RecoveryCode
            {
                UserId = userId,
                Code = formatted.ToUpperInvariant()
            });
        }

        return codes;
    }

    private static string BytesToBase32(byte[] bytes)
    {
        var bitCount = bytes.Length * 8;
        var resultCapacity = (bitCount + 4) / 5;
        var result = new char[resultCapacity];

        var buffer = 0;
        var bitsRemaining = 0;
        var index = 0;

        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsRemaining += 8;
            while (bitsRemaining >= 5)
            {
                bitsRemaining -= 5;
                result[index++] = Base32Chars[(buffer >> bitsRemaining) & 0x1F];
            }
        }

        if (bitsRemaining > 0)
            result[index++] = Base32Chars[(buffer << (5 - bitsRemaining)) & 0x1F];

        return new string(result, 0, index);
    }

    private static byte[] Base32ToBytes(string base32)
    {
        base32 = base32.TrimEnd('=').ToUpperInvariant();
        var bitCount = base32.Length * 5;
        var byteCount = bitCount / 8;
        var bytes = new byte[byteCount];

        var buffer = 0;
        var bitsRemaining = 0;
        var byteIndex = 0;

        foreach (var c in base32)
        {
            var value = Array.IndexOf(Base32Chars, c);
            if (value < 0) continue;

            buffer = (buffer << 5) | value;
            bitsRemaining += 5;
            if (bitsRemaining >= 8)
            {
                bitsRemaining -= 8;
                bytes[byteIndex++] = (byte)((buffer >> bitsRemaining) & 0xFF);
            }
        }

        return bytes;
    }
}
