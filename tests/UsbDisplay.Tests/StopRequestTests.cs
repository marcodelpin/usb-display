using UsbDisplay.Core;
using Xunit;

namespace UsbDisplay.Tests;

public class StopRequestTests
{
    private static readonly DateTime Started = new(2026, 10, 23, 8, 0, 0, DateTimeKind.Utc);

    private static string RequestWrittenAt(DateTime utc)
    {
        var path = Path.GetTempFileName();
        File.SetLastWriteTimeUtc(path, utc);
        return path;
    }

    [Fact]
    public void RequestWrittenAfterTheTrayStartedStopsIt()
    {
        var path = RequestWrittenAt(Started.AddMilliseconds(1));
        try { Assert.True(StopRequest.IsFor(path, Started)); } finally { File.Delete(path); }
    }

    [Fact]
    public void RequestLeftFromAnEarlierInstanceIsIgnored()
    {
        var path = RequestWrittenAt(Started.AddSeconds(-1));
        try { Assert.False(StopRequest.IsFor(path, Started)); } finally { File.Delete(path); }
    }

    [Fact]
    public void NoRequestFileMeansNoStop() =>
        Assert.False(StopRequest.IsFor(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), Started));
}
