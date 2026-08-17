using Avtomagazin.ApiClient;

namespace Avtomagazin.MauiShared;

public static class MauiGateway
{
    public static string EnvironmentName =>
#if APP_STAGING
        AvtomagazinEnvironments.Staging;
#elif APP_PROD
        AvtomagazinEnvironments.Production;
#else
        AvtomagazinEnvironments.Development;
#endif

    public static string Resolve()
    {
#if APP_STAGING
        return AvtomagazinEnvironments.StagingGateway;
#elif APP_PROD
        return AvtomagazinEnvironments.ProductionGateway;
#else
        return Local();
#endif
    }

    public static string Local()
    {
        if (DeviceInfo.Platform == DevicePlatform.Android && DeviceInfo.DeviceType == DeviceType.Virtual)
        {
            return "http://10.0.2.2:5100";
        }

        return AvtomagazinEnvironments.LocalGateway;
    }
}

public sealed class AppFlavor
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public bool AllowRegister { get; init; }
    public required IReadOnlySet<string> AllowedRoles { get; init; }

    public string Client => AllowedRoles.Contains(Roles.Resident) && !AllowedRoles.Contains(Roles.Driver)
        ? AuthClients.Resident
        : AuthClients.Staff;

    public static AppFlavor Resident { get; } = new()
    {
        Title = "Автомагазин",
        Subtitle = "",
        AllowRegister = true,
        AllowedRoles = new HashSet<string>(StringComparer.Ordinal) { Roles.Resident }
    };

    public static AppFlavor Staff { get; } = new()
    {
        Title = "Автомагазин",
        Subtitle = "",
        AllowRegister = true,
        AllowedRoles = new HashSet<string>(StringComparer.Ordinal)
        {
            Roles.Driver, Roles.Seller, Roles.Operator, Roles.Admin
        }
    };
}

public interface IAppHost
{
    void ShowSignedIn();
    void ShowLogin();
    void SignOut();
}
