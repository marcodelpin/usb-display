using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;

namespace UsbDisplay.Win;

/// <summary>Per-user locations: config in %APPDATA%, state (status.json, last-frame.png) in %LOCALAPPDATA%.</summary>
public static class Paths
{
    public static string ConfigFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "usb-display", "config.json");

    public static string StateDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "usb-display");
}

/// <summary>What scripts/probe-tray.ps1 reads: one JSON object rewritten atomically after every refresh.</summary>
public static class Status
{
    public static void Write(string? port, int valuesOk, long bytesSent, string? lastError, bool vmReachable, string alarms = "")
    {
        Directory.CreateDirectory(Paths.StateDir);
        var obj = new
        {
            time_utc = DateTime.UtcNow.ToString("o"),
            panel = port ?? "absent",
            values_ok = valuesOk,
            bytes_sent = bytesSent,
            vm_reachable = vmReachable,
            alarms,
            last_error = lastError ?? "",
        };
        var tmp = Path.Combine(Paths.StateDir, "status.json.tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(obj));
        File.Move(tmp, Path.Combine(Paths.StateDir, "status.json"), true);
    }

    /// <summary>Snapshot of the default this build ships: install-tray.ps1 replaces a config that still equals it.</summary>
    public static void SaveDefaultConfig(string json)
    {
        Directory.CreateDirectory(Paths.StateDir);
        File.WriteAllText(Path.Combine(Paths.StateDir, "default-config.json"), json);
    }

    public static void SaveFrame(Bitmap bmp)
    {
        Directory.CreateDirectory(Paths.StateDir);
        var tmp = Path.Combine(Paths.StateDir, "last-frame.png.tmp");
        bmp.Save(tmp, ImageFormat.Png);
        File.Move(tmp, Path.Combine(Paths.StateDir, "last-frame.png"), true);
    }
}
