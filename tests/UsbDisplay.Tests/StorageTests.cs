using UsbDisplay.Core;
using Xunit;

namespace UsbDisplay.Tests;

public class StorageTests
{
    [Theory]
    [InlineData(600.0, 100.0, 50.0, 6.0)]     // steeper of the two rates wins
    [InlineData(600.0, -5.0, 50.0, 12.0)]     // shrinking today, growing over the week
    [InlineData(600.0, 100.0, -15.0, 6.0)]    // the 7-day average alone would hide this one
    public void DaysLeftUsesTheSteeperPositiveRate(double free, double g1, double g7, double want) =>
        Assert.Equal(want, StorageMath.DaysLeft(free, g1, g7));

    [Fact]
    public void NoGrowthNoDaysLeft() => Assert.Null(StorageMath.DaysLeft(600, -5, -1));

    [Fact]
    public void UnknownFreeNoDaysLeft() => Assert.Null(StorageMath.DaysLeft(null, 100, 100));

    [Theory]
    [InlineData(500.0, 600.0, 500.0)]
    [InlineData(700.0, 600.0, 600.0)]   // the hourly subquery lags the present
    public void LowestFreeIncludesNow(double sub, double now, double want) =>
        Assert.Equal(want, StorageMath.LowestFree(sub, now));

    [Fact]
    public void LowestFreeWithoutSubquery() => Assert.Equal(600, StorageMath.LowestFree(null, 600));

    [Theory]
    [InlineData(448.2e9, "448G")]
    [InlineData(4.93e12, "4.9T")]
    [InlineData(89.6e12, "90T")]
    [InlineData(19.4e9, "19G")]
    public void BytesAreAbsoluteDecimal(double b, string want) => Assert.Equal(want, Fmt.Bytes(b));

    [Fact]
    public void UnknownBytesAreDashes() => Assert.Equal("--", Fmt.Bytes(null));

    [Theory]
    [InlineData(2.6, "2d")]
    [InlineData(0.4, "10h")]
    [InlineData(150.0, "99+d")]
    public void DaysRoundDown(double d, string want) => Assert.Equal(want, Fmt.Days(d));

    [Theory]
    [InlineData(106e9, "+106G/d")]
    [InlineData(-12e9, "-12G/d")]
    [InlineData(2.72e11, "+272G/d")]
    [InlineData(1.5e12, "+1.5T/d")]
    [InlineData(0.0, "+0M/d")]
    [InlineData(4e6, "+4M/d")]
    public void RateIsSignedPerDay(double b, string want) => Assert.Equal(want, Fmt.Rate(b));

    [Fact]
    public void UnknownRateIsEmpty() => Assert.Equal("", Fmt.Rate(null));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteValuesFormatAsUnknown(double v)
    {
        Assert.Equal("--", Fmt.Bytes(v));
        Assert.Equal("", Fmt.Rate(v));
        Assert.Equal("--", Fmt.Percent(v));
        Assert.Equal("--", Fmt.Number(v));
        Assert.Equal("", Fmt.Days(v));
    }

    [Theory]
    [InlineData(1e9, double.NaN, "+1G/d 24h")]
    [InlineData(double.NaN, 1e9, "+1G/d 7d")]
    [InlineData(double.PositiveInfinity, double.NaN, "")]
    public void GrowthIgnoresNonFiniteRates(double g1, double g7, string want) =>
        Assert.Equal(want, Fmt.Growth(g1, g7));

    [Fact]
    public void DaysLeftIgnoresNonFiniteRates()
    {
        Assert.Equal(10, StorageMath.DaysLeft(100, double.NaN, 10));
        Assert.Null(StorageMath.DaysLeft(100, double.NaN, double.PositiveInfinity));
    }

    [Theory]
    [InlineData(103e9, -15e9, "+103G/d 24h")]    // 1d spike, the 7d average hides it
    [InlineData(272e9, 393e9, "+393G/d 7d")]     // 7d is steeper
    [InlineData(5e9, 5e9, "+5G/d 24h")]          // tie goes to 24h
    [InlineData(-5e9, -3e9, "-3G/d 7d")]         // both shrinking: the least negative
    [InlineData(null, 50e9, "+50G/d 7d")]
    [InlineData(20e9, null, "+20G/d 24h")]
    [InlineData(null, null, "")]
    public void GrowthShowsTheSteeperRateWithItsWindow(double? g1, double? g7, string want) =>
        Assert.Equal(want, Fmt.Growth(g1, g7));

    [Fact]
    public void FillRatioFromFree() =>
        Assert.Equal(0.25, new StorageRow { Label = "x", Size = 1000, Free = 750 }.FillRatio);

    [Fact]
    public void DefaultConfigIsValidAndComplete()
    {
        var cfg = AppConfig.Parse(AppConfig.DefaultJson());
        Assert.NotEmpty(cfg.Load);
        Assert.NotEmpty(cfg.Storage);
        Assert.All(cfg.Storage, s => Assert.Null(s.WarnFreeBytes));
        Assert.All(cfg.Load, l => Assert.NotNull(l.AlarmAt));
        Assert.Equal(7, cfg.StorageDaysAlarm);
        Assert.Equal(30, cfg.StorageDaysWarn);
        foreach (var s in cfg.Storage)
        {
            foreach (var t in new[] { cfg.Trends.MinFree, cfg.Trends.Growth1d, cfg.Trends.Growth7d })
            {
                var q = TrendTemplates.Expand(t, s);
                Assert.DoesNotContain("{size}", q);
                Assert.DoesNotContain("{used}", q);
                Assert.Contains(s.UsedQuery, q);
            }
        }
    }
}
