using HeartbeatWatchdog.Core;

namespace HeartbeatWatchdog.Tests.Core;

public class MonitorScheduleTests
{
    private static DateTime Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public void Interval_NextExpected_IsReferencePlusInterval()
    {
        var schedule = MonitorSchedule.Interval(15, "UTC");

        var next = schedule.NextAfter(Utc(2026, 7, 1, 10, 0));

        Assert.Equal(Utc(2026, 7, 1, 10, 15), next);
    }

    [Fact]
    public void Cron_BerlinDaily0230_InSummer_IsUtcPlusTwo()
    {
        var schedule = MonitorSchedule.Cron("30 2 * * *", "Europe/Berlin");

        var next = schedule.NextAfter(Utc(2026, 7, 1, 0, 0));

        // CEST = UTC+2，所以柏林 02:30 = 00:30 UTC
        Assert.Equal(Utc(2026, 7, 1, 0, 30), next);
    }

    [Fact]
    public void Cron_BerlinDaily0230_InWinter_IsUtcPlusOne()
    {
        var schedule = MonitorSchedule.Cron("30 2 * * *", "Europe/Berlin");

        var next = schedule.NextAfter(Utc(2026, 1, 15, 3, 0));

        // CET = UTC+1；01:30 UTC 已過，所以是隔天
        Assert.Equal(Utc(2026, 1, 16, 1, 30), next);
    }

    [Fact]
    public void Cron_BerlinDaily0230_OnSpringForwardDay_ShiftsToFirstValidInstant()
    {
        // 2026-03-29 柏林 02:00 跳到 03:00，當地沒有 02:30。
        // Cronos 會改在跳躍後的第一個時刻（03:00 CEST = 01:00 UTC）觸發，不會整天跳過。
        var schedule = MonitorSchedule.Cron("30 2 * * *", "Europe/Berlin");

        var next = schedule.NextAfter(Utc(2026, 3, 28, 12, 0));

        Assert.Equal(Utc(2026, 3, 29, 1, 0), next);
    }

    [Fact]
    public void Cron_BerlinDaily0230_OnFallBackDay_FiresOnlyOnce()
    {
        // 2026-10-25 柏林 03:00 CEST 退回 02:00 CET，當地 02:30 出現兩次（00:30 UTC 與 01:30 UTC）。
        // 只應在第一次觸發；之後的下一次是隔天，而不是同一天的 01:30 UTC。
        var schedule = MonitorSchedule.Cron("30 2 * * *", "Europe/Berlin");

        Assert.Equal(Utc(2026, 10, 25, 0, 30), schedule.NextAfter(Utc(2026, 10, 24, 12, 0)));
        Assert.Equal(Utc(2026, 10, 26, 1, 30), schedule.NextAfter(Utc(2026, 10, 25, 0, 30)));
    }

    [Fact]
    public void Cron_ImpossibleDate_ReturnsNull()
    {
        var schedule = MonitorSchedule.Cron("0 0 30 2 *", "UTC");

        Assert.Null(schedule.NextAfter(Utc(2026, 1, 1, 0, 0)));
    }
}
