using UsbDisplay.Core;
using Xunit;

namespace UsbDisplay.Tests;

public class LevelTests
{
    [Theory]
    [InlineData(99.0, Level.Alarm)]   // a CPU at 99 % must show as an alarm
    [InlineData(90.0, Level.Alarm)]   // thresholds are inclusive
    [InlineData(75.0, Level.Warn)]
    [InlineData(70.0, Level.Warn)]
    [InlineData(50.0, Level.Ok)]
    public void PercentAgainstThresholds(double value, Level want) => Assert.Equal(want, Levels.ForPercent(value, 70, 90));

    [Fact]
    public void NoValueNoColor() => Assert.Equal(Level.None, Levels.ForPercent(null, 70, 90));

    [Fact]
    public void NoThresholdNoColor() => Assert.Equal(Level.None, Levels.ForPercent(99, null, null));

    [Theory]
    [InlineData(2.0, Level.Alarm)]    // a pool two days from full
    [InlineData(12.0, Level.Warn)]
    [InlineData(150.0, Level.Ok)]
    public void StorageOnDaysLeft(double days, Level want) =>
        Assert.Equal(want, Levels.ForStorage(266e9, days, null, 30, 7));

    [Fact]
    public void StorageNotGrowingIsOk() => Assert.Equal(Level.Ok, Levels.ForStorage(266e9, null, null, 30, 7));

    [Fact]
    public void StorageUnknownFreeHasNoColor() => Assert.Equal(Level.None, Levels.ForStorage(null, 2, null, 30, 7));

    [Fact]
    public void StorageAbsoluteFloorWins() => Assert.Equal(Level.Alarm, Levels.ForStorage(266e9, null, 300_000_000_000, 30, 7));

    [Fact]
    public void StorageWithoutThresholdsHasNoColor() => Assert.Equal(Level.None, Levels.ForStorage(266e9, 2, null, null, null));
}
