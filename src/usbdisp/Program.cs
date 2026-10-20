// usbdisp - command line for the USB panel.
//   usbdisp detect | on | off | brightness <0-100> | orientation [n] | test [n] | image <path> [n] | frame | selftest
//   options: --port COMn (skip detection), --dry-run [--out <file>] (bytes to a file, not the port),
//            --png <path> (frame: also save the rendered frame)
// Orientation n: 0 portrait, 1 reverse portrait, 2 landscape (default), 3 reverse landscape.
// The tray redraws every RefreshSeconds: pause it from its menu to keep what this tool draws.
using System.Drawing;
using UsbDisplay.Core;
using UsbDisplay.Win;

var rest = args.ToList();
string? portOpt = TakeOption(rest, "--port");
string? outOpt = TakeOption(rest, "--out");
string? pngOpt = TakeOption(rest, "--png");
bool dryRun = rest.Remove("--dry-run");
if (rest.Count == 0) return Usage();
string verb = rest[0];

switch (verb)
{
    case "detect":
    {
        var p = PanelIo.FindPort();
        if (p is null)
        {
            Console.Error.WriteLine($"panel {PanelProtocol.PnpId} not found");
            return 2;
        }
        Console.WriteLine(p);
        return 0;
    }
    case "selftest":
        return SelfTest();
    case "on" or "off" or "brightness" or "orientation" or "test" or "image" or "frame":
        break;
    default:
        return Usage();
}

int orientation = 2;
int pct = 0;
if (verb == "brightness" && (rest.Count < 2 || !int.TryParse(rest[1], out pct) || pct is < 0 or > 100))
{
    Console.Error.WriteLine("brightness needs a percent 0-100");
    return 2;
}
if (verb == "image" && rest.Count < 2)
{
    Console.Error.WriteLine("image needs a file path");
    return 2;
}
int oIndex = verb == "image" ? 2 : 1;
if (verb is "orientation" or "test" or "image" && rest.Count > oIndex)
{
    if (!int.TryParse(rest[oIndex], out orientation) || orientation is < 0 or > 3)
    {
        Console.Error.WriteLine("orientation must be 0-3");
        return 2;
    }
}

Bitmap? frame = null;
FrameData? data = null;
var (w, h) = PanelProtocol.Size(orientation);
if (verb == "test") frame = FrameRenderer.TestPattern(w, h, orientation, Environment.MachineName);
if (verb == "image") frame = FrameRenderer.FitImage(rest[1], w, h);
if (verb == "frame")
{
    var cfg = AppConfig.LoadOrCreate(Paths.ConfigFile);
    orientation = cfg.Orientation;
    (w, h) = PanelProtocol.Size(orientation);
    using var collector = new Collector(cfg);
    data = collector.CollectAsync(DateTime.UtcNow, CancellationToken.None).GetAwaiter().GetResult();
    frame = FrameRenderer.Render(data, w, h);
    if (pngOpt is not null) frame.Save(pngOpt, System.Drawing.Imaging.ImageFormat.Png);
}

IPanelSink sink;
string sinkName;
if (dryRun)
{
    sink = new BufferSink();
    sinkName = "dry-run";
}
else
{
    var port = portOpt ?? PanelIo.FindPort();
    if (port is null)
    {
        Console.Error.WriteLine($"panel {PanelProtocol.PnpId} not found");
        return 2;
    }
    sink = new PortSink(port);
    sinkName = port;
}

try
{
    switch (verb)
    {
        case "on": sink.Write(PanelProtocol.On()); break;
        case "off": sink.Write(PanelProtocol.Off()); break;
        case "brightness": sink.Write(PanelProtocol.Brightness(pct)); break;
        case "orientation":
            sink.Write(PanelProtocol.Mirror(0));
            sink.Write(PanelProtocol.Orientation(orientation));
            break;
        default:
            PanelIo.SendFrame(sink, frame!, orientation);
            break;
    }
}
finally
{
    (sink as IDisposable)?.Dispose();
    frame?.Dispose();
}

