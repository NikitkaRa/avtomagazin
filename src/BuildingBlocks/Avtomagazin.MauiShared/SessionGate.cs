using Avtomagazin.ApiClient;
using Microsoft.Extensions.DependencyInjection;

namespace Avtomagazin.MauiShared;

/// <summary>Picks login vs main shell. Never block the UI thread on SecureStorage.</summary>
public static class SessionGate
{
    public static void Apply(IServiceProvider services, IAppHost host)
    {
        host.ShowLogin();
        _ = RestoreSessionAsync(services, host);
    }

    private static async Task RestoreSessionAsync(IServiceProvider services, IAppHost host)
    {
        var session = services.GetRequiredService<Session>();
        var flavor = services.GetRequiredService<AppFlavor>();
        try
        {
            await session.HydrateTokenAsync();
        }
        catch
        {
            return;
        }

        if (!session.IsAuthenticated)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!string.IsNullOrWhiteSpace(session.Role) && flavor.AllowedRoles.Contains(session.Role))
            {
                host.ShowSignedIn();
                _ = BootstrapAsync(services, session);
                return;
            }

            session.SignOut();
        });
    }

    public static async Task SignOutAsync(IServiceProvider services, IAppHost host)
    {
        var session = services.GetRequiredService<Session>();
        try
        {
            if (session.IsAuthenticated)
            {
                await services.GetRequiredService<ApiHub>().Client.UnregisterDeviceAsync(session.DeviceToken);
            }
        }
        catch
        {
            // Best-effort: local sign-out still proceeds.
        }

        session.SignOut();
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
            if (session.IsAuthenticated)
            {
                var refreshed = await api.Client.RefreshSessionAsync();
                if (refreshed is { AccessToken.Length: > 0 })
                {
                    await session.SignInAsync(refreshed);
                }
                else if (!session.IsAuthenticated)
                {
                    return;
                }
            }

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
