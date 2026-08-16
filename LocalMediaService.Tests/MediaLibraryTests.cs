using LocalMediaService.Web.Options;
using LocalMediaService.Web.Services;
using Microsoft.Extensions.Options;

namespace LocalMediaService.Tests;

public sealed class MediaLibraryTests
{
    [Fact]
    public async Task Scan_FindsNestedVideoAndMatchingLanguageSubtitles()
    {
        using var directory = new TestDirectory();
        directory.WriteText("Movies/Arrival (2016)/Arrival (2016).mp4", "video-data");
        directory.WriteText("Movies/Arrival (2016)/Arrival (2016).en.srt", "subtitle");
        directory.WriteText("Movies/Arrival (2016)/Different.srt", "unrelated");
        var library = CreateLibrary(directory.Path);

        var snapshot = await library.GetSnapshotAsync(true, CancellationToken.None);

        var item = Assert.Single(snapshot.Items);
        Assert.Equal("Arrival (2016)", item.Title);
        Assert.Equal("Movies", item.Category);
        Assert.Equal("video/mp4", item.ContentType);
        Assert.True(item.DirectPlayLikely);
        var subtitle = Assert.Single(item.Subtitles);
        Assert.Equal("English", subtitle.Label);
        Assert.Equal("en", subtitle.Language);
        Assert.DoesNotContain("Arrival", item.Id);

        var opened = await library.OpenMediaAsync(item.Id, CancellationToken.None);
        Assert.NotNull(opened);
        await using (opened.Stream)
        {
            using var reader = new StreamReader(opened.Stream);
            Assert.Equal("video-data", await reader.ReadToEndAsync());
        }
    }

    [Fact]
    public async Task Scan_ReportsMissingRootWithoutThrowing()
    {
        using var directory = new TestDirectory();
        var missing = System.IO.Path.Combine(directory.Path, "not-mounted");
        var library = CreateLibrary(missing);

        var snapshot = await library.GetSnapshotAsync(true, CancellationToken.None);

        Assert.False(snapshot.RootAvailable);
        Assert.Empty(snapshot.Items);
    }

    [Fact]
    public async Task Scan_RequiresConfiguredMountSentinel()
    {
        using var directory = new TestDirectory();
        directory.WriteText("Movies/Film.mp4", "video-data");
        var library = new MediaLibrary(Options.Create(new MediaLibraryOptions
        {
            RootPath = directory.Path,
            MountSentinelFile = ".local-media-volume",
            ScanIntervalSeconds = 30
        }));

        var withoutSentinel = await library.GetSnapshotAsync(true, CancellationToken.None);
        directory.WriteText(".local-media-volume", "mounted");
        var withSentinel = await library.GetSnapshotAsync(true, CancellationToken.None);

        Assert.False(withoutSentinel.RootAvailable);
        Assert.Empty(withoutSentinel.Items);
        Assert.True(withSentinel.RootAvailable);
        Assert.Single(withSentinel.Items);
    }

    [Fact]
    public async Task Scan_IgnoresUnsupportedFilesAndSymbolicLinks()
    {
        using var directory = new TestDirectory();
        directory.WriteText("Movies/readme.txt", "not video");
        var outside = directory.WriteText("outside.mp4", "outside");
        var movies = directory.CreateDirectory("Movies");
        var symlink = System.IO.Path.Combine(movies, "linked.mp4");
        File.CreateSymbolicLink(symlink, outside);
        var library = CreateLibrary(movies);

        var snapshot = await library.GetSnapshotAsync(true, CancellationToken.None);

        Assert.Empty(snapshot.Items);
    }

    [Fact]
    public async Task Find_RejectsFileReplacedBySymlinkAfterScan()
    {
        using var directory = new TestDirectory();
        var libraryRoot = directory.CreateDirectory("Library");
        var video = directory.WriteText("Library/Film.mp4", "original-video");
        var outside = directory.WriteText("outside.mp4", "outside-data");
        var library = CreateLibrary(libraryRoot);
        var snapshot = await library.GetSnapshotAsync(true, CancellationToken.None);
        var id = Assert.Single(snapshot.Items).Id;

        File.Delete(video);
        File.CreateSymbolicLink(video, outside);
        var found = await library.OpenMediaAsync(id, CancellationToken.None);

        Assert.Null(found);
    }

    private static MediaLibrary CreateLibrary(string rootPath) => new(Options.Create(new MediaLibraryOptions
    {
        RootPath = rootPath,
        MountSentinelFile = string.Empty,
        ScanIntervalSeconds = 30
    }));
}
