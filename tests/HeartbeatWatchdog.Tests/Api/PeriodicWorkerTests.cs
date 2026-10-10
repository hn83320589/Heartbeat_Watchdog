using System.Threading.Channels;
using HeartbeatWatchdog.Api.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HeartbeatWatchdog.Tests.Api;

/// <summary>
/// 驗證背景 worker 的迴圈本身：用 FakeTimeProvider 驅動真正的 PeriodicTimer，
/// 每個 tick 透過 channel 回報，測試只等待「tick 發生」這個事件，不等待真實時間。
/// </summary>
public class PeriodicWorkerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>只是防止測試在失敗時永遠卡住的保險，正常情況下 tick 會立即到達。</summary>
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(10);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

    private sealed class RecordingWorker(TimeProvider clock, WorkerHeartbeats heartbeats, Func<int, bool> failOnTick)
        : PeriodicWorker(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            clock, heartbeats, NullLogger.Instance)
    {
        private readonly TimeProvider _clock = clock;
        private int _ticks;

        public Channel<DateTimeOffset> Ticks { get; } = Channel.CreateUnbounded<DateTimeOffset>();

        protected override string Name => "test-worker";
        protected override TimeSpan Interval => PeriodicWorkerTests.Interval;

        protected override Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            var tick = Interlocked.Increment(ref _ticks);
            Ticks.Writer.TryWrite(_clock.GetUtcNow());
            return failOnTick(tick) ? throw new InvalidOperationException($"boom on tick {tick}") : Task.CompletedTask;
        }
    }

    private async Task<DateTimeOffset> NextTickAsync(RecordingWorker worker)
    {
        using var cts = new CancellationTokenSource(Safety);
        return await worker.Ticks.Reader.ReadAsync(cts.Token);
    }

    [Fact]
    public async Task Worker_RunsImmediately_ThenEveryInterval()
    {
        var worker = new RecordingWorker(_clock, new WorkerHeartbeats(_clock), _ => false);
        await worker.StartAsync(CancellationToken.None);

        Assert.Equal(_clock.Start, await NextTickAsync(worker));

        _clock.Advance(Interval - TimeSpan.FromSeconds(1));
        Assert.False(worker.Ticks.Reader.TryRead(out _)); // 29 秒時還不會 tick

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(_clock.Start + Interval, await NextTickAsync(worker));

        _clock.Advance(Interval);
        Assert.Equal(_clock.Start + Interval * 2, await NextTickAsync(worker));

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FailingTick_IsLogged_AndWorkerKeepsTicking_AndHealthRecovers()
    {
        var heartbeats = new WorkerHeartbeats(_clock);
        var worker = new RecordingWorker(_clock, heartbeats, tick => tick is >= 2 and <= 4);
        await worker.StartAsync(CancellationToken.None);
        await NextTickAsync(worker);                      // tick 1 成功

        for (var i = 0; i < 3; i++)                        // tick 2~4 丟例外
        {
            _clock.Advance(Interval);
            await NextTickAsync(worker);
        }
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HealthStatus.Unhealthy, heartbeats.Check().Status); // 連續失敗超過 3 個間隔

        _clock.Advance(Interval);
        await NextTickAsync(worker);                       // tick 5：worker 沒有停，恢復成功
        await worker.StopAsync(CancellationToken.None);    // 等迴圈處理完這個 tick

        Assert.Equal(HealthStatus.Healthy, heartbeats.Check().Status);
    }
}
