namespace HeartbeatWatchdog.Api.Services;

/// <summary>
/// 序列化所有會改變監控項目狀態的操作（ping、檢查 tick、暫停/恢復、更新）。
/// 題目假設只有單一執行個體，行程內的鎖就足以避免「tick 讀到舊狀態後覆寫 ping 結果」這類競態，
/// 比樂觀並行控制加重試簡單得多。
/// </summary>
public sealed class MonitorMutationLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
