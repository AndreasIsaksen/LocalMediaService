namespace LocalMediaService.Tests;

internal sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Path = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "test-data",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateDirectory(params string[] segments)
    {
        var path = segments.Aggregate(Path, (current, segment) => System.IO.Path.Combine(current, segment));
        Directory.CreateDirectory(path);
        return path;
    }

    public string WriteText(string relativePath, string content)
    {
        var path = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
