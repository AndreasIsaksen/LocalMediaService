using System.Security.Cryptography;
using LocalMediaService.Web.Models;
using LocalMediaService.Web.Options;
using LocalMediaService.Web.Services;
using Microsoft.Extensions.Options;

namespace LocalMediaService.Tests;

public sealed class CredentialStoreTests
{
    [Fact]
    public async Task Store_EncryptsSensitiveValuesAndRoundTripsThem()
    {
        using var directory = new TestDirectory();
        var encryptionOptions = Options.Create(new CredentialStoreOptions
        {
            EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        });
        var store = new EncryptedCredentialStore(
            Options.Create(new StorageOptions { DataPath = directory.Path }),
            new CredentialEncryption(encryptionOptions));
        var input = new CredentialInput("netflix", "Family", "person@example.com", "very-secret-password");

        var saved = await store.SaveAsync(null, input, CancellationToken.None);
        var rawFile = await File.ReadAllTextAsync(System.IO.Path.Combine(directory.Path, "credentials.json"));
        var revealed = await store.RevealAsync(saved.Id, CancellationToken.None);

        Assert.DoesNotContain(input.Username, rawFile, StringComparison.Ordinal);
        Assert.DoesNotContain(input.Password, rawFile, StringComparison.Ordinal);
        Assert.NotNull(revealed);
        Assert.Equal(input.Username, revealed.Username);
        Assert.Equal(input.Password, revealed.Password);
        Assert.Equal("p***@example.com", saved.UsernameHint);
    }

    [Fact]
    public void EncryptionKeyValidation_RequiresExactly32Base64Bytes()
    {
        Assert.True(CredentialEncryption.IsValidKey(Convert.ToBase64String(new byte[32])));
        Assert.False(CredentialEncryption.IsValidKey(Convert.ToBase64String(new byte[16])));
        Assert.False(CredentialEncryption.IsValidKey("not-base64"));
    }
}
