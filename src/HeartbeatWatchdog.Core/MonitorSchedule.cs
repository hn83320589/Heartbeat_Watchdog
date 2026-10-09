using Cronos;

namespace HeartbeatWatchdog.Core;

/// <summary>
/// 計算「下一次預期收到 ping 的時間」。輸入輸出一律是 UTC；
/// cron 依 <see cref="TimeZoneId"/> 解讀，所以柏林主機的 02:30 會自動跟著夏令時間移動。
/// </summary>
public sealed record MonitorSchedule(ScheduleType Type, int? IntervalMinutes, string? CronExpression, string TimeZoneId)
{
    public static MonitorSchedule Interval(int minutes, string timeZoneId) =>
        new(ScheduleType.Interval, minutes, null, timeZoneId);

    public static MonitorSchedule Cron(string expression, string timeZoneId) =>
        new(ScheduleType.Cron, null, expression, timeZoneId);

    /// <returns>參考時間之後的下一次預期時間；cron 永遠不會觸發時回傳 null。</returns>
    public DateTime? NextAfter(DateTime referenceUtc)
    {
        if (referenceUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Reference time must be UTC.", nameof(referenceUtc));
        }

        return Type switch
        {
            ScheduleType.Interval => referenceUtc.AddMinutes(IntervalMinutes
                ?? throw new InvalidOperationException("Interval schedule has no IntervalMinutes.")),
            ScheduleType.Cron => Cronos.CronExpression
                .Parse(CronExpression ?? throw new InvalidOperationException("Cron schedule has no CronExpression."), CronFormat.Standard)
                .GetNextOccurrence(referenceUtc, TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)),
            _ => throw new InvalidOperationException($"Unknown schedule type {Type}."),
        };
    }
}
