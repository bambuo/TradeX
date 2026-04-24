using Microsoft.Extensions.Options;
using TradeX.Infrastructure.Services;
using TradeX.Infrastructure.Settings;

namespace TradeX.Tests.Infrastructure;

public class EncryptionServiceTests
{
    private static EncryptionService CreateService()
    {
        var key = Convert.ToBase64String(new byte[32]);
        var settings = Options.Create(new EncryptionSettings { Key = key });
        return new EncryptionService(settings);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip_ReturnsOriginal()
    {
        var service = CreateService();
        var original = "my-api-key-12345";

        var encrypted = service.Encrypt(original);
        var decrypted = service.Decrypt(encrypted);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_EmptyString_ReturnsEmptyString()
    {
        var service = CreateService();
        var original = "";

        var encrypted = service.Encrypt(original);
        var decrypted = service.Decrypt(encrypted);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_SpecialCharacters_ReturnsOriginal()
    {
        var service = CreateService();
        var original = "!@#$%^&*()_+-=[]{}|;':\",./<>?`~";

        var encrypted = service.Encrypt(original);
        var decrypted = service.Decrypt(encrypted);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_UnicodeCharacters_ReturnsOriginal()
    {
        var service = CreateService();
        var original = "你好世界🔥🆗";

        var encrypted = service.Encrypt(original);
        var decrypted = service.Decrypt(encrypted);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesUniqueCiphertexts()
    {
        var service = CreateService();
        var plaintext = "same-text";

        var encrypted1 = service.Encrypt(plaintext);
        var encrypted2 = service.Encrypt(plaintext);

        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void Decrypt_WrongKey_Throws()
    {
        var key1 = Convert.ToBase64String(new byte[32] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 });
        var key2 = Convert.ToBase64String(new byte[32] { 32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 });

        var service1 = new EncryptionService(Options.Create(new EncryptionSettings { Key = key1 }));
        var service2 = new EncryptionService(Options.Create(new EncryptionSettings { Key = key2 }));

        var encrypted = service1.Encrypt("secret");

        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(() =>
            service2.Decrypt(encrypted));
    }

    [Fact]
    public void Decrypt_InvalidBase64_Throws()
    {
        var service = CreateService();

        Assert.Throws<FormatException>(() =>
            service.Decrypt("not-valid-base64!"));
    }
}
