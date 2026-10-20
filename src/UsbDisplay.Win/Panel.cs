using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Ports;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UsbDisplay.Core;

namespace UsbDisplay.Win;

/// <summary>Where the panel bytes go: the serial port, or a buffer for a dry run.</summary>
public interface IPanelSink
{
    long Sent { get; }

    void Write(byte[] data, int delayMs = 5);
}

public sealed class BufferSink : IPanelSink
{
    private readonly MemoryStream _ms = new();

    public long Sent => _ms.Length;

    public void Write(byte[] data, int delayMs = 5) => _ms.Write(data);

    public byte[] ToArray() => _ms.ToArray();
}

/// <summary>The panel serial port, opened for one operation and closed after it (never with command 108).</summary>
public sealed class PortSink : IPanelSink, IDisposable
{
    private readonly SerialPort _sp;

    public string PortName { get; }

    public long Sent { get; private set; }

    public PortSink(string portName, int attempts = 5)
    {
        PortName = portName;
        _sp = new SerialPort(portName, PanelProtocol.BaudRate, Parity.None, 8, StopBits.One)
        {
            DtrEnable = true,
            RtsEnable = true,
            WriteTimeout = 15000,
            ReadTimeout = 1000,
        };
        for (int i = 1; ; i++)
        {
            try
            {
                _sp.Open();
                return;
            }
            catch (UnauthorizedAccessException) when (i < attempts)
            {
                Thread.Sleep(500); // another writer (tray or CLI) holds the port for one frame
            }
        }
    }

    public void Write(byte[] data, int delayMs = 5)
    {
        _sp.Write(data, 0, data.Length);
        Sent += data.Length;
        if (delayMs > 0) Thread.Sleep(delayMs);
    }

    public void Dispose()
    {
        if (_sp.IsOpen) _sp.Close();
        _sp.Dispose();
    }
}

public static class PanelIo
{
    /// <summary>COM port of the panel, found by its PNP id; null when it is not plugged in.</summary>
    public static string? FindPort()
    {
        using var searcher = new ManagementObjectSearcher(
            $"SELECT Name, DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE '%{PanelProtocol.PnpId}%'");
        foreach (var mo in searcher.Get().Cast<ManagementObject>())
        {
            using (mo)
            {
                var m = Regex.Match(mo["Name"] as string ?? "", @"\((COM\d+)\)");
                if (m.Success) return m.Groups[1].Value;
            }
        }
        return null;
    }

    /// <summary>Screen on, mirror off, orientation, bitmap window, then the pixels (sequence verified on the panel).</summary>
    public static void SendFrame(IPanelSink sink, Bitmap bmp, int orientation)
    {
        var px = Rgb565(bmp);
        foreach (var p in PanelProtocol.FramePrologue(orientation, bmp.Width, bmp.Height)) sink.Write(p, 5);
        sink.Write(px, 10);
    }

    /// <summary>RGB565 little endian, rows top to bottom, through the GDI+ format conversion.</summary>
    public static byte[] Rgb565(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format16bppRgb565);
        try
        {
            var px = new byte[w * h * 2];
            if (data.Stride == w * 2)
            {
                Marshal.Copy(data.Scan0, px, 0, px.Length);
            }
            else
            {
                for (int y = 0; y < h; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), px, y * w * 2, w * 2);
            }
            return px;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
