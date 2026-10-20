using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using UsbDisplay.Core;

namespace UsbDisplay.Win;

/// <summary>Draws frames for the 480x320 landscape panel with GDI+. No font under 13 px: it is a 3.5 inch screen.</summary>
public static class FrameRenderer
{
    private static readonly Color Bg = Color.FromArgb(16, 18, 24);
    private static readonly Color Header = Color.FromArgb(30, 34, 44);
    private static readonly Color Fg = Color.FromArgb(232, 232, 232);
    private static readonly Color Dim = Color.FromArgb(146, 152, 162);
    private static readonly Color Track = Color.FromArgb(44, 49, 60);
    private static readonly Color LoadFill = Color.FromArgb(63, 167, 255);
    private static readonly Color DiskFill = Color.FromArgb(118, 150, 196);
    private static readonly Color Amber = Color.FromArgb(255, 176, 32);
    private static readonly Color Red = Color.FromArgb(255, 77, 77);
    private static readonly Color Green = Color.FromArgb(63, 208, 122);

    public static Bitmap Render(FrameData d, int w, int h)
    {
        var bmp = new Bitmap(w, h);
        using var g = Prepare(bmp);
        using var fLabel = new Font("Segoe UI", 15, FontStyle.Regular, GraphicsUnit.Pixel);
        using var fValue = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
        using var fSmall = new Font("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);
        using var fTitle = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);

        Fill(g, Header, 0, 0, w, 26);
        var dot = !d.VmReachable ? Red : d.StaleSources > 0 ? Amber : Green;
        using (var b = new SolidBrush(dot)) g.FillEllipse(b, 8, 8, 11, 11);
        Text(g, d.VmReachable ? d.Title : "VictoriaMetrics unreachable", fTitle, d.VmReachable ? Fg : Red, 26, 3, w - 140, StringAlignment.Near);
        Text(g, d.TimeUtc.ToLocalTime().ToString("HH:mm:ss"), fTitle, Fg, w - 110, 3, 102, StringAlignment.Far);

        int y = 31;
        foreach (var r in d.Load)
        {
            Text(g, r.Label, fLabel, Dim, 8, y, 82, StringAlignment.Near);
            Bar(g, 92, y + 6, 210, 12, r.Value / 100.0, LevelFill(r.Level, LoadFill));
            string tail = r.ExtraLabel is not null ? $"  {r.ExtraLabel} {Fmt.Number(r.Extra)}" : r.Note is not null ? $"  {r.Note}" : "";
            Text(g, Fmt.Percent(r.Value) + tail, fValue, r.Value is null ? Amber : LevelText(r.Level), 306, y, w - 314, StringAlignment.Far);
            y += 24;
        }

        using (var p = new Pen(Track)) g.DrawLine(p, 6, y + 2, w - 6, y + 2);
        y += 8;

        int rowH = d.Storage.Count == 0 ? 26 : Math.Max(22, (h - y - 2) / d.Storage.Count);
        foreach (var s in d.Storage)
        {
            Text(g, s.Label, fLabel, Dim, 8, y + 2, 86, StringAlignment.Near);
            Bar(g, 96, y + 9, 84, 10, s.FillRatio, LevelFill(s.Level, DiskFill));
            Text(g, Fmt.Bytes(s.Free), fValue, s.Free is null ? Amber : LevelText(s.Level), 182, y + 1, 52, StringAlignment.Far);
            Text(g, s.LowestFree7d is null ? "" : "min " + Fmt.Bytes(s.LowestFree7d), fSmall, Dim, 238, y + 3, 66, StringAlignment.Near);
            // 114 px: "+104G/d 24h" was cut to "+104G/d 2..." at 84 px on the panel (2026-10-20).
            Text(g, Fmt.Growth(s.Growth1d, s.Growth7d), fSmall, Dim, 308, y + 3, 114, StringAlignment.Near);
            Text(g, Fmt.Days(s.DaysLeft), fValue, LevelText(s.Level), 424, y + 1, w - 432, StringAlignment.Far);
            y += rowH;
        }
        return bmp;
    }

    /// <summary>Color bars and text, to check orientation and colors by eye.</summary>
    public static Bitmap TestPattern(int w, int h, int orientation, string host)
    {
        var bmp = new Bitmap(w, h);
        using var g = Prepare(bmp);
        string[] bars = { "Red", "Lime", "Blue", "Yellow", "Cyan", "Magenta", "White", "Black" };
        int bw = w / bars.Length, bh = h / 4;
        for (int i = 0; i < bars.Length; i++) Fill(g, Color.FromName(bars[i]), i * bw, 0, bw, bh);
        using (var p = new Pen(Color.White)) g.DrawRectangle(p, 0, 0, w - 1, h - 1);
        using var big = new Font("Segoe UI", 28, FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font("Consolas", 16, FontStyle.Regular, GraphicsUnit.Pixel);
        int y = bh + 12;
        Text(g, "usbdisp test", big, Color.White, 12, y, w - 24, StringAlignment.Near);
        Text(g, $"{w}x{h}  orientation {orientation}", small, Color.LightGray, 14, y + 46, w - 28, StringAlignment.Near);
        Text(g, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), small, Color.Gold, 14, y + 70, w - 28, StringAlignment.Near);
        Text(g, host, small, Color.LightGray, 14, y + 94, w - 28, StringAlignment.Near);
        int dd = Math.Min(w, h) / 4;
        using (var b = new SolidBrush(Color.OrangeRed)) g.FillEllipse(b, w - dd - 16, h - dd - 16, dd, dd);
        return bmp;
    }

    /// <summary>An image file scaled to fit, centered on black.</summary>
    public static Bitmap FitImage(string path, int w, int h)
    {
        using var src = Image.FromFile(path);
        double scale = Math.Min((double)w / src.Width, (double)h / src.Height);
        int dw = (int)Math.Round(src.Width * scale), dh = (int)Math.Round(src.Height * scale);
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, (w - dw) / 2, (h - dh) / 2, dw, dh);
        return bmp;
    }

    /// <summary>Bar color: green / amber / red by level; the neutral color when the row has no thresholds.</summary>
    private static Color LevelFill(Level level, Color neutral) => level switch
    {
        Level.Ok => Green,
        Level.Warn => Amber,
        Level.Alarm => Red,
        _ => neutral,
    };

    private static Color LevelText(Level level) => level switch
    {
        Level.Warn => Amber,
        Level.Alarm => Red,
        _ => Fg,
    };

    private static Graphics Prepare(Bitmap bmp)
    {
        var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Bg);
        return g;
    }

    private static void Fill(Graphics g, Color c, int x, int y, int w, int h)
    {
        using var b = new SolidBrush(c);
        g.FillRectangle(b, x, y, w, h);
    }

    private static void Bar(Graphics g, int x, int y, int width, int height, double? ratio, Color fill)
    {
        Fill(g, Track, x, y, width, height);
        if (ratio is null) return;
        int fw = (int)Math.Round(Math.Clamp(ratio.Value, 0, 1) * width);
        if (fw > 0) Fill(g, fill, x, y, fw, height);
    }

    private static void Text(Graphics g, string text, Font f, Color c, int x, int y, int width, StringAlignment align)
    {
        using var b = new SolidBrush(c);
        using var sf = new StringFormat
        {
            Alignment = align,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        g.DrawString(text, f, b, new RectangleF(x, y, width, f.Height + 4), sf);
    }
}
