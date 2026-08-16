using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using LocalMediaService.Web.Models;
using LocalMediaService.Web.Options;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.Options;

namespace LocalMediaService.Web.Services;

public sealed partial class MediaLibrary
{
    private const int AtFileDescriptorCurrentWorkingDirectory = -100;
    private const int OpenReadOnly = 0;
    private const int OpenNonBlocking = 0x800;
    private const int OpenDirectory = 0x10000;
    private const int OpenNoFollow = 0x20000;
    private const int OpenCloseOnExec = 0x80000;
    private static readonly IReadOnlyDictionary<string, (string ContentType, bool DirectPlayLikely)> VideoTypes =
        new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase)
        {
            [".mp4"] = ("video/mp4", true),
            [".m4v"] = ("video/mp4", true),
            [".webm"] = ("video/webm", true),
            [".ogv"] = ("video/ogg", true),
            [".mov"] = ("video/quicktime", false),
            [".mkv"] = ("video/x-matroska", false),
            [".avi"] = ("video/x-msvideo", false)
        };

    private static readonly IReadOnlyDictionary<string, (string Label, string Language)> Languages =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = ("English", "en"),
            ["eng"] = ("English", "en"),
            ["nb"] = ("Norsk bokmål", "nb"),
            ["no"] = ("Norsk", "no"),
            ["nor"] = ("Norsk", "no"),
            ["nn"] = ("Norsk nynorsk", "nn"),
            ["da"] = ("Dansk", "da"),
            ["sv"] = ("Svenska", "sv"),
            ["de"] = ("Deutsch", "de"),
            ["fr"] = ("Français", "fr"),
            ["es"] = ("Español", "es")
        };

    private readonly string _rootPath;
    private readonly string _mountSentinelFile;
    private readonly TimeSpan _scanInterval;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private MediaSnapshot? _snapshot;

    public MediaLibrary(IOptions<MediaLibraryOptions> options)
    {
        _rootPath = Path.GetFullPath(options.Value.RootPath);
        _mountSentinelFile = options.Value.MountSentinelFile;
        _scanInterval = TimeSpan.FromSeconds(options.Value.ScanIntervalSeconds);
    }

    public bool IsRootAvailable()
    {
        try
        {
            if (!Directory.Exists(_rootPath))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(_mountSentinelFile) &&
                !File.Exists(Path.Combine(_rootPath, _mountSentinelFile)))
            {
                return false;
            }

            using var enumerator = Directory.EnumerateFileSystemEntries(_rootPath).GetEnumerator();
            _ = enumerator.MoveNext();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal async Task<MediaSnapshot> GetSnapshotAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var current = _snapshot;
        if (!forceRefresh && current is not null && DateTimeOffset.UtcNow - current.ScannedAt < _scanInterval)
        {
            return current;
        }

        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            current = _snapshot;
            if (!forceRefresh && current is not null && DateTimeOffset.UtcNow - current.ScannedAt < _scanInterval)
            {
                return current;
            }

            _snapshot = Scan();
            return _snapshot;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    internal async Task<OpenedMediaFile?> OpenMediaAsync(string id, CancellationToken cancellationToken)
    {
        var media = await FindMetadataAsync(id, cancellationToken);
        if (media is null)
        {
            return null;
        }

        var stream = TryOpenCatalogFile(media.FullPath);
        return stream is null ? null : new OpenedMediaFile(media, stream);
    }

    internal async Task<OpenedSubtitleFile?> OpenSubtitleAsync(
        string mediaId,
        string subtitleId,
        CancellationToken cancellationToken)
    {
        var media = await FindMetadataAsync(mediaId, cancellationToken);
        var subtitle = media?.Subtitles.SingleOrDefault(track => track.Id == subtitleId);
        if (media is null || subtitle is null)
        {
            return null;
        }

        var stream = TryOpenCatalogFile(subtitle.FullPath);
        return stream is null ? null : new OpenedSubtitleFile(media, subtitle, stream);
    }

    private async Task<MediaFile?> FindMetadataAsync(string id, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(false, cancellationToken);
        var media = snapshot.Items.SingleOrDefault(item => item.Id == id);
        if (media is not null)
        {
            return media;
        }

        snapshot = await GetSnapshotAsync(true, cancellationToken);
        return snapshot.Items.SingleOrDefault(item => item.Id == id);
    }

    private MediaSnapshot Scan()
    {
        var scannedAt = DateTimeOffset.UtcNow;
        if (!IsRootAvailable())
        {
            return new MediaSnapshot([], false, scannedAt, ["The media root is not mounted."]);
        }

        var videoPaths = new List<string>();
        var subtitlePaths = new List<string>();
        var warnings = new List<string>();

        try
        {
            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
                MatchCasing = MatchCasing.CaseInsensitive
            };

            foreach (var path in Directory.EnumerateFiles(_rootPath, "*", enumerationOptions))
            {
                var extension = Path.GetExtension(path);
                if (VideoTypes.ContainsKey(extension))
                {
                    videoPaths.Add(path);
                }
                else if (extension.Equals(".srt", StringComparison.OrdinalIgnoreCase))
                {
                    subtitlePaths.Add(path);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add("Part of the media library could not be read.");
        }

        var subtitlesByDirectory = subtitlePaths
            .GroupBy(path => Path.GetDirectoryName(path) ?? _rootPath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var items = new List<MediaFile>(videoPaths.Count);
        foreach (var fullPath in videoPaths)
        {
            try
            {
                if (!IsSafeCatalogPath(fullPath))
                {
                    continue;
                }

                var relativePath = NormalizeRelativePath(fullPath);
                var fileInfo = new FileInfo(fullPath);
                var extension = fileInfo.Extension;
                var videoType = VideoTypes[extension];
                var directory = fileInfo.DirectoryName ?? _rootPath;
                subtitlesByDirectory.TryGetValue(directory, out var possibleSubtitles);
                var subtitles = FindSubtitles(fileInfo, possibleSubtitles ?? []);

                items.Add(new MediaFile(
                    CreateId(relativePath),
                    CreateTitle(Path.GetFileNameWithoutExtension(relativePath)),
                    relativePath,
                    GetCategory(relativePath),
                    fullPath,
                    fileInfo.Length,
                    fileInfo.LastWriteTimeUtc,
                    videoType.ContentType,
                    videoType.DirectPlayLikely,
                    subtitles));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add("A media file changed or became unavailable during the scan.");
            }
        }

        return new MediaSnapshot(
            items.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            true,
            scannedAt,
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private IReadOnlyList<SubtitleFile> FindSubtitles(FileInfo video, IReadOnlyList<string> candidates)
    {
        var videoStem = Path.GetFileNameWithoutExtension(video.Name);
        var tracks = new List<SubtitleFile>();

        foreach (var path in candidates)
        {
            if (!IsSafeCatalogPath(path))
            {
                continue;
            }

            var subtitleStem = Path.GetFileNameWithoutExtension(path);
            if (!subtitleStem.Equals(videoStem, StringComparison.OrdinalIgnoreCase) &&
                !subtitleStem.StartsWith(videoStem + ".", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffix = subtitleStem.Length == videoStem.Length
                ? string.Empty
                : subtitleStem[(videoStem.Length + 1)..];
            var languageCode = suffix.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "und";
            var (label, language) = GetLanguage(languageCode);
            var relativePath = NormalizeRelativePath(path);
            tracks.Add(new SubtitleFile(CreateId(relativePath), label, language, relativePath, path));
        }

        return tracks
            .OrderBy(track => track.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool IsSafeCatalogPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var rootWithSeparator = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            return false;
        }

        var relative = Path.GetRelativePath(_rootPath, fullPath);
        var current = _rootPath;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private FileStream? TryOpenCatalogFile(string fullPath)
    {
        try
        {
            var relativePath = Path.GetRelativePath(_rootPath, Path.GetFullPath(fullPath));
            var segments = relativePath.Split(
                Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 ||
                Path.IsPathRooted(relativePath) ||
                segments.Any(segment => segment is "." or ".."))
            {
                return null;
            }

            if (!OperatingSystem.IsLinux())
            {
                return IsSafeCatalogPath(fullPath)
                    ? new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read)
                    : null;
            }

            var rootSegments = _rootPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            using var directoryHandle = OpenDirectoryChain(rootSegments.Concat(segments[..^1]));
            var fileHandle = OpenAt(
                directoryHandle,
                segments[^1],
                OpenReadOnly | OpenNonBlocking | OpenNoFollow | OpenCloseOnExec);

            FileStream? stream = null;
            try
            {
                stream = new FileStream(fileHandle, FileAccess.Read, bufferSize: 64 * 1024, isAsync: false);
                if (!stream.CanSeek)
                {
                    stream.Dispose();
                    return null;
                }

                _ = stream.Length;
                return stream;
            }
            catch
            {
                if (stream is null)
                {
                    fileHandle.Dispose();
                }
                else
                {
                    stream.Dispose();
                }

                throw;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static SafeFileHandle OpenDirectoryChain(IEnumerable<string> segments)
    {
        var current = OpenAt(
            new SafeFileHandle(new IntPtr(AtFileDescriptorCurrentWorkingDirectory), ownsHandle: false),
            "/",
            OpenReadOnly | OpenDirectory | OpenNoFollow | OpenCloseOnExec);

        try
        {
            foreach (var segment in segments)
            {
                var next = OpenAt(
                    current,
                    segment,
                    OpenReadOnly | OpenDirectory | OpenNoFollow | OpenCloseOnExec);
                current.Dispose();
                current = next;
            }

            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    private static SafeFileHandle OpenAt(SafeFileHandle directoryHandle, string path, int flags)
    {
        var fileDescriptor = OpenAtNative(directoryHandle.DangerousGetHandle().ToInt32(), path, flags);
        if (fileDescriptor < 0)
        {
            throw new IOException($"Unable to open a catalog path (errno {Marshal.GetLastPInvokeError()}).");
        }

        return new SafeFileHandle(new IntPtr(fileDescriptor), ownsHandle: true);
    }

    private string NormalizeRelativePath(string fullPath) =>
        Path.GetRelativePath(_rootPath, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    private static string CreateId(string relativePath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(relativePath));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string CreateTitle(string fileStem)
    {
        var title = SeparatorsRegex().Replace(fileStem, " ");
        return WhitespaceRegex().Replace(title, " ").Trim();
    }

    private static string GetCategory(string relativePath)
    {
        var separator = relativePath.IndexOf('/');
        return separator > 0 ? relativePath[..separator] : "Unsorted";
    }

    private static (string Label, string Language) GetLanguage(string code)
    {
        if (Languages.TryGetValue(code, out var language))
        {
            return language;
        }

        return code.Equals("und", StringComparison.OrdinalIgnoreCase)
            ? ("Subtitles", "und")
            : (code.ToUpperInvariant(), code.ToLowerInvariant());
    }

    [GeneratedRegex("[._]+", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorsRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [DllImport("libc", EntryPoint = "openat", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern int OpenAtNative(int directoryFileDescriptor, string path, int flags);
}
