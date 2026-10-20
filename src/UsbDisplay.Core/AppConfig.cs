using System.Text.Json;

namespace UsbDisplay.Core;

/// <summary>Tray configuration. Host names, ids and PromQL live in DefaultConfig.json, never in code.</summary>
public sealed class AppConfig
{
    public string VmUrl { get; set; } = "";
    public int RefreshSeconds { get; set; } = 10;
    public int TrendRefreshSeconds { get; set; } = 300;
    public int StaleSeconds { get; set; } = 120;
    public int Orientation { get; set; } = 2;
    public int Brightness { get; set; } = 100;
    public string Title { get; set; } = "";

    /// <summary>Storage rows turn amber / red when the days left fall under these (null = no color).</summary>
    public double? StorageDaysWarn { get; set; }

    public double? StorageDaysAlarm { get; set; }

    public List<SourceConfig> Sources { get; set; } = new();
    public List<LoadConfig> Load { get; set; } = new();
    public TrendTemplates Trends { get; set; } = new();
    public List<StorageConfig> Storage { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string DefaultJson()
    {
        using var s = typeof(AppConfig).Assembly.GetManifestResourceStream("UsbDisplay.Core.DefaultConfig.json")
            ?? throw new InvalidOperationException("embedded DefaultConfig.json is missing");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static AppConfig Parse(string json)
    {
        var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? throw new InvalidDataException("empty config");
        cfg.Validate();
        return cfg;
    }

    /// <summary>Loads the config file, creating it from the embedded default when it does not exist.</summary>
    public static AppConfig LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, DefaultJson());
        }
        return Parse(File.ReadAllText(path));
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(VmUrl)) throw new InvalidDataException("VmUrl is empty");
        if (RefreshSeconds < 2) throw new InvalidDataException("RefreshSeconds must be at least 2");
        if (Orientation is < 0 or > 3) throw new InvalidDataException("Orientation must be 0-3");
        if (Brightness is < 0 or > 100) throw new InvalidDataException("Brightness must be 0-100");
        var names = new HashSet<string>();
        foreach (var s in Sources)
        {
            if (!names.Add(s.Name)) throw new InvalidDataException($"duplicate source '{s.Name}'");
        }
        foreach (var l in Load)
        {
            if (!names.Contains(l.Source)) throw new InvalidDataException($"load '{l.Label}': unknown source '{l.Source}'");
        }
        foreach (var s in Storage)
        {
            if (!names.Contains(s.Source)) throw new InvalidDataException($"storage '{s.Label}': unknown source '{s.Source}'");
        }
    }
}

/// <summary>A data source and the query that returns the age in seconds of its newest sample.</summary>
public sealed class SourceConfig
{
    public string Name { get; set; } = "";
    public string AgeQuery { get; set; } = "";
}

public sealed class LoadConfig
{
    public string Label { get; set; } = "";
    public string Source { get; set; } = "";
    public string Query { get; set; } = "";
    public string? ExtraLabel { get; set; }
    public string? ExtraQuery { get; set; }
    public string? Note { get; set; }

    /// <summary>Percent thresholds for the bar color, inclusive (null = no color).</summary>
    public double? WarnAt { get; set; }

    public double? AlarmAt { get; set; }
}

/// <summary>Trend queries built from a storage row: {size} and {used} are replaced by its queries.</summary>
public sealed class TrendTemplates
{
    public string MinFree { get; set; } = "";
    public string Growth1d { get; set; } = "";
    public string Growth7d { get; set; } = "";

    public static string Expand(string template, StorageConfig s) =>
        template.Replace("{size}", s.SizeQuery).Replace("{used}", s.UsedQuery);
}

public sealed class StorageConfig
{
    public string Label { get; set; } = "";
    public string Source { get; set; } = "";
    public string SizeQuery { get; set; } = "";
    public string UsedQuery { get; set; } = "";
    public long? WarnFreeBytes { get; set; }
}
