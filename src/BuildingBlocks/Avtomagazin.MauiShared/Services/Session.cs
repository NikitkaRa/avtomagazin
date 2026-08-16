using Avtomagazin.ApiClient;

namespace Avtomagazin.MauiShared;

public sealed class Session : IAccessTokenAccessor
{
    public string Gateway { get; } = MauiGateway.Resolve();
    public string? AccessToken { get; set; }
    public string? Role { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public Guid? UserId { get; set; }
    public Guid? VehicleId { get; set; }
    public string DeviceToken { get; }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);
    public bool IsResident => Role == "resident";
    public bool IsDriver => Role == "driver";
    public bool IsSeller => Role == "seller";
    public bool IsVanCrew => Role is "driver" or "seller";
    public bool IsOperator => Role is "operator" or "admin";
    public string Platform => DeviceInfo.Platform == DevicePlatform.iOS ? "ios" : "android";

    public Session()
    {
        var token = Preferences.Default.Get("device", "");
        if (string.IsNullOrWhiteSpace(token))
        {
            token = $"{Platform}-{Guid.NewGuid():N}"[..20];
            Preferences.Default.Set("device", token);
        }

        DeviceToken = token;
        TryRestore();
    }

    public bool TryRestore()
    {
        var token = Preferences.Default.Get("accessToken", "");
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        AccessToken = token;
        Role = Preferences.Default.Get("role", "");
        Name = Preferences.Default.Get("name", "");
        Email = Preferences.Default.Get("email", "");
        UserId = Guid.TryParse(Preferences.Default.Get("userId", ""), out var id) ? id : null;
        VehicleId = Guid.TryParse(Preferences.Default.Get("vehicleId", ""), out var van) ? van : null;
        return IsAuthenticated && !string.IsNullOrWhiteSpace(Role);
    }

    public void SignIn(LoginResponse login)
    {
        AccessToken = login.AccessToken;
        Role = login.Role;
        Name = login.Name;
        Email = login.Email;
        UserId = login.UserId;
        VehicleId = login.VehicleId;
        Preferences.Default.Set("accessToken", login.AccessToken ?? "");
        Preferences.Default.Set("role", login.Role ?? "");
        Preferences.Default.Set("name", login.Name ?? "");
        Preferences.Default.Set("email", login.Email ?? "");
        Preferences.Default.Set("userId", login.UserId.ToString());
        Preferences.Default.Set("vehicleId", login.VehicleId?.ToString() ?? "");
    }

    public void SignOut()
    {
        AccessToken = null;
        Role = null;
        Name = null;
        Email = null;
        UserId = null;
        VehicleId = null;
        Preferences.Default.Remove("accessToken");
        Preferences.Default.Remove("role");
        Preferences.Default.Remove("name");
        Preferences.Default.Remove("email");
        Preferences.Default.Remove("userId");
        Preferences.Default.Remove("vehicleId");
    }
}
