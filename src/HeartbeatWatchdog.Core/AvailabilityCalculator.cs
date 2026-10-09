namespace HeartbeatWatchdog.Core;

public sealed record AvailabilityResult(
    DateTime FromUtc,
    DateTime ToUtc,
    TimeSpan UpTime,
    TimeSpan MonitoredTime,
    TimeSpan PausedTime,
    double? Ratio);

/// <summary>
/// 可用率 = 可用時間 ÷ 受監控時間。
/// <list type="bullet">
/// <item>Paused 與 New（還沒開始監控）不計入分母。</item>
/// <item>Late 仍在寬限內、任務尚未被判定失敗，所以算可用時間。</item>
/// </list>
/// </summary>
public static class AvailabilityCalculator
{
    public static AvailabilityResult Calculate(IEnumerable<StatusPeriod> periods, DateTime fromUtc, DateTime toUtc)
    {
        var up = TimeSpan.Zero;
        var monitored = TimeSpan.Zero;
        var paused = TimeSpan.Zero;

        foreach (var period in periods)
        {
            var start = period.StartedAtUtc > fromUtc ? period.StartedAtUtc : fromUtc;
            var end = period.EndedAtUtc is { } ended && ended < toUtc ? ended : toUtc;
            if (end <= start)
            {
                continue;
            }

            var length = end - start;
            switch (period.Status)
            {
                case MonitorStatus.Up or MonitorStatus.Late:
                    up += length;
                    monitored += length;
                    break;
                case MonitorStatus.Down:
                    monitored += length;
                    break;
                case MonitorStatus.Paused:
                    paused += length;
                    break;
            }
        }

        double? ratio = monitored > TimeSpan.Zero ? up / monitored : null;
        return new AvailabilityResult(fromUtc, toUtc, up, monitored, paused, ratio);
    }
}
