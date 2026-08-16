namespace LocalMediaService.Web.Models;

public sealed record LoginRequest(string Username, string Password, bool RememberMe = false);

public sealed record CredentialInput(string ServiceId, string Label, string Username, string Password);

public sealed record RevealCredentialRequest(string AdminPassword);

public sealed record CredentialSummary(
    Guid Id,
    string ServiceId,
    string Label,
    string UsernameHint,
    bool HasPassword,
    DateTimeOffset UpdatedAt);

public sealed record RevealedCredential(Guid Id, string ServiceId, string Label, string Username, string Password);

public sealed record StreamingServiceResponse(
    string Id,
    string Name,
    string HomeUrl,
    string LoginUrl,
    string Description,
    string Accent,
    IReadOnlyList<CredentialSummary> Accounts);

public sealed record MediaItemResponse(
    string Id,
    string Title,
    string RelativePath,
    string Category,
    long SizeBytes,
    DateTimeOffset LastModified,
    string ContentType,
    string StreamUrl,
    bool DirectPlayLikely,
    IReadOnlyList<SubtitleTrackResponse> Subtitles);

public sealed record SubtitleTrackResponse(string Id, string Label, string Language, string Url);

internal sealed record CredentialPayload(string Username, string Password);

internal sealed class StoredCredentialDocument
{
    public int Version { get; set; } = 1;
    public List<StoredCredential> Credentials { get; set; } = [];
}

internal sealed class StoredCredential
{
    public Guid Id { get; set; }
    public string ServiceId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public string Ciphertext { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed record SubtitleFile(string Id, string Label, string Language, string RelativePath, string FullPath);

internal sealed record MediaFile(
    string Id,
    string Title,
    string RelativePath,
    string Category,
    string FullPath,
    long SizeBytes,
    DateTimeOffset LastModified,
    string ContentType,
    bool DirectPlayLikely,
    IReadOnlyList<SubtitleFile> Subtitles);

internal sealed record MediaSnapshot(
    IReadOnlyList<MediaFile> Items,
    bool RootAvailable,
    DateTimeOffset ScannedAt,
    IReadOnlyList<string> Warnings);

internal sealed record OpenedMediaFile(MediaFile Media, FileStream Stream);

internal sealed record OpenedSubtitleFile(MediaFile Media, SubtitleFile Subtitle, FileStream Stream);
