namespace UsbDisplay.Core;

/// <summary>Evaluates the configured queries and builds one FrameData. Trend queries run every TrendRefreshSeconds.</summary>
public sealed class Collector : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly PromClient _prom;
    private readonly SemaphoreSlim _gate = new(6);
    private readonly Dictionary<string, (double? Min, double? G1, double? G7)> _trends = new();
    private DateTime _trendAtUtc = DateTime.MinValue;

    public Collector(AppConfig cfg, PromClient? prom = null)
    {
        _cfg = cfg;
        _prom = prom ?? new PromClient(cfg.VmUrl);
    }

    private async Task<Reading> Query(string promql, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(promql)) return Reading.Fail("no query");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await _prom.QueryAsync(promql, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FrameData> CollectAsync(DateTime nowUtc, CancellationToken ct)
    {
        var ages = _cfg.Sources.ToDictionary(s => s.Name, s => Query(s.AgeQuery, ct));
        var load = _cfg.Load
            .Select(l => (Cfg: l, Main: Query(l.Query, ct), Extra: l.ExtraQuery is null ? null : Query(l.ExtraQuery, ct)))
            .ToList();
        var storage = _cfg.Storage
            .Select(s => (Cfg: s, Size: Query(s.SizeQuery, ct), Used: Query(s.UsedQuery, ct)))
            .ToList();
        var trends = (nowUtc - _trendAtUtc).TotalSeconds >= _cfg.TrendRefreshSeconds
            ? _cfg.Storage.Select(s => (Cfg: s,
                Min: Query(TrendTemplates.Expand(_cfg.Trends.MinFree, s), ct),
                G1: Query(TrendTemplates.Expand(_cfg.Trends.Growth1d, s), ct),
                G7: Query(TrendTemplates.Expand(_cfg.Trends.Growth7d, s), ct))).ToList()
            : null;

        var all = ages.Values
            .Concat(load.SelectMany(x => new[] { x.Main, x.Extra }).OfType<Task<Reading>>())
            .Concat(storage.SelectMany(x => new[] { x.Size, x.Used }))
            .Concat(trends?.SelectMany(x => new[] { x.Min, x.G1, x.G7 }) ?? [])
            .ToList();
        await Task.WhenAll(all).ConfigureAwait(false);

        var readings = all.Select(t => t.Result).ToList();
        bool vmReachable = readings.Any(r => !r.Transport);
        string? error = vmReachable ? null : readings.Select(r => r.Error).FirstOrDefault(e => e is not null);

        if (trends is not null && trends.Any(x => !x.Min.Result.Transport))
        {
            foreach (var x in trends) _trends[x.Cfg.Label] = (x.Min.Result.Value, x.G1.Result.Value, x.G7.Result.Value);
            _trendAtUtc = nowUtc;
        }

        var fresh = ages.ToDictionary(kv => kv.Key, kv => StorageMath.IsFresh(kv.Value.Result.Value, _cfg.StaleSeconds));
        int ok = 0;

        var loadRows = new List<LoadRow>();
        foreach (var (cfg, main, extra) in load)
        {
            bool f = fresh.GetValueOrDefault(cfg.Source);
            double? v = f ? main.Result.Value : null;
            double? e = f && extra is not null ? extra.Result.Value : null;
            if (v.HasValue) ok++;
            if (e.HasValue) ok++;
            loadRows.Add(new LoadRow
            {
                Label = cfg.Label,
                Note = cfg.Note,
                ExtraLabel = cfg.ExtraLabel,
                Value = v,
                Extra = e,
                Level = Levels.ForPercent(v, cfg.WarnAt, cfg.AlarmAt),
            });
        }

        var storageRows = new List<StorageRow>();
        foreach (var (cfg, size, used) in storage)
        {
            bool f = fresh.GetValueOrDefault(cfg.Source);
            double? sz = size.Result.Value;
            double? free = f ? sz - used.Result.Value : null;
            if (free.HasValue) ok++;
            _trends.TryGetValue(cfg.Label, out var tr);
            var days = StorageMath.DaysLeft(free, tr.G1, tr.G7);
            storageRows.Add(new StorageRow
            {
                Label = cfg.Label,
                Size = f ? sz : null,
                Free = free,
                LowestFree7d = free.HasValue ? StorageMath.LowestFree(tr.Min, free) : null,
                DaysLeft = days,
                Growth1d = free.HasValue ? tr.G1 : null,
                Growth7d = free.HasValue ? tr.G7 : null,
                Level = Levels.ForStorage(free, days, cfg.WarnFreeBytes, _cfg.StorageDaysWarn, _cfg.StorageDaysAlarm),
            });
        }

        return new FrameData
        {
            TimeUtc = nowUtc,
            Title = _cfg.Title,
            VmReachable = vmReachable,
            StaleSources = fresh.Count(kv => !kv.Value),
            Load = loadRows,
            Storage = storageRows,
            ValuesOk = ok,
            Error = error,
        };
    }

    public void Dispose()
    {
        _prom.Dispose();
        _gate.Dispose();
    }
}
