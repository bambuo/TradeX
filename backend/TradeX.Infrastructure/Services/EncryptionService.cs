using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using TradeX.Core.Interfaces;
using TradeX.Infrastructure.Settings;

namespace TradeX.Infrastructure.Services;

public class EncryptionService(IOptions<EncryptionSettings> options) : IEncryptionService
{
    private readonly byte[] _key = Convert.FromBase64String(options.Value.Key);

    public string Encrypt(string plaintext)
    {
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[12];
        var tag = new byte[16];
        var cipherBytes = new byte[plainBytes.Length];

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var combined = new byte[12 + 16 + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, 12);
        Buffer.BlockCopy(tag, 0, combined, 12, 16);
        Buffer.BlockCopy(cipherBytes, 0, combined, 28, cipherBytes.Length);

        return Convert.ToBase64String(combined);
    }

    public string Decrypt(string ciphertext)
    {
        var combined = Convert.FromBase64String(ciphertext);
        var nonce = combined[..12];
        var tag = combined[12..28];
        var cipherBytes = combined[28..];
        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return System.Text.Encoding.UTF8.GetString(plainBytes);
    }
}
