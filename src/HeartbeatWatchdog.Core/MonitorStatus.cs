namespace HeartbeatWatchdog.Core;

public enum MonitorStatus
{
    /// <summary>尚未收過任何 ping。</summary>
    New,

    /// <summary>最近一次成功 ping 之後，還沒超過預期時間。</summary>
    Up,

    /// <summary>超過預期時間，但仍在寬限時間內。</summary>
    Late,

    /// <summary>超過寬限時間，或收到 fail ping。</summary>
    Down,

    /// <summary>手動暫停；不評估、不通知。</summary>
    Paused,
}
