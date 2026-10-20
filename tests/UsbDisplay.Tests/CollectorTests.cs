using System.Globalization;
using System.Net;
using UsbDisplay.Core;
using Xunit;

namespace UsbDisplay.Tests;

/// <summary>A fake VictoriaMetrics: answers each query from a table, or fails the connection.</summary>
internal sealed class FakeVm : HttpMessageHandler
{
    private readonly Func<string, double?> _answer;
    private readonly bool _down;

    public FakeVm(Func<string, double?> answer, bool down = false)
    {
        _answer = answer;
        _down = down;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (_down) throw new HttpRequestException("connection refused");
        var q = Uri.UnescapeDataString(request.RequestUri!.Query.Replace("?query=", ""));
        var v = _answer(q);
        var result = v is null ? "[]" : $"[{{\"metric\":{{}},\"value\":[1760000000,\"{v.Value.ToString(CultureInfo.InvariantCulture)}\"]}}]";
        var body = $"{{\"status\":\"success\",\"data\":{{\"resultType\":\"vector\",\"result\":{result}}}}}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}

[Trait("Category", "Stale")]
public class CollectorTests
{
    private const string Cfg = """
    {
      "VmUrl": "http://fake",
      "StaleSeconds": 120,
      "Title": "t",
      "Sources": [ { "Name": "node", "AgeQuery": "AGE_NODE" }, { "Name": "pve", "AgeQuery": "AGE_PVE" } ],
      "Load": [
        { "Label": "a", "Source": "node", "Query": "Q1", "ExtraLabel": "ld", "ExtraQuery": "QX" },
        { "Label": "b", "Source": "pve", "Query": "Q2" }
      ],
      "Trends": { "MinFree": "MIN({size}|{used})", "Growth1d": "G1({used})", "Growth7d": "G7({used})" },
      "Storage": [ { "Label": "s", "Source": "pve", "SizeQuery": "SZ", "UsedQuery": "US" } ]
    }
    """;

    private static Func<string, double?> Table(double pveAge) => q => q switch
    {
        "AGE_NODE" => 10,
        "AGE_PVE" => pveAge,
        "Q1" => 42,
        "QX" => 3.5,
        "Q2" => 43,
        "SZ" => 1000,
        "US" => 400,
        "MIN(SZ|US)" => 500,
        "G1(US)" => 100,
        "G7(US)" => 50,
        _ => null,
    };

    private static FrameData Collect(Func<string, double?> answer, bool down = false)
    {
        var cfg = AppConfig.Parse(Cfg);
        using var c = new Collector(cfg, new PromClient(cfg.VmUrl, new FakeVm(answer, down)));
        return c.CollectAsync(DateTime.UtcNow, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void FreshSourcesGiveValues()
    {
        var d = Collect(Table(pveAge: 30));
        Assert.Equal(42, d.Load[0].Value);
        Assert.Equal(3.5, d.Load[0].Extra);
        Assert.Equal(43, d.Load[1].Value);
        Assert.Equal(600, d.Storage[0].Free);
        Assert.Equal(500, d.Storage[0].LowestFree7d);
        Assert.Equal(6, d.Storage[0].DaysLeft);
        Assert.Equal(100, d.Storage[0].Growth1d);
        Assert.Equal(50, d.Storage[0].Growth7d);
        Assert.Equal(4, d.ValuesOk);
        Assert.True(d.VmReachable);
    }

    [Fact]
    public void StaleSourceBlanksOnlyItsValues()
    {
        var d = Collect(Table(pveAge: 500));
        Assert.Equal(42, d.Load[0].Value);
        Assert.Null(d.Load[1].Value);
        Assert.Null(d.Storage[0].Free);
        Assert.Null(d.Storage[0].DaysLeft);
        Assert.Null(d.Storage[0].Growth1d);
        Assert.Null(d.Storage[0].Growth7d);
        Assert.Equal(1, d.StaleSources);
        Assert.Equal(2, d.ValuesOk);
    }

    [Fact]
    public void ShrinkingStorageKeepsItsNegativeRates()
    {
        var d = Collect(q => q switch { "G1(US)" => -12.0, "G7(US)" => -3.0, _ => Table(30)(q) });
        Assert.Equal(-12, d.Storage[0].Growth1d);
        Assert.Equal(-3, d.Storage[0].Growth7d);
        Assert.Null(d.Storage[0].DaysLeft);
    }

    [Fact]
    public void MissingGrowthSeriesIsNull()
    {
        var d = Collect(q => q == "G1(US)" ? null : Table(30)(q));
        Assert.Null(d.Storage[0].Growth1d);
        Assert.Equal(50, d.Storage[0].Growth7d);
    }

    [Fact]
    public void MissingSeriesIsNotZero()
    {
        var d = Collect(q => q == "Q1" ? null : Table(30)(q));
        Assert.Null(d.Load[0].Value);
        Assert.Equal(3, d.ValuesOk);
    }

    [Fact]
    public void UnreachableVmBlanksEverything()
    {
        var d = Collect(_ => 1, down: true);
        Assert.False(d.VmReachable);
        Assert.StartsWith("unreachable", d.Error);
        Assert.All(d.Load, r => Assert.Null(r.Value));
        Assert.All(d.Storage, s => Assert.Null(s.Free));
        Assert.Equal(0, d.ValuesOk);
    }

    [Fact]
    public void UnknownSourceAgeIsStale() => Assert.False(StorageMath.IsFresh(null, 120));

    [Theory]
    [InlineData(18.0, true)]
    [InlineData(120.0, true)]
    [InlineData(121.0, false)]
    public void AgeLimitIsInclusive(double age, bool fresh) => Assert.Equal(fresh, StorageMath.IsFresh(age, 120));
}
