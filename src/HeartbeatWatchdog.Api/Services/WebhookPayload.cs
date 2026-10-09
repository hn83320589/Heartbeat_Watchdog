using System.Text.Json;
using System.Text.Json.Serialization;
using HeartbeatWatchdog.Core;

namespace HeartbeatWatchdog.Api.Services;

/// <summary>Webhook JSON 內容；格式記載於 README「Webhook payload」。</summary>
public sealed record WebhookPayload(
    string Event,
    WebhookPayload.MonitorInfo Monitor,
    MonitorStatus Status,
    MonitorStatus PreviousStatus,
    TransitionReason Reason,
    DateTime OccurredAt,
    DateTime? LastPingAt,
    DateTime? NextExpectedAt)
{
    public sealed record MonitorInfo(int Id, string Name, string Slug, string TimeZone);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(HeartbeatMonitor monitor, StatusTransition transition, NotificationKind kind) =>
        JsonSerializer.Serialize(
            new WebhookPayload(
                Event: kind == NotificationKind.Down ? "monitor.down" : "monitor.up",
                Monitor: new MonitorInfo(monitor.Id, monitor.Name, monitor.Slug, monitor.TimeZone),
                Status: transition.To,
                PreviousStatus: transition.From,
                Reason: transition.Reason,
                OccurredAt: transition.AtUtc,
                LastPingAt: monitor.LastPingAtUtc,
                NextExpectedAt: monitor.NextExpectedAtUtc),
            SerializerOptions);
}