if (data is not null) Console.WriteLine($"values_ok={data.ValuesOk} vm_reachable={data.VmReachable.ToString().ToLowerInvariant()}");
if (sink is BufferSink buffer)
{
    var path = outOpt ?? Path.Combine(Path.GetTempPath(), "usbdisp-dryrun.bin");
    File.WriteAllBytes(path, buffer.ToArray());
    Console.WriteLine($"dry-run: {verb} bytes={buffer.Sent} file={path}");
}
else
{
    Console.WriteLine($"{sinkName}: {verb} bytes={sink.Sent}");
}
return 0;

static int Usage()
{
    Console.Error.WriteLine("usage: usbdisp detect|on|off|brightness <0-100>|orientation [n]|test [n]|image <path> [n]|frame|selftest");
    Console.Error.WriteLine("       [--port COMn] [--dry-run [--out <file>]] [--png <path>]");
    return 2;
}

static string? TakeOption(List<string> list, string name)
{
    int i = list.IndexOf(name);
    if (i < 0 || i + 1 >= list.Count) return null;
    var v = list[i + 1];
    list.RemoveRange(i, 2);
    return v;
}

// Expected vectors are worked out by hand from the packing formula and match what the panel accepted.
static int SelfTest()
{
    int pass = 0, fail = 0;
    void Check(string name, string got, string want)
    {
        if (got == want)
        {
            pass++;
            Console.WriteLine($"PASS {name}");
        }
        else
        {
            fail++;
            Console.WriteLine($"FAIL {name} got [{got}] want [{want}]");
        }
    }

    Check("bitmap header 480x320", PanelProtocol.Hex(PanelProtocol.Header(PanelProtocol.CmdBitmap, 0, 0, 479, 319)), "00 00 07 7D 3F C5");
    Check("bitmap header 320x480", PanelProtocol.Hex(PanelProtocol.Header(PanelProtocol.CmdBitmap, 0, 0, 319, 479)), "00 00 04 FD DF C5");
    Check("brightness 0 pct", PanelProtocol.Hex(PanelProtocol.Brightness(0)), "3F C0 00 00 00 6E");
    Check("brightness 100 pct", PanelProtocol.Hex(PanelProtocol.Brightness(100)), "00 00 00 00 00 6E");
    Check("orientation landscape", PanelProtocol.Hex(PanelProtocol.Orientation(2)), "00 00 00 00 00 79 66 01 E0 01 40 00 00 00 00 00");
    Check("orientation portrait", PanelProtocol.Hex(PanelProtocol.Orientation(0)), "00 00 00 00 00 79 64 01 40 01 E0 00 00 00 00 00");
    Check("mirror off", PanelProtocol.Hex(PanelProtocol.Mirror(0)), "00 00 00 00 00 7A 00 00 00 00 00 00 00 00 00 00");
    Check("color565", string.Join(' ', new[] { PanelProtocol.Color565(255, 0, 0), PanelProtocol.Color565(0, 255, 0), PanelProtocol.Color565(0, 0, 255), PanelProtocol.Color565(255, 255, 255) }.Select(c => c.ToString("X4"))), "F800 07E0 001F FFFF");

    using (var bmp = new Bitmap(2, 1))
    {
        bmp.SetPixel(0, 0, Color.Red);
        bmp.SetPixel(1, 0, Color.Blue);
        Check("rgb565 little endian (GDI+)", PanelProtocol.Hex(PanelIo.Rgb565(bmp)), "00 F8 1F 00");
    }
    var (lw, lh) = PanelProtocol.Size(2);
    var sink = new BufferSink();
    using (var pattern = FrameRenderer.TestPattern(lw, lh, 2, "selftest")) PanelIo.SendFrame(sink, pattern, 2);
    Check("landscape frame stream length", sink.Sent.ToString(), (6 + 16 + 16 + 6 + 480 * 320 * 2).ToString());

    Console.WriteLine($"selftest: {pass} passed, {fail} failed");
    return fail > 0 ? 1 : 0;
}
