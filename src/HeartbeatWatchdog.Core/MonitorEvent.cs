namespace HeartbeatWatchdog.Core;

public enum MonitorEventKind
{
    PingSuccess,
    PingStart,
    PingFail,
    StatusChanged,
}

/// <summary>監控項目的事件歷史：每一次 ping 與每一次狀態轉換各一筆。</summary>
public class MonitorEvent
{
    private MonitorEvent()
    {
    }

    public long Id { get; private set; }
    public int MonitorId { get; private set; }
    public MonitorEventKind Kind { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? SourceIp { get; private set; }
    public long? DurationMs { get; private set; }
    public MonitorStatus? FromStatus { get; private set; }
    public MonitorStatus? ToStatus { get; private set; }
    public TransitionReason? Reason { get; private set; }

    public static MonitorEvent Ping(int monitorId, PingKind kind, DateTime occurredAtUtc, string? sourceIp, TimeSpan? duration) => new()
    {
        MonitorId = monitorId,
        Kind = kind switch
        {
            PingKind.Success => MonitorEventKind.PingSuccess,
            PingKind.Start => MonitorEventKind.PingStart,
            PingKind.Fail => MonitorEventKind.PingFail,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        },
        OccurredAtUtc = occurredAtUtc,
        SourceIp = sourceIp,
        DurationMs = duration is { } d ? (long)d.TotalMilliseconds : null,
    };

    public static MonitorEvent StatusChanged(int monitorId, StatusTransition transition) => new()
    {
        MonitorId = monitorId,
        Kind = MonitorEventKind.StatusChanged,
        OccurredAtUtc = transition.AtUtc,
        FromStatus = transition.From,
        ToStatus = transition.To,
        Reason = transition.Reason,
    };
}
