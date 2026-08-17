using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Avtomagazin.Notifications.Api.Data;

public sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql("Host=localhost;Database=avtomagazin_notifications;Username=avtomagazin;Password=avtomagazin")
            .Options;
        return new NotificationsDbContext(options);
    }
}
