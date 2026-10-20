using System.Globalization;

namespace UsbDisplay.Core;

public sealed class LoadRow
{
    public required string Label { get; init; }
    public string? Note { get; init; }
    public string? ExtraLabel { get; init; }

    /// <summary>Null when the query failed or its source is stale: never show an old number as current.</summary>
    public double? Value { get; init; }

    public double? Extra { get; init; }

    public Level Level { get; init; }
}

public sealed class StorageRow
{
    public required string Label { get; init; }
    public double? Size { get; init; }

    /// <summary>Absolute free bytes now; null when unknown or stale.</summary>
    public double? Free { get; init; }

    public double? LowestFree7d { get; init; }
    public double? DaysLeft { get; init; }

    /// <summary>Growth in bytes per day over the last 24 h; negative when shrinking, null when unknown.</summary>
    public double? Growth1d { get; init; }

    /// <summary>Growth in bytes per day over the last 7 days; negative when shrinking, null when unknown.</summary>
    public double? Growth7d { get; init; }

    public Level Level { get; init; }

    public double? FillRatio => Size is > 0 && Free.HasValue ? Math.Clamp(1 - Free.Value / Size.Value, 0, 1) : null;
}

/// <summary>How a value is colored: None = no threshold or no value, Ok = green, Warn = amber, Alarm = red.</summary>
public enum Level
{
    None,
    Ok,
    Warn,
    Alarm,
}

public static class Levels
{
    /// <summary>Percent value against its thresholds; both are inclusive (99 with AlarmAt 90 is Alarm).</summary>
    public static Level ForPercent(double? value, double? warnAt, double? alarmAt)
    {
        if (value is null || (warnAt is null && alarmAt is null)) return Level.None;
        if (alarmAt is not null && value.Value >= alarmAt.Value) return Level.Alarm;
        if (warnAt is not null && value.Value >= warnAt.Value) return Level.Warn;
        return Level.Ok;
    }

    /// <summary>A storage is judged on time to full (and an optional absolute floor), never on percent used.</summary>
    public static Level ForStorage(double? free, double? daysLeft, long? warnFreeBytes, double? daysWarn, double? daysAlarm)
    {
        if (free is null || (daysWarn is null && daysAlarm is null && warnFreeBytes is null)) return Level.None;
        if (warnFreeBytes is not null && free.Value < warnFreeBytes.Value) return Level.Alarm;
        if (daysLeft is not null && daysAlarm is not null && daysLeft.Value < daysAlarm.Value) return Level.Alarm;
        if (daysLeft is not null && daysWarn is not null && daysLeft.Value < daysWarn.Value) return Level.Warn;
        return Level.Ok;
    }
}

public sealed class FrameData
{
    public required DateTime TimeUtc { get; init; }
    public required string Title { get; init; }
    public bool VmReachable { get; init; }
    public int StaleSources { get; init; }
    public required IReadOnlyList<LoadRow> Load { get; init; }
    public required IReadOnlyList<StorageRow> Storage { get; init; }

    /// <summary>Fresh values on the frame: load values (main and extra) plus storage free values.</summary>
    public int ValuesOk { get; init; }

    public string? Error { get; init; }
}

public static class StorageMath
{
    public static bool IsFresh(double? ageSeconds, int staleSeconds) => ageSeconds.HasValue && ageSeconds.Value <= staleSeconds;

    /// <summary>Lowest free of the window. The hourly subquery lags the present, so the current value counts too.</summary>
    public static double? LowestFree(double? subqueryMin, double? freeNow) => (subqueryMin, freeNow) switch
    {
        (null, null) => null,
        (null, double f) => f,
        (double m, null) => m,
        (double m, double f) => Math.Min(m, f),
    };

    /// <summary>Days until full at the steeper positive growth (bytes/day); null when neither rate grows.</summary>
    public static double? DaysLeft(double? free, double? growthPerDay1d, double? growthPerDay7d)
    {
        if (free is null) return null;
        var g = Math.Max(growthPerDay1d ?? 0, growthPerDay7d ?? 0);
        if (g <= 0) return null;
        return Math.Max(0, free.Value) / g;
    }
}

public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Decimal units, as the storage APIs report them: 448G, 4.9T, 12T.</summary>
    public static string Bytes(double? b)
    {
        if (b is null) return "--";
        var v = b.Value;
        if (v >= 1e13) return (v / 1e12).ToString("0", Inv) + "T";
        if (v >= 1e12) return (v / 1e12).ToString("0.0", Inv) + "T";
        if (v >= 1e9) return (v / 1e9).ToString("0", Inv) + "G";
        return (v / 1e6).ToString("0", Inv) + "M";
    }

    /// <summary>Signed bytes per day: +106G/d, -12G/d, +0M/d. Empty when unknown.</summary>
    public static string Rate(double? bytesPerDay) =>
        bytesPerDay is null ? "" : (bytesPerDay.Value < 0 ? "-" : "+") + Bytes(Math.Abs(bytesPerDay.Value)) + "/d";

    /// <summary>
    /// The steeper of the two growth rates (the one that drives days-left; the least negative when
    /// both shrink) with its window: "+106G/d 24h" or "+393G/d 7d". Ties go to 24 h. Empty when both are unknown.
    /// </summary>
    public static string Growth(double? growth1d, double? growth7d)
    {
        if (growth1d is null && growth7d is null) return "";
        if (growth7d is null || (growth1d is not null && growth1d.Value >= growth7d.Value)) return Rate(growth1d) + " 24h";
        return Rate(growth7d) + " 7d";
    }

    public static string Percent(double? p) => p is null ? "--" : Math.Round(p.Value).ToString("0", Inv) + "%";

    public static string Number(double? n) => n is null ? "--" : n.Value.ToString(n.Value >= 100 ? "0" : "0.0", Inv);

    /// <summary>Whole days, rounded down so the warning is never late; hours under one day.</summary>
    public static string Days(double? d)
    {
        if (d is null) return "";
        if (d.Value < 1) return Math.Max(1, Math.Round(d.Value * 24)).ToString("0", Inv) + "h";
        if (d.Value > 99) return "99+d";
        return Math.Floor(d.Value).ToString("0", Inv) + "d";
    }
}
