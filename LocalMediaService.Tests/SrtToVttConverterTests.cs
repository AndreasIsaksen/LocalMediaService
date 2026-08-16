using LocalMediaService.Web.Services;

namespace LocalMediaService.Tests;

public sealed class SrtToVttConverterTests
{
    [Fact]
    public void Convert_NormalizesBomNewlinesAndTimestamps()
    {
        const string input = "\uFEFF1\r\n00:00:01,250 --> 00:00:03,900\r\nHello from SRT\r\n";

        var result = SrtToVttConverter.Convert(input);

        Assert.Equal("WEBVTT\n\n1\n00:00:01.250 --> 00:00:03.900\nHello from SRT\n", result);
    }
}
