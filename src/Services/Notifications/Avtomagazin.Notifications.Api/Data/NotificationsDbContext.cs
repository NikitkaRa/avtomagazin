using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Notifications.Api.Data;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<DeviceSubscription> DeviceSubscriptions => Set<DeviceSubscription>();
    public DbSet<FavoriteStop> FavoriteStops => Set<FavoriteStop>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceSubscription>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.DeviceToken).IsUnique();
            e.Property(x => x.DeviceToken).HasMaxLength(512);
            e.Property(x => x.Platform).HasMaxLength(32);
            e.Property(x => x.SettlementName).HasMaxLength(200);
            e.HasIndex(x => x.UserId);
            e.HasMany(x => x.Favorites).WithOne(x => x.Device).HasForeignKey(x => x.DeviceSubscriptionId);
        });

        modelBuilder.Entity<FavoriteStop>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.DeviceSubscriptionId, x.StopId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.StopId })
                .IsUnique()
                .HasFilter("\"UserId\" IS NOT NULL");
            e.Property(x => x.SettlementName).HasMaxLength(200);
        });

        modelBuilder.Entity<NotificationLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Body).HasMaxLength(1000);
            e.Property(x => x.SettlementName).HasMaxLength(200);
        });
    }
}

public sealed class DeviceSubscription
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public required string DeviceToken { get; set; }
    public required string Platform { get; set; }
    public required string SettlementName { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public List<FavoriteStop> Favorites { get; set; } = [];
}

public sealed class FavoriteStop
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid DeviceSubscriptionId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public DeviceSubscription Device { get; set; } = null!;
    public Guid StopId { get; set; }
    public required string SettlementName { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class NotificationLog
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public required string SettlementName { get; set; }
    public DateTimeOffset SentAtUtc { get; set; }
    public int RecipientCount { get; set; }
}
