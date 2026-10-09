using HeartbeatWatchdog.Core;

namespace HeartbeatWatchdog.Tests.Core;

public class MonitorStateMachineTests
{
    private static readonly DateTime T0 = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>每 60 分鐘一次，寬限 10 分鐘。</summary>
    private static HeartbeatMonitor NewHourlyMonitor(int graceMinutes = 10) => HeartbeatMonitor.Create(
        new MonitorDefinition("Hourly sync", "hourly-sync", ScheduleType.Interval, 60, null, graceMinutes, "UTC", null),
        pingToken: "token",
        nowUtc: T0);

    private static HeartbeatMonitor UpMonitor(int graceMinutes = 10)
    {
        var monitor = NewHourlyMonitor(graceMinutes);
        monitor.RecordPing(PingKind.Success, T0);
        return monitor;
    }

    [Fact]
    public void NewMonitor_StartsInNew_WithoutExpectation()
    {
        var monitor = NewHourlyMonitor();

        Assert.Equal(MonitorStatus.New, monitor.Status);
        Assert.Null(monitor.NextExpectedAtUtc);
        Assert.Null(monitor.Evaluate(T0.AddDays(10)));
    }

    [Fact]
    public void SuccessPing_FromNew_GoesUp_WithoutNotification()
    {
        var monitor = NewHourlyMonitor();

        var outcome = monitor.RecordPing(PingKind.Success, T0);

        Assert.Equal(MonitorStatus.Up, monitor.Status);
        Assert.Equal(new StatusTransition(MonitorStatus.New, MonitorStatus.Up, T0, TransitionReason.SuccessPing), outcome.Transition);
        Assert.Null(outcome.Transition!.Notification);
        Assert.Equal(T0.AddMinutes(60), monitor.NextExpectedAtUtc);
    }

    [Fact]
    public void Evaluate_BeforeExpectedTime_StaysUp()
    {
        var monitor = UpMonitor();

        Assert.Null(monitor.Evaluate(T0.AddMinutes(60)));
        Assert.Equal(MonitorStatus.Up, monitor.Status);
    }

    [Fact]
    public void Evaluate_PastExpectedButWithinGrace_IsLate_WithoutNotification()
    {
        var monitor = UpMonitor(graceMinutes: 10);

        var transition = monitor.Evaluate(T0.AddMinutes(65));

        Assert.Equal(MonitorStatus.Late, monitor.Status);
        Assert.Equal(TransitionReason.ExpectedTimeMissed, transition!.Reason);
        Assert.Null(transition.Notification);
    }

    [Fact]
    public void Evaluate_ExactlyAtGraceDeadline_IsStillLate()
    {
        var monitor = UpMonitor(graceMinutes: 10);

        monitor.Evaluate(T0.AddMinutes(70));

        Assert.Equal(MonitorStatus.Late, monitor.Status);
    }

    [Fact]
    public void Evaluate_PastGrace_IsDown_WithDownNotification()
    {
        var monitor = UpMonitor(graceMinutes: 10);
        monitor.Evaluate(T0.AddMinutes(65));

        var transition = monitor.Evaluate(T0.AddMinutes(70).AddSeconds(1));

        Assert.Equal(MonitorStatus.Down, monitor.Status);
        Assert.Equal(new StatusTransition(MonitorStatus.Late, MonitorStatus.Down, T0.AddMinutes(70).AddSeconds(1), TransitionReason.GracePeriodExceeded), transition);
        Assert.Equal(NotificationKind.Down, transition!.Notification);
    }

    [Fact]
    public void Evaluate_ZeroGrace_GoesStraightFromUpToDown()
    {
        var monitor = UpMonitor(graceMinutes: 0);

        var transition = monitor.Evaluate(T0.AddMinutes(61));

        Assert.Equal(MonitorStatus.Up, transition!.From);
        Assert.Equal(MonitorStatus.Down, transition.To);
    }

    [Fact]
    public void Evaluate_WhileAlreadyDown_NeverProducesAnotherTransition()
    {
        var monitor = UpMonitor();
        Assert.NotNull(monitor.Evaluate(T0.AddHours(2)));

        for (var tick = 1; tick <= 100; tick++)
        {
            Assert.Null(monitor.Evaluate(T0.AddHours(2).AddSeconds(30 * tick)));
        }
        Assert.Equal(MonitorStatus.Down, monitor.Status);
    }

    [Fact]
    public void FailPing_GoesDownImmediately_AndOnlyNotifiesOnce()
    {
        var monitor = UpMonitor();

        var first = monitor.RecordPing(PingKind.Fail, T0.AddMinutes(5));
        var second = monitor.RecordPing(PingKind.Fail, T0.AddMinutes(6));

        Assert.Equal(MonitorStatus.Down, monitor.Status);
        Assert.Equal(TransitionReason.FailPing, first.Transition!.Reason);
        Assert.Equal(NotificationKind.Down, first.Transition.Notification);
        Assert.Null(second.Transition);
    }

