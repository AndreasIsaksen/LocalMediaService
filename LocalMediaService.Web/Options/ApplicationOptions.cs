namespace LocalMediaService.Web.Options;

public sealed class MediaLibraryOptions
{
    public const string SectionName = "MediaLibrary";

    public string RootPath { get; set; } = "/media";
    public string MountSentinelFile { get; set; } = ".local-media-volume";
    public int ScanIntervalSeconds { get; set; } = 30;
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string DataPath { get; set; } = "/data";
}

public sealed class PortalSecurityOptions
{
    public const string SectionName = "PortalSecurity";

    public string AdminUsername { get; set; } = "admin";
    public string AdminPassword { get; set; } = string.Empty;
    public bool RequireHttps { get; set; } = true;
}

public sealed class CredentialStoreOptions
{
    public const string SectionName = "CredentialStore";

    public string EncryptionKey { get; set; } = string.Empty;
}

public sealed class StreamingServicesOptions
{
    public const string SectionName = "StreamingServices";

    public List<StreamingServiceDefinition> Services { get; set; } = [];
}

public sealed class StreamingServiceDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string HomeUrl { get; set; } = string.Empty;
    public string LoginUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Accent { get; set; } = "#7c3aed";
}
