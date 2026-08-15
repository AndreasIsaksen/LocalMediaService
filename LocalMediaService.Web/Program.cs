using System.Net;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var mediaRoot = Environment.GetEnvironmentVariable("MEDIA_ROOT");
if (string.IsNullOrWhiteSpace(mediaRoot))
{
    mediaRoot = "/media";
}

var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    ".mp4", ".mkv", ".webm", ".mov", ".avi"
};

app.MapGet("/", () => Results.Content("""
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Local Media Service</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 2rem; background: #0f172a; color: #e2e8f0; }
        h1, h2 { color: #f8fafc; }
        .platforms { display: flex; flex-wrap: wrap; gap: 0.75rem; margin-bottom: 2rem; }
        a.platform { text-decoration: none; color: #0f172a; background: #38bdf8; padding: 0.6rem 1rem; border-radius: 8px; font-weight: 600; }
        a.platform:hover { background: #7dd3fc; }
        .video-list { display: grid; gap: 0.75rem; }
        .video-card { background: #1e293b; border-radius: 8px; padding: 1rem; }
        .video-card h3 { margin-top: 0; }
        video { width: 100%; max-width: 900px; border-radius: 6px; background: black; }
    </style>
</head>
<body>
    <h1>Local Media Service</h1>
    <p>One place for your streaming platforms and local media playback.</p>

    <h2>Streaming platforms</h2>
    <div class="platforms">
        <a class="platform" href="https://www.netflix.com" target="_blank" rel="noreferrer">Netflix</a>
        <a class="platform" href="https://www.primevideo.com" target="_blank" rel="noreferrer">Prime Video</a>
        <a class="platform" href="https://www.disneyplus.com" target="_blank" rel="noreferrer">Disney+</a>
        <a class="platform" href="https://tv.apple.com" target="_blank" rel="noreferrer">Apple TV+</a>
        <a class="platform" href="https://www.max.com" target="_blank" rel="noreferrer">Max</a>
    </div>

    <h2>Local videos</h2>
    <p>Mount your media library into <code>/media</code> (or set <code>MEDIA_ROOT</code>) in Docker.</p>
    <div id="videos" class="video-list">Loading videos...</div>

    <script>
        const container = document.getElementById('videos');
        fetch('/api/videos')
            .then(r => r.json())
            .then(videos => {
                if (!videos.length) {
                    container.innerHTML = '<div class="video-card">No local videos found.</div>';
                    return;
                }

                container.innerHTML = videos.map(v => {
                    const safeTitle = v.replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
                    const encodedPath = v.split('/').map(encodeURIComponent).join('/');
                    return `
                        <div class="video-card">
                            <h3>${safeTitle}</h3>
                            <video controls preload="metadata" src="/videos/${encodedPath}"></video>
                        </div>`;
                }).join('');
            })
            .catch(() => {
                container.innerHTML = '<div class="video-card">Unable to load videos.</div>';
            });
    </script>
</body>
</html>
""", "text/html"));

app.MapGet("/api/videos", () =>
{
    if (!Directory.Exists(mediaRoot))
    {
        return Results.Ok(Array.Empty<string>());
    }

    var files = Directory
        .EnumerateFiles(mediaRoot, "*.*", SearchOption.AllDirectories)
        .Where(path => allowedExtensions.Contains(Path.GetExtension(path)))
        .Select(path => Path.GetRelativePath(mediaRoot, path).Replace('\\', '/'))
        .OrderBy(path => path)
        .ToArray();

    return Results.Ok(files);
});

app.MapGet("/videos/{**filePath}", (string filePath) =>
{
    if (string.IsNullOrWhiteSpace(filePath))
    {
        return Results.NotFound();
    }

    var decodedPath = WebUtility.UrlDecode(filePath);
    var fullPath = Path.GetFullPath(Path.Combine(mediaRoot, decodedPath));
    var fullRootPath = Path.GetFullPath(mediaRoot);
    var fullRootPathWithSeparator = fullRootPath.EndsWith(Path.DirectorySeparatorChar)
        ? fullRootPath
        : fullRootPath + Path.DirectorySeparatorChar;

    if (!fullPath.StartsWith(fullRootPathWithSeparator, StringComparison.Ordinal))
    {
        return Results.BadRequest("Invalid path.");
    }

    if (!File.Exists(fullPath) || !allowedExtensions.Contains(Path.GetExtension(fullPath)))
    {
        return Results.NotFound();
    }

    return Results.File(fullPath, enableRangeProcessing: true);
});

app.Run();
