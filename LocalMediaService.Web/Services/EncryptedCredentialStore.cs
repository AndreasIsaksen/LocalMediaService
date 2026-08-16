using System.Security.Cryptography;
using System.Text.Json;
using LocalMediaService.Web.Models;
using LocalMediaService.Web.Options;
using Microsoft.Extensions.Options;

namespace LocalMediaService.Web.Services;

public sealed class EncryptedCredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _storePath;
    private readonly CredentialEncryption _encryption;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EncryptedCredentialStore(
        IOptions<StorageOptions> storageOptions,
        CredentialEncryption encryption)
    {
        var dataPath = Path.GetFullPath(storageOptions.Value.DataPath);
        Directory.CreateDirectory(dataPath);
        _storePath = Path.Combine(dataPath, "credentials.json");
        _encryption = encryption;
    }

    public async Task<IReadOnlyList<CredentialSummary>> ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentAsync(cancellationToken);
            return document.Credentials
                .Select(ToSummary)
                .OrderBy(item => item.ServiceId, StringComparer.Ordinal)
                .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CredentialSummary> SaveAsync(
        Guid? id,
        CredentialInput input,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentAsync(cancellationToken);
            var record = id is null
                ? null
                : document.Credentials.SingleOrDefault(item => item.Id == id.Value);

            if (id is not null && record is null)
            {
                throw new KeyNotFoundException("The saved account does not exist.");
            }

            if (record is null)
            {
                record = new StoredCredential { Id = Guid.NewGuid() };
                document.Credentials.Add(record);
            }

            record.ServiceId = input.ServiceId;
            record.Label = input.Label.Trim();
            record.UpdatedAt = DateTimeOffset.UtcNow;
            _encryption.Encrypt(record, new CredentialPayload(input.Username.Trim(), input.Password));

            await WriteDocumentAsync(document, cancellationToken);
            return ToSummary(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RevealedCredential?> RevealAsync(Guid id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentAsync(cancellationToken);
            var record = document.Credentials.SingleOrDefault(item => item.Id == id);
            if (record is null)
            {
                return null;
            }

            var payload = _encryption.Decrypt(record);
            return new RevealedCredential(
                record.Id,
                record.ServiceId,
                record.Label,
                payload.Username,
                payload.Password);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentAsync(cancellationToken);
            var removed = document.Credentials.RemoveAll(item => item.Id == id) > 0;
            if (removed)
            {
                await WriteDocumentAsync(document, cancellationToken);
            }

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> IsReadableAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _ = await ReadDocumentAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or CryptographicException or FormatException)
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StoredCredentialDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storePath))
        {
            return new StoredCredentialDocument();
        }

        await using var stream = new FileStream(
            _storePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        var document = await JsonSerializer.DeserializeAsync<StoredCredentialDocument>(
            stream,
            JsonOptions,
            cancellationToken);

        if (document is null || document.Version != 1 || document.Credentials is null)
        {
            throw new JsonException("Unsupported or invalid credential store.");
        }

        foreach (var record in document.Credentials)
        {
            _ = _encryption.Decrypt(record);
        }

        return document;
    }

    private async Task WriteDocumentAsync(
        StoredCredentialDocument document,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_storePath)!;
        var tempPath = Path.Combine(directory, $"credentials.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(tempPath, _storePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private CredentialSummary ToSummary(StoredCredential record)
    {
        var payload = _encryption.Decrypt(record);
        return new CredentialSummary(
            record.Id,
            record.ServiceId,
            record.Label,
            MaskUsername(payload.Username),
            !string.IsNullOrEmpty(payload.Password),
            record.UpdatedAt);
    }

    private static string MaskUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return "Not set";
        }

        var at = username.IndexOf('@');
        if (at > 0)
        {
            return $"{username[0]}***{username[at..]}";
        }

        return username.Length == 1 ? "*" : $"{username[0]}***";
    }
}
