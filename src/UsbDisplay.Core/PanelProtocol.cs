namespace UsbDisplay.Core;

/// <summary>Wire protocol of the Turing Smart Screen 3.5" rev A (docs/PROTOCOL.md).</summary>
public static class PanelProtocol
{
    public const string PnpId = "USB35INCHIPSV2";
    public const int BaudRate = 115200;
    public const int NativeWidth = 320;
    public const int NativeHeight = 480;

    public const byte CmdOff = 108;
    public const byte CmdOn = 109;
    public const byte CmdBrightness = 110;
    public const byte CmdOrientation = 121;
    public const byte CmdMirror = 122;
    public const byte CmdBitmap = 197;

    /// <summary>left/top/right/bottom as 10-bit fields packed MSB first, then the command byte.</summary>
    public static byte[] Header(byte code, int left, int top, int right, int bottom, int size = 6)
    {
        var b = new byte[size];
        b[0] = (byte)(left >> 2);
        b[1] = (byte)(((left & 3) << 6) + (top >> 4));
        b[2] = (byte)(((top & 0xF) << 4) + (right >> 6));
        b[3] = (byte)(((right & 0x3F) << 2) + (bottom >> 8));
        b[4] = (byte)(bottom & 0xFF);
        b[5] = code;
        return b;
    }

    /// <summary>Frame size as seen in an orientation: 0/1 portrait, 2/3 landscape.</summary>
    public static (int Width, int Height) Size(int orientation) =>
        orientation <= 1 ? (NativeWidth, NativeHeight) : (NativeHeight, NativeWidth);

    public static byte[] Orientation(int orientation)
    {
        if (orientation is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(orientation));
        var (w, h) = Size(orientation);
        var b = Header(CmdOrientation, 0, 0, 0, 0, 16);
        b[6] = (byte)(100 + orientation);
        b[7] = (byte)(w >> 8);
        b[8] = (byte)(w & 0xFF);
        b[9] = (byte)(h >> 8);
        b[10] = (byte)(h & 0xFF);
        return b;
    }

    public static byte[] Mirror(int mode)
    {
        var b = Header(CmdMirror, 0, 0, 0, 0, 16);
        b[6] = (byte)mode;
        return b;
    }

    /// <summary>The panel takes 0 = brightest, 255 = darkest (same mapping as the vendor UI).</summary>
    public static int BrightnessLevel(int percent)
    {
        if (percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        return 255 - percent * 255 / 100;
    }

    public static byte[] Brightness(int percent) => Header(CmdBrightness, BrightnessLevel(percent), 0, 0, 0);

    public static byte[] On() => Header(CmdOn, 0, 0, 0, 0);

    public static byte[] Off() => Header(CmdOff, 0, 0, 0, 0);

    public static byte[] BitmapWindow(int x, int y, int width, int height) =>
        Header(CmdBitmap, x, y, x + width - 1, y + height - 1);

    public static int Color565(int r, int g, int b) => ((r << 8) & 0xF800) | ((g << 3) & 0x7E0) | (b >> 3);

    /// <summary>Packets sent before the pixels of a full frame, in the order verified on the panel.</summary>
    public static IEnumerable<byte[]> FramePrologue(int orientation, int width, int height)
    {
        yield return On();
        yield return Mirror(0);
        yield return Orientation(orientation);
        yield return BitmapWindow(0, 0, width, height);
    }

    public static string Hex(byte[] bytes) => string.Join(' ', bytes.Select(x => x.ToString("X2")));
}
