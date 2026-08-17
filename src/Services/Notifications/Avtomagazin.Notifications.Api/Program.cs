using Avtomagazin.Notifications.Api.Consumers;
using Avtomagazin.Notifications.Api.Data;
using Avtomagazin.Notifications.Api.Endpoints;
using Avtomagazin.Notifications.Api.Push;
using Avtomagazin.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddAvtomagazinDefaults(bus =>
{
    bus.AddConsumer<DriverArrivedAtStopConsumer>();
    bus.AddConsumer<ScheduleChangedConsumer>();
});

var notificationsCs = DeploySecrets.ConnectionString(
    builder.Configuration,
    builder.Environment,
    "Notifications",
    "Host=localhost;Port=5432;Database=avtomagazin_notifications;Username=avtomagazin;Password=avtomagazin");

var notificationsTestDb = $"avtomagazin-notifications-tests-{Guid.NewGuid()}";
builder.Services.AddDbContext<NotificationsDbContext>(options =>
{
    if (builder.Environment.IsEnvironment("Testing"))
    {
        options.UseInMemoryDatabase(notificationsTestDb);
        return;
    }

    options.UseNpgsql(notificationsCs);
});

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.AddAvtomagazinPostgresHealth("postgres", notificationsCs);
}

if (FcmPushSender.IsConfigured(builder.Configuration))
{
    builder.Services.AddHttpClient<IPushSender, FcmPushSender>();
}
else
{
    builder.Services.AddSingleton<IPushSender, LoggingPushSender>();
}

var app = builder.Build();
app.UseAvtomagazinDefaults();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
    await RelationalSchema.ApplyAsync(db);

    if (app.Environment.IsDevelopment())
    {
        await HeatSeed.EnsureAsync(db);
    }
}

app.MapNotificationApi();
app.Run();

public partial class Program;
