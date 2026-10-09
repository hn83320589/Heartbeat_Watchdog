using HeartbeatWatchdog.Api.Workers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;

namespace HeartbeatWatchdog.Tests.Api;

public class WorkerHeartbeatsTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void TickingWorker_IsHealthy()
    {
        var heartbeats = new WorkerHeartbeats(_clock);
        heartbeats.Register("monitor-check", TimeSpan.FromSeconds(30));

        for (var i = 0; i < 10; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(30));
            heartbeats.MarkSuccess("monitor-check");
        }

        Assert.Equal(HealthStatus.Healthy, heartbeats.Check().Status);
    }

    [Fact]
    public void WorkerThatStoppedTicking_IsUnhealthy()
    {
        var heartbeats = new WorkerHeartbeats(_clock);
        heartbeats.Register("monitor-check", TimeSpan.FromSeconds(30));
        heartbeats.MarkSuccess("monitor-check");

        _clock.Advance(TimeSpan.FromSeconds(91));

        var result = heartbeats.Check();
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("monitor-check", result.Description);
    }

    [Fact]
    public void WorkerThatNeverSucceeded_BecomesUnhealthyAfterThreeIntervals()
    {
        var heartbeats = new WorkerHeartbeats(_clock);
        heartbeats.Register("notification-dispatch", TimeSpan.FromSeconds(2));

        Assert.Equal(HealthStatus.Healthy, heartbeats.Check().Status);
        _clock.Advance(TimeSpan.FromSeconds(7));
        Assert.Equal(HealthStatus.Unhealthy, heartbeats.Check().Status);
    }
}