    [Fact]
    public void SuccessPing_FromDown_RecoversToUp_WithUpNotification()
    {
        var monitor = UpMonitor();
        monitor.RecordPing(PingKind.Fail, T0.AddMinutes(5));

        var outcome = monitor.RecordPing(PingKind.Success, T0.AddMinutes(20));

        Assert.Equal(MonitorStatus.Up, monitor.Status);
        Assert.Equal(NotificationKind.Up, outcome.Transition!.Notification);
        Assert.Equal(T0.AddMinutes(80), monitor.NextExpectedAtUtc);
    }

    [Fact]
    public void SuccessPing_FromLate_GoesUp_WithoutNotification()
    {
        var monitor = UpMonitor();
        monitor.Evaluate(T0.AddMinutes(65));

        var outcome = monitor.RecordPing(PingKind.Success, T0.AddMinutes(66));

        Assert.Equal(MonitorStatus.Up, monitor.Status);
        Assert.Null(outcome.Transition!.Notification);
    }

    [Fact]
    public void StartThenSuccess_ReportsRunDuration()
    {
        var monitor = UpMonitor();

        var start = monitor.RecordPing(PingKind.Start, T0.AddMinutes(60));
        var success = monitor.RecordPing(PingKind.Success, T0.AddMinutes(60).AddSeconds(754));

        Assert.Null(start.Duration);
        Assert.Null(start.Transition);
        Assert.Equal(TimeSpan.FromSeconds(754), success.Duration);
    }

    [Fact]
    public void SuccessWithoutStart_HasNoDuration_AndStartIsConsumedOnce()
    {
        var monitor = UpMonitor();
        monitor.RecordPing(PingKind.Start, T0.AddMinutes(1));
        monitor.RecordPing(PingKind.Success, T0.AddMinutes(2));

        var second = monitor.RecordPing(PingKind.Success, T0.AddMinutes(3));

        Assert.Null(second.Duration);
    }

    [Fact]
    public void Paused_IgnoresEvaluationAndPings()
    {
        var monitor = UpMonitor();
        monitor.Pause(T0.AddMinutes(1));

        Assert.Null(monitor.Evaluate(T0.AddDays(3)));
        var ping = monitor.RecordPing(PingKind.Fail, T0.AddDays(3));

        Assert.Equal(MonitorStatus.Paused, monitor.Status);
        Assert.Null(ping.Transition);
        Assert.Null(monitor.NextExpectedAtUtc);
    }

    [Fact]
    public void Resume_RecalculatesExpectationFromResumeTime_WithoutNotification()
    {
        var monitor = UpMonitor();
        monitor.Pause(T0.AddMinutes(1));
        var resumedAt = T0.AddDays(2);

        var transition = monitor.Resume(resumedAt);

        Assert.Equal(new StatusTransition(MonitorStatus.Paused, MonitorStatus.Up, resumedAt, TransitionReason.Resumed), transition);
        Assert.Null(transition!.Notification);
        Assert.Equal(resumedAt.AddMinutes(60), monitor.NextExpectedAtUtc);
        // 恢復當下不能因為暫停前的舊期限而立刻變成 Late / Down
        Assert.Null(monitor.Evaluate(resumedAt.AddMinutes(30)));
    }

    [Fact]
    public void Resume_OfNeverPingedMonitor_ReturnsToNew()
    {
        var monitor = NewHourlyMonitor();
        monitor.Pause(T0);

        monitor.Resume(T0.AddHours(1));

        Assert.Equal(MonitorStatus.New, monitor.Status);
        Assert.Null(monitor.NextExpectedAtUtc);
    }

    [Fact]
    public void PauseAndResume_AreIdempotent()
    {
        var monitor = UpMonitor();

        Assert.Null(monitor.Resume(T0.AddMinutes(1)));
        Assert.NotNull(monitor.Pause(T0.AddMinutes(2)));
        Assert.Null(monitor.Pause(T0.AddMinutes(3)));
    }

    [Fact]
    public void Resume_AfterBeingDown_DoesNotSendRecoveryNotification()
    {
        var monitor = UpMonitor();
        monitor.RecordPing(PingKind.Fail, T0.AddMinutes(1));
        monitor.Pause(T0.AddMinutes(2));

        var transition = monitor.Resume(T0.AddMinutes(3));

        Assert.Equal(MonitorStatus.Up, transition!.To);
        Assert.Null(transition.Notification);
    }

    [Fact]
    public void ApplyDefinition_RecalculatesExpectationFromLastPing()
    {
        var monitor = UpMonitor();

        monitor.ApplyDefinition(
            new MonitorDefinition("Hourly sync", "hourly-sync", ScheduleType.Interval, 30, null, 10, "UTC", null));

        Assert.Equal(T0.AddMinutes(30), monitor.NextExpectedAtUtc);
    }
}
