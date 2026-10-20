using System.Diagnostics;
using UsbDisplay.Core;
using UsbDisplay.Win;

namespace UsbDisplayTray;

/// <summary>Tray icon + refresh loop: collect metrics, render one frame, send it to the panel, write status.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly ToolStripMenuItem _pause = new("Pause");
    private AppConfig _cfg;
    private Collector _collector;
    private volatile bool _brightnessPending = true;
    private bool _busy;
    private bool _paused;
    private string _status = "starting";

    public TrayContext()
    {
        _cfg = AppConfig.LoadOrCreate(Paths.ConfigFile);
        _collector = new Collector(_cfg);
        Status.SaveDefaultConfig(AppConfig.DefaultJson());

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh now", null, async (_, _) => await RefreshAsync());
        _pause.Click += (_, _) =>
        {
            _paused = !_paused;
            _pause.Text = _paused ? "Resume" : "Pause";
            ShowStatus(_paused ? "paused" : _status);
        };
        menu.Items.Add(_pause);
        var bright = new ToolStripMenuItem("Brightness");
        foreach (var pct in new[] { 25, 50, 75, 100 })
        {
            bright.DropDownItems.Add($"{pct} %", null, async (_, _) =>
            {
                _cfg.Brightness = pct;
                _brightnessPending = true;
                await RefreshAsync();
            });
        }
        menu.Items.Add(bright);
        menu.Items.Add("Open config", null, (_, _) => Process.Start(new ProcessStartInfo(Paths.ConfigFile) { UseShellExecute = true }));
        menu.Items.Add("Reload config", null, async (_, _) => await ReloadAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _icon = new NotifyIcon { Icon = TrayIcon.Create(), Text = "usb-display: starting", Visible = true, ContextMenuStrip = menu };
        _timer.Interval = _cfg.RefreshSeconds * 1000;
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_busy || _paused) return;
        _busy = true;
        try
        {
            var data = await _collector.CollectAsync(DateTime.UtcNow, CancellationToken.None);
            _status = await Task.Run(() => Draw(data));
        }
        catch (Exception e)
        {
            _status = "error: " + e.Message;
            TryWriteStatus(null, 0, 0, e.Message, false);
        }
        finally
        {
            _busy = false;
            ShowStatus(_paused ? "paused" : _status);
        }
    }

    private string Draw(FrameData data)
    {
        var (w, h) = PanelProtocol.Size(_cfg.Orientation);
        using var bmp = FrameRenderer.Render(data, w, h);
        Status.SaveFrame(bmp);
        var port = PanelIo.FindPort();
        if (port is null)
        {
            _brightnessPending = true; // a replugged panel starts from its own default
            Status.Write(null, data.ValuesOk, 0, data.Error ?? "panel absent", data.VmReachable);
            return "panel absent";
        }
        long sent;
        using (var sink = new PortSink(port))
        {
            if (_brightnessPending)
            {
                sink.Write(PanelProtocol.Brightness(_cfg.Brightness));
                _brightnessPending = false;
            }
            PanelIo.SendFrame(sink, bmp, _cfg.Orientation);
            sent = sink.Sent;
        }
        var alarms = string.Join(", ", data.Load.Where(r => r.Level == Level.Alarm).Select(r => r.Label)
            .Concat(data.Storage.Where(s => s.Level == Level.Alarm).Select(s => s.Label)));
        Status.Write(port, data.ValuesOk, sent, data.Error, data.VmReachable, alarms);
        return $"{port} - {DateTime.Now:HH:mm:ss}" + (data.VmReachable ? "" : " - VM unreachable") + (alarms.Length > 0 ? " - ALARM: " + alarms : "");
    }

    private async Task ReloadAsync()
    {
        try
        {
            var cfg = AppConfig.LoadOrCreate(Paths.ConfigFile);
            while (_busy) await Task.Delay(100);
            _collector.Dispose();
            _cfg = cfg;
            _collector = new Collector(_cfg);
            _timer.Interval = _cfg.RefreshSeconds * 1000;
            _brightnessPending = true;
            await RefreshAsync();
        }
        catch (Exception e)
        {
            ShowStatus("config error: " + e.Message);
        }
    }

    private static void TryWriteStatus(string? port, int ok, long sent, string? error, bool vm)
    {
        try
        {
            Status.Write(port, ok, sent, error, vm);
        }
        catch (IOException)
        {
            // the status file is best effort; the tooltip still shows the error
        }
    }

    private void ShowStatus(string text)
    {
        var t = "usb-display: " + text;
        _icon.Text = t.Length > 120 ? t[..120] : t;
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        _collector.Dispose();
        base.ExitThreadCore();
    }
}

internal static class TrayIcon
{
    /// <summary>A small screen glyph drawn at run time, so the repo carries no binary icon.</summary>
    public static Icon Create()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var frame = new SolidBrush(Color.FromArgb(63, 167, 255));
            using var screen = new SolidBrush(Color.FromArgb(16, 18, 24));
            using var bar = new SolidBrush(Color.FromArgb(63, 208, 122));
            g.FillRectangle(frame, 1, 3, 14, 10);
            g.FillRectangle(screen, 3, 5, 10, 6);
            g.FillRectangle(bar, 4, 8, 6, 2);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
