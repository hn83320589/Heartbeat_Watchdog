using HeartbeatWatchdog.Core;

namespace HeartbeatWatchdog.Api.Contracts;

/// <summary>建立 / 更新監控項目的請求。scheduleType 為 Interval 時填 intervalMinutes，為 Cron 時填 cronExpression。</summary>
public sealed record MonitorRequest(
    string? Name,
    string? Slug,
    ScheduleType? ScheduleType,
    int? IntervalMinutes,
    string? CronExpression,
    int? GraceMinutes,
    string? TimeZone,
    string? WebhookUrl)
{
    public const string DefaultTimeZone = "UTC";

    public IReadOnlyDictionary<string, string[]> Validate(DateTime nowUtc, out MonitorDefinition definition)
    {
        definition = new MonitorDefinition(
            Name ?? "",
            Slug ?? "",
            ScheduleType ?? Core.ScheduleType.Interval,
            IntervalMinutes,
            CronExpression,
            GraceMinutes ?? 0,
            string.IsNullOrEmpty(TimeZone) ? DefaultTimeZone : TimeZone,
            string.IsNullOrWhiteSpace(WebhookUrl) ? null : WebhookUrl);

        var errors = new Dictionary<string, string[]>(MonitorDefinitionValidator.Validate(definition, nowUtc));
        if (ScheduleType is null)
        {
            errors["scheduleType"] = ["scheduleType is required: 'Interval' or 'Cron'."];
        }
        if (GraceMinutes is null)
        {
            errors["graceMinutes"] = ["graceMinutes is required (minutes, >= 0)."];
        }
        return errors;
    }
}

public sealed record MonitorResponse(
    int Id,
    string Name,
    string Slug,
    string PingToken,
    string PingUrl,
    ScheduleType ScheduleType,
    int? IntervalMinutes,
    string? CronExpression,
    int GraceMinutes,
    string TimeZone,
    string? WebhookUrl,
    MonitorStatus Status,
    DateTime CreatedAt,
    DateTime StatusChangedAt,
    DateTime? LastPingAt,
    DateTime? NextExpectedAt,
    DateTimeOffset? NextExpectedAtLocal)
{
    public static MonitorResponse From(HeartbeatMonitor m, string baseUrl) => new(
        m.Id, m.Name, m.Slug, m.PingToken, $"{baseUrl}/ping/{m.PingToken}",
        m.ScheduleType, m.IntervalMinutes, m.CronExpression, m.GraceMinutes, m.TimeZone, m.WebhookUrl,
        m.Status, m.CreatedAtUtc, m.StatusChangedAtUtc, m.LastPingAtUtc, m.NextExpectedAtUtc,
        m.NextExpectedAtUtc is { } next
            ? TimeZoneInfo.ConvertTime(new DateTimeOffset(next), TimeZoneInfo.FindSystemTimeZoneById(m.TimeZone))
            : null);
}

public sealed record MonitorEventResponse(
    long Id,
    MonitorEventKind Kind,
    DateTime OccurredAt,
    string? SourceIp,
    long? DurationMs,
    MonitorStatus? FromStatus,
    MonitorStatus? ToStatus,
    TransitionReason? Reason)
{
    public static MonitorEventResponse From(MonitorEvent e) =>
        new(e.Id, e.Kind, e.OccurredAtUtc, e.SourceIp, e.DurationMs, e.FromStatus, e.ToStatus, e.Reason);
}

public sealed record NotificationAttemptResponse(
    int AttemptNumber, DateTime AttemptedAt, bool Succeeded, int? HttpStatusCode, string? Error, long ElapsedMs);

public sealed record NotificationResponse(
    long Id,
    NotificationKind Kind,
    NotificationStatus Status,
    string? TargetUrl,
    DateTime CreatedAt,
    DateTime? NextAttemptAt,
    DateTime? CompletedAt,
    string Payload,
    IReadOnlyList<NotificationAttemptResponse> Attempts)
{
    public static NotificationResponse From(Notification n) => new(
        n.Id, n.Kind, n.Status, n.TargetUrl, n.CreatedAtUtc, n.NextAttemptAtUtc, n.CompletedAtUtc, n.Payload,
        n.Attempts
            .OrderBy(a => a.AttemptNumber)
            .Select(a => new NotificationAttemptResponse(a.AttemptNumber, a.AttemptedAtUtc, a.Succeeded, a.HttpStatusCode, a.Error, a.ElapsedMs))
            .ToList());
}

public sealed record AvailabilityResponse(
    int MonitorId,
    int Days,
    DateTime From,
    DateTime To,
    double? Availability,
    double UpSeconds,
    double MonitoredSeconds,
    double PausedSeconds);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
