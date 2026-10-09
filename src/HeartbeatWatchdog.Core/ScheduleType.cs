namespace HeartbeatWatchdog.Core;

public enum ScheduleType
{
    /// <summary>固定間隔：每 N 分鐘預期一次 ping。</summary>
    Interval,

    /// <summary>標準 5 欄位 cron 表達式，依監控項目的時區解讀。</summary>
    Cron,
}
