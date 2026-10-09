namespace HeartbeatWatchdog.Core;

/// <summary>監控項目處於某狀態的一段區間；<see cref="EndedAtUtc"/> 為 null 表示目前仍在此狀態。</summary>
public class StatusPeriod(MonitorStatus status, DateTime startedAtUtc, DateTime? endedAtUtc)
{
    public long Id { get; private set; }
    public int MonitorId { get; init; }
    public MonitorStatus Status { get; private set; } = status;
    public DateTime StartedAtUtc { get; private set; } = startedAtUtc;
    public DateTime? EndedAtUtc { get; private set; } = endedAtUtc;

    public void Close(DateTime endedAtUtc) => EndedAtUtc = endedAtUtc;
}
