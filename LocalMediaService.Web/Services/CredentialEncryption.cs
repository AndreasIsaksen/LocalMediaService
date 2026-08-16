using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalMediaService.Web.Models;
using LocalMediaService.Web.Options;
using Microsoft.Extensions.Options;

namespace LocalMediaService.Web.Services;

public sealed class CredentialEncryption
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] _key;

    public CredentialEncryption(IOptions<CredentialStoreOptions> options)
    {
        _key = Convert.FromBase64String(options.Value.EncryptionKey);
    }

    public static bool IsValidKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(value).Length == KeySize;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal void Encrypt(StoredCredential record, CredentialPayload payload)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, GetAdditionalData(record));
        CryptographicOperations.ZeroMemory(plaintext);

        record.Nonce = Convert.ToBase64String(nonce);
        record.Ciphertext = Convert.ToBase64String(ciphertext);
        record.Tag = Convert.ToBase64String(tag);
    }

    internal CredentialPayload Decrypt(StoredCredential record)
    {
        var nonce = Convert.FromBase64String(record.Nonce);
        var ciphertext = Convert.FromBase64String(record.Ciphertext);
        var tag = Convert.FromBase64String(record.Tag);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, GetAdditionalData(record));
            return JsonSerializer.Deserialize<CredentialPayload>(plaintext, JsonOptions)
                   ?? throw new CryptographicException("Credential payload is empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] GetAdditionalData(StoredCredential record) =>
        Encoding.UTF8.GetBytes($"LocalMediaService.Credential.v1|{record.Id:D}|{record.ServiceId}");
}
