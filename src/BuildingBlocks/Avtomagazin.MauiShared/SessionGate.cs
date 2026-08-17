using Avtomagazin.ApiClient;
using Microsoft.Extensions.DependencyInjection;

namespace Avtomagazin.MauiShared;

/// <summary>Chooses login vs main shell before the first page paints.</summary>
public static class SessionGate
{
    public static void Apply(IServiceProvider services, IAppHost host)
    {
        var session = services.GetRequiredService<Session>();
        var flavor = services.GetRequiredService<AppFlavor>();
        session.HydrateTokenAsync().GetAwaiter().GetResult();
        if (session.IsAuthenticated
            && !string.IsNullOrWhiteSpace(session.Role)
            && flavor.AllowedRoles.Contains(session.Role))
        {
            host.ShowSignedIn();
            _ = BootstrapAsync(services, session);
            return;
        }

        if (session.IsAuthenticated)
        {
            session.SignOut();
        }

        host.ShowLogin();
    }

    public static Task BootstrapAsync(IServiceProvider services, Session? session = null)
    {
        session ??= services.GetRequiredService<Session>();
        return BootstrapAsync(
            session,
            services.GetRequiredService<ApiHub>(),
            services.GetRequiredService<SnapshotStore>());
    }

    public static async Task BootstrapAsync(Session session, ApiHub api, SnapshotStore snapshot)
    {
        try
        {
            if (session.UserId is Guid uid)
            {
                var settlement = Preferences.Default.Get("settlement", "");
                await api.Client.RegisterDeviceAsync(new(
                    uid,
                    session.DeviceToken,
                    session.Platform,
                    settlement));
            }

            if (session.IsResident)
            {
                await FavoriteStore.PullAsync(api.Client);
            }

            await snapshot.LoadBootstrapAsync();
            snapshot.LoadCache();
            _ = snapshot.RefreshAsync(api.Client);
        }
        catch
        {
            // offline / cold start — screens will retry
        }
    }
}
