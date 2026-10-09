namespace HeartbeatWatchdog.Core;

/// <summary>建立或更新監控項目時，使用者可設定的欄位。</summary>
public sealed record MonitorDefinition(
    string Name,
    string Slug,
    ScheduleType ScheduleType,
    int? IntervalMinutes,
    string? CronExpression,
    int GraceMinutes,
    string TimeZone,
    string? WebhookUrl)
{
    public MonitorSchedule ToSchedule() => new(ScheduleType, IntervalMinutes, CronExpression, TimeZone);
}
