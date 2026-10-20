using UsbDisplay.Core;
using Xunit;

namespace UsbDisplay.Tests;

/// <summary>Vectors from docs/PROTOCOL.md, verified on the real panel 2026-10-19.</summary>
[Trait("Category", "Protocol")]
public class ProtocolTests
{
    [Theory]
    [InlineData(0, 0, 479, 319, "00 00 07 7D 3F C5")]
    [InlineData(0, 0, 319, 479, "00 00 04 FD DF C5")]
    public void BitmapHeader(int left, int top, int right, int bottom, string want) =>
        Assert.Equal(want, PanelProtocol.Hex(PanelProtocol.Header(PanelProtocol.CmdBitmap, left, top, right, bottom)));

    [Fact]
    public void BrightnessZeroIsDarkest() => Assert.Equal("3F C0 00 00 00 6E", PanelProtocol.Hex(PanelProtocol.Brightness(0)));

    [Fact]
    public void BrightnessHundredIsBrightest() => Assert.Equal("00 00 00 00 00 6E", PanelProtocol.Hex(PanelProtocol.Brightness(100)));

    [Fact]
    public void OrientationLandscape() =>
        Assert.Equal("00 00 00 00 00 79 66 01 E0 01 40 00 00 00 00 00", PanelProtocol.Hex(PanelProtocol.Orientation(2)));

    [Fact]
    public void OrientationPortrait() =>
        Assert.Equal("00 00 00 00 00 79 64 01 40 01 E0 00 00 00 00 00", PanelProtocol.Hex(PanelProtocol.Orientation(0)));

    [Fact]
    public void MirrorOff() =>
        Assert.Equal("00 00 00 00 00 7A 00 00 00 00 00 00 00 00 00 00", PanelProtocol.Hex(PanelProtocol.Mirror(0)));

    [Fact]
    public void Color565() =>
        Assert.Equal(new[] { 0xF800, 0x07E0, 0x001F, 0xFFFF },
            new[] { PanelProtocol.Color565(255, 0, 0), PanelProtocol.Color565(0, 255, 0), PanelProtocol.Color565(0, 0, 255), PanelProtocol.Color565(255, 255, 255) });

    [Fact]
    public void LandscapeFrameStreamLength()
    {
        var (w, h) = PanelProtocol.Size(2);
        long n = PanelProtocol.FramePrologue(2, w, h).Sum(p => (long)p.Length) + w * h * 2;
        Assert.Equal(307244, n);
    }

    [Fact]
    public void PrologueNeverTurnsTheScreenOff() =>
        Assert.DoesNotContain(PanelProtocol.FramePrologue(2, 480, 320), p => p[5] == PanelProtocol.CmdOff);
}
