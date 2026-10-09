using System.Buffers.Text;
using System.Security.Cryptography;
using HeartbeatWatchdog.Api.Data;
using HeartbeatWatchdog.Core;
using Microsoft.EntityFrameworkCore;

namespace HeartbeatWatchdog.Api.Services;

public enum CommandResult
{
    Ok,
    NotFound,
    SlugConflict,
}

/// <summary>所有會改變監控項目狀態的操作；在 <see cref="MonitorMutationLock"/> 內執行並一次存檔。</summary>
public sealed class MonitorCommands(
    WatchdogDbContext db,
    StatusChangeRecorder recorder,
    MonitorMutationLock mutationLock,
    TimeProvider timeProvider)
{
    private DateTime NowUtc => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<(CommandResult Result, HeartbeatMonitor? Monitor)> CreateAsync(MonitorDefinition definition, CancellationToken ct)
    {
        using var _ = await mutationLock.AcquireAsync(ct);
        if (await db.Monitors.AnyAsync(m => m.Slug == definition.Slug, ct))
        {
            return (CommandResult.SlugConflict, null);
        }

        var nowUtc = NowUtc;
        var monitor = HeartbeatMonitor.Create(definition, GeneratePingToken(), nowUtc);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Monitors.Add(monitor);
        await db.SaveChangesAsync(ct);
        db.StatusPeriods.Add(new StatusPeriod(MonitorStatus.New, nowUtc, null) { MonitorId = monitor.Id });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return (CommandResult.Ok, monitor);
    }

    public async Task<(CommandResult Result, HeartbeatMonitor? Monitor)> UpdateAsync(int id, MonitorDefinition definition, CancellationToken ct)
    {
        using var _ = await mutationLock.AcquireAsync(ct);
        var monitor = await db.Monitors.FindAsync([id], ct);
        if (monitor is null)
        {
            return (CommandResult.NotFound, null);
        }
        if (await db.Monitors.AnyAsync(m => m.Slug == definition.Slug && m.Id != id, ct))
        {
            return (CommandResult.SlugConflict, null);
        }

        monitor.ApplyDefinition(definition);
        await db.SaveChangesAsync(ct);
        return (CommandResult.Ok, monitor);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        using var _ = await mutationLock.AcquireAsync(ct);
        var deleted = await db.Monitors.Where(m => m.Id == id).ExecuteDeleteAsync(ct);
        return deleted > 0;
    }

    public Task<HeartbeatMonitor?> PauseAsync(int id, CancellationToken ct) =>
        ApplyAsync(id, (monitor, nowUtc) => monitor.Pause(nowUtc), ct);

    public Task<HeartbeatMonitor?> ResumeAsync(int id, CancellationToken ct) =>
        ApplyAsync(id, (monitor, nowUtc) => monitor.Resume(nowUtc), ct);

    /// <returns>找不到 token 時回傳 false。</returns>
    public async Task<bool> PingAsync(string token, PingKind kind, string? sourceIp, CancellationToken ct)
    {
        using var _ = await mutationLock.AcquireAsync(ct);
        var monitor = await db.Monitors.SingleOrDefaultAsync(m => m.PingToken == token, ct);
        if (monitor is null)
        {
            return false;
        }

        var nowUtc = NowUtc;
        var outcome = monitor.RecordPing(kind, nowUtc);
        db.Events.Add(MonitorEvent.Ping(monitor.Id, kind, nowUtc, sourceIp, outcome.Duration));
        if (outcome.Transition is { } transition)
        {
            await recorder.RecordAsync(monitor, transition, ct);
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<HeartbeatMonitor?> ApplyAsync(int id, Func<HeartbeatMonitor, DateTime, StatusTransition?> action, CancellationToken ct)
    {
        using var _ = await mutationLock.AcquireAsync(ct);
        var monitor = await db.Monitors.FindAsync([id], ct);
        if (monitor is null)
        {
            return null;
        }

        if (action(monitor, NowUtc) is { } transition)
        {
            await recorder.RecordAsync(monitor, transition, ct);
            await db.SaveChangesAsync(ct);
        }
        return monitor;
    }

    /// <summary>144 bits 隨機值，URL-safe base64（24 字元）。</summary>
    private static string GeneratePingToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(18));
}
