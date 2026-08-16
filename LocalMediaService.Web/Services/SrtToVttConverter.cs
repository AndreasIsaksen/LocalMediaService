using System.Text.RegularExpressions;

namespace LocalMediaService.Web.Services;

public static partial class SrtToVttConverter
{
    public static async Task<string> ConvertFileAsync(string path, CancellationToken cancellationToken)
    {
        var srt = await File.ReadAllTextAsync(path, cancellationToken);
        return Convert(srt);
    }

    public static async Task<string> ConvertStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            stream,
            System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);
        var srt = await reader.ReadToEndAsync(cancellationToken);
        return Convert(srt);
    }

    public static string Convert(string srt)
    {
        var normalized = srt.TrimStart('\uFEFF')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        var webVttBody = TimestampRegex().Replace(normalized, "$1.$2");
        return $"WEBVTT\n\n{webVttBody}\n";
    }

    [GeneratedRegex(@"(\d{2}:\d{2}:\d{2}),(\d{3})", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampRegex();
}
