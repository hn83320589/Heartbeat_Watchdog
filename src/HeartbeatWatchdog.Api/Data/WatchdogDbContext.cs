using HeartbeatWatchdog.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HeartbeatWatchdog.Api.Data;

public class WatchdogDbContext(DbContextOptions<WatchdogDbContext> options) : DbContext(options)
{
    public DbSet<HeartbeatMonitor> Monitors => Set<HeartbeatMonitor>();
    public DbSet<MonitorEvent> Events => Set<MonitorEvent>();
    public DbSet<StatusPeriod> StatusPeriods => Set<StatusPeriod>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationAttempt> NotificationAttempts => Set<NotificationAttempt>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // 資料庫一律存 UTC；讀回時標記 Kind = Utc，序列化才會帶 "Z"，也避免被誤當成伺服器本地時間。
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HeartbeatMonitor>(entity =>
        {
            entity.ToTable("Monitors");
            entity.Ignore(m => m.Schedule);
            entity.HasIndex(m => m.Slug).IsUnique();
            entity.HasIndex(m => m.PingToken).IsUnique();
            entity.HasIndex(m => m.Status);
            entity.Property(m => m.Name).HasMaxLength(200);
            entity.Property(m => m.Slug).HasMaxLength(100);
            entity.Property(m => m.PingToken).HasMaxLength(64);
            entity.Property(m => m.TimeZone).HasMaxLength(64);
        });

        modelBuilder.Entity<MonitorEvent>(entity =>
        {
            entity.HasOne<HeartbeatMonitor>().WithMany().HasForeignKey(e => e.MonitorId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.MonitorId, e.OccurredAtUtc });
        });

        modelBuilder.Entity<StatusPeriod>(entity =>
        {
            entity.HasOne<HeartbeatMonitor>().WithMany().HasForeignKey(p => p.MonitorId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(p => new { p.MonitorId, p.EndedAtUtc });
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasOne<HeartbeatMonitor>().WithMany().HasForeignKey(n => n.MonitorId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(n => n.Attempts).WithOne().HasForeignKey(a => a.NotificationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(n => new { n.Status, n.NextAttemptAtUtc });
        });
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => EnsureUtc(v),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v == null ? v : EnsureUtc(v.Value),
        v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));

    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : throw new InvalidOperationException($"Refusing to store non-UTC DateTime {value:O} ({value.Kind}).");
}
