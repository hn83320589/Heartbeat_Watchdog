using HeartbeatWatchdog.Core;

namespace HeartbeatWatchdog.Tests.Core;

public class AvailabilityCalculatorTests
{
    private static readonly DateTime T0 = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    private static StatusPeriod Period(MonitorStatus status, int startHour, int? endHour) =>
        new(status, T0.AddHours(startHour), endHour is { } h ? T0.AddHours(h) : null);

    [Fact]
    public void PausedTime_IsExcludedFromDenominator()
    {
        // 6h Up、4h Paused、2h Down → 6 / (6 + 2) = 75%，而不是 6 / 12 = 50%
        StatusPeriod[] periods =
        [
            Period(MonitorStatus.Up, 0, 6),
            Period(MonitorStatus.Paused, 6, 10),
            Period(MonitorStatus.Down, 10, null),
        ];

        var result = AvailabilityCalculator.Calculate(periods, T0, T0.AddHours(12));

        Assert.Equal(0.75, result.Ratio);
        Assert.Equal(TimeSpan.FromHours(6), result.UpTime);
        Assert.Equal(TimeSpan.FromHours(8), result.MonitoredTime);
        Assert.Equal(TimeSpan.FromHours(4), result.PausedTime);
    }

    [Fact]
    public void NewTime_IsExcluded_AndLateCountsAsAvailable()
    {
        StatusPeriod[] periods =
        [
            Period(MonitorStatus.New, 0, 2),
            Period(MonitorStatus.Up, 2, 5),
            Period(MonitorStatus.Late, 5, 6),
            Period(MonitorStatus.Down, 6, 8),
        ];

        var result = AvailabilityCalculator.Calculate(periods, T0, T0.AddHours(8));

        Assert.Equal(4.0 / 6.0, result.Ratio!.Value, precision: 10);
    }

    [Fact]
    public void PeriodsAreClippedToWindow()
    {
        // 視窗只涵蓋 Up 的最後 2 小時與 Down 的前 2 小時
        StatusPeriod[] periods =
        [
            Period(MonitorStatus.Up, -100, 10),
            Period(MonitorStatus.Down, 10, 500),
        ];

        var result = AvailabilityCalculator.Calculate(periods, T0.AddHours(8), T0.AddHours(12));

        Assert.Equal(0.5, result.Ratio);
    }

    [Fact]
    public void OnlyPausedOrNew_HasNoRatio()
    {
        StatusPeriod[] periods = [Period(MonitorStatus.New, 0, 1), Period(MonitorStatus.Paused, 1, null)];

        var result = AvailabilityCalculator.Calculate(periods, T0, T0.AddHours(5));

        Assert.Null(result.Ratio);
    }
}
