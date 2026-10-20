using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using UsbDisplay.Core;
using Xunit;

namespace UsbDisplay.Tests;

/// <summary>
/// Golden master of the observable outputs of UsbDisplay.Core: protocol bytes, formatted strings,
/// levels, storage math, PromQL parsing, config validation and collector frames. Any change in the
/// dump is a behavior change. Regenerate on purpose only: GOLDEN_UPDATE=1 dotnet test.
/// </summary>
public class GoldenMasterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string GoldenPath([CallerFilePath] string self = "") =>
        Path.Combine(Path.GetDirectoryName(self)!, "golden", "golden-master.txt");

    [Fact]
    public void ObservableOutputsMatchTheGoldenMaster()
    {
        var got = Dump();
        var path = GoldenPath();
        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, got);
        }
        Assert.Equal(File.ReadAllText(path), got);
    }

    private static string D(double? v) => v is null ? "null" : v.Value.ToString("R", Inv);

    private static string Try(Func<string> f)
    {
        try
        {
            return f();
        }
        catch (Exception e)
        {
            return $"!{e.GetType().Name}: {e.Message}";
        }
    }

    /// <summary>The dump is culture-neutral: record ToString() formats numbers with the current
    /// culture (1,5 under it-IT), so the whole dump runs under the invariant culture.</summary>
    internal static string Dump()
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = Inv;
        try
        {
            return DumpCore();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private static string DumpCore()
    {
        var sb = new StringBuilder();
        void L(string s) => sb.Append(s).Append('\n');

        L("== protocol");
        int[] coords = { 0, 1, 2, 3, 63, 64, 255, 256, 319, 320, 479, 511, 1023 };
        foreach (var a in coords)
        {
            foreach (var b in coords)
            {
                L($"header {a} {b} {1023 - a} {b ^ 0x155} = {PanelProtocol.Hex(PanelProtocol.Header(PanelProtocol.CmdBitmap, a, b, 1023 - a, b ^ 0x155))}");
            }
        }
        L($"header size16 = {PanelProtocol.Hex(PanelProtocol.Header(7, 1, 2, 3, 4, 16))}");
        for (int o = -1; o <= 4; o++)
        {
            int oo = o;
            L($"size {o} = {PanelProtocol.Size(o)}");
            L($"orientation {o} = {Try(() => PanelProtocol.Hex(PanelProtocol.Orientation(oo)))}");
        }
        for (int m = 0; m <= 2; m++) L($"mirror {m} = {PanelProtocol.Hex(PanelProtocol.Mirror(m))}");
        for (int p = -1; p <= 101; p++)
        {
            int pp = p;
            L($"brightness {p} = {Try(() => PanelProtocol.BrightnessLevel(pp).ToString(Inv))} {Try(() => PanelProtocol.Hex(PanelProtocol.Brightness(pp)))}");
        }
        L($"on = {PanelProtocol.Hex(PanelProtocol.On())}");
        L($"off = {PanelProtocol.Hex(PanelProtocol.Off())}");
        L($"window = {PanelProtocol.Hex(PanelProtocol.BitmapWindow(10, 20, 100, 50))}");
        int[] ch = { 0, 7, 8, 128, 248, 255 };
        foreach (var r in ch) foreach (var g in ch) foreach (var b in ch) L($"565 {r} {g} {b} = {PanelProtocol.Color565(r, g, b):X4}");
        for (int o = 0; o <= 3; o++)
        {
            var (w, h) = PanelProtocol.Size(o);
            L($"prologue {o} = {string.Join(" | ", PanelProtocol.FramePrologue(o, w, h).Select(PanelProtocol.Hex))}");
        }
        L($"consts {PanelProtocol.PnpId} {PanelProtocol.BaudRate} {PanelProtocol.NativeWidth} {PanelProtocol.NativeHeight}");
        L($"hex empty = [{PanelProtocol.Hex(Array.Empty<byte>())}]");

        L("== fmt");
        double?[] bytes =
        {
            null, double.NaN, -5e9, 0, 1, 499_999, 500_000, 999_999_999, 1e9, 1.5e9, 448.2e9, 999.4e9, 999.6e9,
            1e12, 1.04e12, 4.93e12, 9.94e12, 9.96e12, 1e13, 89.6e12, 1e15,
        };
        foreach (var b in bytes) L($"bytes {D(b)} = {Fmt.Bytes(b)} rate = {Fmt.Rate(b)} rateneg = {Fmt.Rate(b is null ? null : -b)}");
        double?[] rates = { null, -5e9, -3e9, 0, -0.0, 4e6, 5e9, 103e9, 393e9 };
        foreach (var a in rates) foreach (var b in rates) L($"growth {D(a)} {D(b)} = [{Fmt.Growth(a, b)}]");
        double?[] nums = { null, -1, 0, 0.04, 0.05, 0.5, 1.45, 2.5, 3.5, 49.5, 50.5, 99.94, 99.95, 99.96, 100, 100.5, 101.5, 1234.5 };
        foreach (var n in nums) L($"num {D(n)} = {Fmt.Percent(n)} {Fmt.Number(n)}");
        double?[] days = { null, -1, 0, 0.01, 0.02, 0.03, 0.4, 0.5, 0.99, 1, 1.99, 2.6, 98.9, 99, 99.5, 100, 150 };
        foreach (var d in days) L($"days {D(d)} = [{Fmt.Days(d)}]");

        L("== levels");
        double?[] pv = { null, 0, 69.9, 70, 89.9, 90, 99 };
        double?[] th = { null, 70, 90 };
        foreach (var v in pv) foreach (var w in th) foreach (var a in th) L($"pct {D(v)} {D(w)} {D(a)} = {Levels.ForPercent(v, w, a)}");
        double?[] fr = { null, 1e9, 500e9 };
        double?[] dl = { null, 2, 7, 12, 30, 150 };
        long?[] floor = { null, 300_000_000_000 };
        double?[] dw = { null, 30 };
        double?[] da = { null, 7 };
        foreach (var f in fr) foreach (var d in dl) foreach (var fl in floor) foreach (var w in dw) foreach (var a in da)
        {
            L($"storage {D(f)} {D(d)} {fl?.ToString(Inv) ?? "null"} {D(w)} {D(a)} = {Levels.ForStorage(f, d, fl, w, a)}");
        }

        L("== storage math");
        double?[] ages = { null, -1, 0, 120, 120.5, 121 };
        foreach (var a in ages) L($"fresh {D(a)} = {StorageMath.IsFresh(a, 120)}");
        double?[] lf = { null, 500, 600, 700 };
        foreach (var a in lf) foreach (var b in lf) L($"lowest {D(a)} {D(b)} = {D(StorageMath.LowestFree(a, b))}");
        double?[] free = { null, -10, 0, 600 };
        double?[] gr = { null, -5, 0, 50, 100 };
        foreach (var f in free) foreach (var a in gr) foreach (var b in gr) L($"daysleft {D(f)} {D(a)} {D(b)} = {D(StorageMath.DaysLeft(f, a, b))}");
        double?[] sizes = { null, -1, 0, 1000 };
        double?[] frees = { null, -100, 0, 250, 1000, 1500 };
        foreach (var s in sizes) foreach (var f in frees) L($"fill {D(s)} {D(f)} = {D(new StorageRow { Label = "x", Size = s, Free = f }.FillRatio)}");

        L("== parse");
        string[] bodies =
        {
            "",
            "{",
            "[]",
            "{}",
            "{\"status\":\"error\"}",
            "{\"status\":\"success\"}",
            "{\"status\":\"success\",\"data\":{}}",
            "{\"status\":\"success\",\"data\":{\"result\":[]}}",
            "{\"status\":\"success\",\"data\":{\"result\":{}}}",
            "{\"status\":\"success\",\"data\":{\"result\":[{\"value\":[1,\"2.5\"]},{\"value\":[1,\"3\"]}]}}",
            "{\"status\":\"success\",\"data\":{\"result\":[{\"value\":[1,\"2.5\"]}]}}",
            "{\"status\":\"success\",\"data\":{\"result\":[{\"value\":[1,\"1e300\"]}]}}",
            "{\"status\":\"success\",\"data\":{\"result\":[{\"value\":[1,\"NaN\"]}]}}",
            "{\"status\":\"success\",\"data\":{\"result\":[{\"value\":[1,\"+Inf\"]}]}}",
            "{\"status\":\"success\",\"data\":{\"result\":[{\"value\":[1,\"Infinity\"]}]}}",
            "{\"status\":\"success\",\"data\":{\"result\":[{\"value\":[1,\"-1e400\"]}]}}",
            "{\"status\":\"success\",\"data\":{\"result\":[{\"value\":[1,\"abc\"]}]}}",
            "{\"status\":\"success\",\"data\":{\"result\":[{\"value\":[1,2]}]}}",
            "{\"status\":\"success\",\"data\":{\"result\":[{\"value\":[1]}]}}",
            "{\"status\":\"success\",\"data\":{\"result\":[{}]}}",
            "{\"status\":5}",
        };
        foreach (var body in bodies) L($"parse [{body}] = {Try(() => PromClient.ParseInstant(body).ToString())}");
        L($"reading ok = {Reading.Ok(1.5)} fail = {Reading.Fail("x")} failT = {Reading.Fail("y", true)}");

        L("== config");
        var def = AppConfig.Parse(AppConfig.DefaultJson());
        L($"default {def.VmUrl} {def.RefreshSeconds} {def.TrendRefreshSeconds} {def.StaleSeconds} {def.Orientation} {def.Brightness} [{def.Title}] {D(def.StorageDaysWarn)} {D(def.StorageDaysAlarm)}");
        foreach (var s in def.Sources) L($"  source {s.Name} [{s.AgeQuery}]");
        foreach (var l in def.Load) L($"  load {l.Label} {l.Source} [{l.Query}] {l.ExtraLabel} [{l.ExtraQuery}] {l.Note} {D(l.WarnAt)} {D(l.AlarmAt)}");
        foreach (var s in def.Storage)
        {
            L($"  storage {s.Label} {s.Source} [{s.SizeQuery}] [{s.UsedQuery}] {s.WarnFreeBytes?.ToString(Inv) ?? "null"}");
            L($"    min [{TrendTemplates.Expand(def.Trends.MinFree, s)}]");
            L($"    g1 [{TrendTemplates.Expand(def.Trends.Growth1d, s)}]");
            L($"    g7 [{TrendTemplates.Expand(def.Trends.Growth7d, s)}]");
        }
        string[] configs =
        {
            "null",
            "{}",
            "{\"VmUrl\":\" \"}",
            "{\"VmUrl\":\"u\",\"RefreshSeconds\":1}",
            "{\"VmUrl\":\"u\",\"Orientation\":4}",
            "{\"VmUrl\":\"u\",\"Orientation\":-1}",
            "{\"VmUrl\":\"u\",\"Brightness\":101}",
            "{\"VmUrl\":\"u\",\"Sources\":[{\"Name\":\"a\"},{\"Name\":\"a\"}]}",
            "{\"VmUrl\":\"u\",\"Load\":[{\"Label\":\"l\",\"Source\":\"x\"}]}",
            "{\"VmUrl\":\"u\",\"Storage\":[{\"Label\":\"s\",\"Source\":\"x\"}]}",
            "{\"vmurl\":\"u\",/* c */\"refreshseconds\":2,}",
            "{\"VmUrl\":\"u\",\"RefreshSeconds\":\"x\"}",
        };
        foreach (var c in configs) L($"config [{c}] = {Try(() => { var x = AppConfig.Parse(c); return $"ok {x.VmUrl} {x.RefreshSeconds}"; })}");
        L($"expand [{TrendTemplates.Expand("{size}-{used}-{size}", new StorageConfig { SizeQuery = "S", UsedQuery = "U{size}" })}]");

        L("== collector");
        CollectorScenarios(L);
        return sb.ToString();
    }

    private const string Cfg = """
    {
      "VmUrl": "http://fake/",
      "StaleSeconds": 120,
      "TrendRefreshSeconds": 300,
      "StorageDaysWarn": 30,
      "StorageDaysAlarm": 7,
      "Title": "golden",
      "Sources": [ { "Name": "node", "AgeQuery": "AGE_NODE" }, { "Name": "pve", "AgeQuery": "AGE_PVE" }, { "Name": "none", "AgeQuery": "" } ],
      "Load": [
        { "Label": "a", "Source": "node", "Query": "Q1", "ExtraLabel": "ld", "ExtraQuery": "QX", "WarnAt": 70, "AlarmAt": 90 },
        { "Label": "b", "Source": "pve", "Query": "Q2", "Note": "12c", "WarnAt": 70, "AlarmAt": 90 },
        { "Label": "c", "Source": "node", "Query": "Q3" },
        { "Label": "d", "Source": "none", "Query": "Q1", "ExtraLabel": "e", "ExtraQuery": "QX" }
      ],
      "Trends": { "MinFree": "MIN({size}|{used})", "Growth1d": "G1({used})", "Growth7d": "G7({used})" },
      "Storage": [
        { "Label": "s", "Source": "pve", "SizeQuery": "SZ", "UsedQuery": "US" },
        { "Label": "t", "Source": "pve", "SizeQuery": "SZ2", "UsedQuery": "US2", "WarnFreeBytes": 1000 },
        { "Label": "u", "Source": "node", "SizeQuery": "SZ3", "UsedQuery": "" }
      ]
    }
    """;

    private const string CleanCfg = """
    {
      "VmUrl": "http://fake",
      "Title": "clean",
      "Sources": [ { "Name": "node", "AgeQuery": "AGE_NODE" }, { "Name": "pve", "AgeQuery": "AGE_PVE" } ],
      "Load": [ { "Label": "a", "Source": "node", "Query": "Q1", "ExtraLabel": "ld", "ExtraQuery": "QX" } ],
      "Trends": { "MinFree": "MIN({size}|{used})", "Growth1d": "G1({used})", "Growth7d": "G7({used})" },
      "Storage": [ { "Label": "s", "Source": "pve", "SizeQuery": "SZ", "UsedQuery": "US" } ]
    }
    """;

    /// <summary>Answers: a number, "none" (no series), "multi", "nan", "http400", "http500", "bad".</summary>
    private sealed class ScriptedVm : HttpMessageHandler
    {
        public Func<string, object?> Answer = _ => null;
        public bool Down;
        public readonly List<string> Asked = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var q = Uri.UnescapeDataString(request.RequestUri!.Query.Replace("?query=", ""));
            lock (Asked) Asked.Add(request.RequestUri!.GetLeftPart(UriPartial.Path) + " " + q);
            if (Down) throw new HttpRequestException("connection refused");
            var a = Answer(q);
            string result;
            switch (a)
            {
                case "http400": return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{}") });
                case "http500": return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
                case "http500exact": return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") });
                case "http499": return Task.FromResult(new HttpResponseMessage((HttpStatusCode)499) { Content = new StringContent("{}") });
                case "bad":return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json") });
                case "multi": result = "[{\"value\":[1,\"1\"]},{\"value\":[1,\"2\"]}]"; break;
                case "nan": result = "[{\"value\":[1,\"NaN\"]}]"; break;
                case double v: result = $"[{{\"metric\":{{}},\"value\":[1760000000,\"{v.ToString("R", CultureInfo.InvariantCulture)}\"]}}]"; break;
                default: result = "[]"; break;
            }
            var body = $"{{\"status\":\"success\",\"data\":{{\"resultType\":\"vector\",\"result\":{result}}}}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static object? Base(string q) => q switch
    {
        "AGE_NODE" => 10.0,
        "AGE_PVE" => 30.0,
        "Q1" => 42.0,
        "QX" => 3.5,
        "Q2" => 95.0,
        "Q3" => 75.0,
        "SZ" => 1000.0,
        "US" => 400.0,
        "MIN(SZ|US)" => 500.0,
        "G1(US)" => 100.0,
        "G7(US)" => 50.0,
        "SZ2" => 5000.0,
        "US2" => 4500.0,
        "MIN(SZ2|US2)" => 300.0,
        "G1(US2)" => -10.0,
        "G7(US2)" => 20.0,
        "SZ3" => 77.0,
        _ => null,
    };

    private static void CollectorScenarios(Action<string> L)
    {
        var t0 = new DateTime(2026, 10, 20, 6, 0, 0, DateTimeKind.Utc);
        var scenarios = new (string Name, Func<string, object?> Answer, bool Down)[]
        {
            ("base", Base, false),
            ("stale-pve", q => q == "AGE_PVE" ? 500.0 : Base(q), false),
            ("stale-node-edge", q => q == "AGE_NODE" ? 120.0 : Base(q), false),
            ("stale-node-over", q => q == "AGE_NODE" ? 120.001 : Base(q), false),
            ("age-missing", q => q == "AGE_PVE" ? null : Base(q), false),
            ("shrinking", q => q switch { "G1(US)" => -12.0, "G7(US)" => -3.0, _ => Base(q) }, false),
            ("missing-g1", q => q == "G1(US)" ? null : Base(q), false),
            ("missing-q1", q => q == "Q1" ? null : Base(q), false),
            ("multi-q2", q => q == "Q2" ? "multi" : Base(q), false),
            ("nan-sz", q => q == "SZ" ? "nan" : Base(q), false),
            ("bad-used", q => q == "US" ? "bad" : Base(q), false),
            ("http400-qx", q => q == "QX" ? "http400" : Base(q), false),
            ("http500-q1", q => q == "Q1" ? "http500" : Base(q), false),
            ("trend-transport", q => q.StartsWith("MIN(") ? "http500" : Base(q), false),
            ("trend-min-transport-only-first", q => q == "MIN(SZ|US)" ? "http500" : Base(q), false),
            ("all-500", _ => "http500", false),
            ("full", q => q switch { "US" => 1000.0, "US2" => 4999.0, _ => Base(q) }, false),
            ("overfull", q => q switch { "US" => 1200.0, _ => Base(q) }, false),
            ("down", Base, true),
        };
        foreach (var (name, answer, down) in scenarios)
        {
            var vm = new ScriptedVm { Answer = answer, Down = down };
            var cfg = AppConfig.Parse(Cfg);
            using var c = new Collector(cfg, new PromClient(cfg.VmUrl, vm));
            var d = c.CollectAsync(t0, CancellationToken.None).GetAwaiter().GetResult();
            L($"-- {name}");
            DumpFrame(L, d);
            DumpAsked(L, vm);
        }

        // Without empty queries every reading can be a transport failure: the real unreachable path.
        foreach (var (name, answer, down) in new (string, Func<string, object?>, bool)[] { ("clean-down", Base, true), ("clean-all-500", _ => "http500", false), ("clean-base", Base, false),
            ("clean-mixed-errors", q => q switch { "AGE_NODE" => "http500exact", _ => "http500" }, false),
            ("clean-499", _ => "http499", false) })
        {
            var vm = new ScriptedVm { Answer = answer, Down = down };
            var cfg = AppConfig.Parse(CleanCfg);
            using var c = new Collector(cfg, new PromClient(cfg.VmUrl, vm));
            var d = c.CollectAsync(t0, CancellationToken.None).GetAwaiter().GetResult();
            L($"-- {name}");
            DumpFrame(L, d);
            DumpAsked(L, vm);
        }

        // Trend cadence: trends are cached between refreshes, kept on a transport failure,
        // replaced on the next successful refresh.
        {
            var vm = new ScriptedVm { Answer = Base };
            var cfg = AppConfig.Parse(Cfg);
            using var c = new Collector(cfg, new PromClient(cfg.VmUrl, vm));
            (string Step, int Sec, Func<string, object?> Answer, bool Down)[] steps =
            {
                ("t0", 0, Base, false),
                ("t+100 new trends not asked", 100, q => q.StartsWith("G1(") ? 7.0 : Base(q), false),
                ("t+299", 299, q => q.StartsWith("G1(") ? 7.0 : Base(q), false),
                ("t+300 refresh", 300, q => q.StartsWith("G1(") ? 7.0 : Base(q), false),
                ("t+600 min transport", 600, q => q.StartsWith("MIN(") ? "http500" : q.StartsWith("G1(") ? 9.0 : Base(q), false),
                ("t+601 retry", 601, q => q.StartsWith("G1(") ? 11.0 : Base(q), false),
                ("t+700 down", 700, Base, true),
                ("t+701 up", 701, q => q.StartsWith("G7(") ? -1.0 : Base(q), false),
                ("t+650 clock back", 650, q => q.StartsWith("G7(") ? 2.0 : Base(q), false),
            };
            foreach (var (step, sec, answer, down) in steps)
            {
                vm.Answer = answer;
                vm.Down = down;
                vm.Asked.Clear();
                var d = c.CollectAsync(t0.AddSeconds(sec), CancellationToken.None).GetAwaiter().GetResult();
                L($"-- cadence {step}");
                DumpFrame(L, d);
                DumpAsked(L, vm);
            }
        }
    }

    private static void DumpAsked(Action<string> L, ScriptedVm vm)
    {
        List<string> asked;
        lock (vm.Asked) asked = vm.Asked.OrderBy(x => x, StringComparer.Ordinal).ToList();
        L($"  asked {asked.Count}: {string.Join(" ; ", asked)}");
    }

    private static void DumpFrame(Action<string> L, FrameData d)
    {
        L($"  frame {d.TimeUtc:o} [{d.Title}] vm={d.VmReachable} stale={d.StaleSources} ok={d.ValuesOk} err=[{d.Error}]");
        foreach (var r in d.Load)
        {
            L($"  load {r.Label} note=[{r.Note}] xl=[{r.ExtraLabel}] v={D(r.Value)} x={D(r.Extra)} level={r.Level} pct={Fmt.Percent(r.Value)} num={Fmt.Number(r.Extra)}");
        }
        foreach (var s in d.Storage)
        {
            L($"  storage {s.Label} size={D(s.Size)} free={D(s.Free)} low={D(s.LowestFree7d)} days={D(s.DaysLeft)} g1={D(s.Growth1d)} g7={D(s.Growth7d)} level={s.Level} fill={D(s.FillRatio)} text=[{Fmt.Bytes(s.Free)}|{Fmt.Bytes(s.LowestFree7d)}|{Fmt.Growth(s.Growth1d, s.Growth7d)}|{Fmt.Days(s.DaysLeft)}]");
        }
    }
}
