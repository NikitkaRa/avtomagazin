using Avtomagazin.ApiClient;

namespace Avtomagazin.Mobile;

public sealed class Session : IAccessTokenAccessor
{
    public string Gateway { get; set; } = GatewayUrl.Default();
    public string? AccessToken { get; set; }
    public string? Role { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public Guid? VehicleId { get; set; }
    public string DeviceToken { get; }

    public bool IsDriver => Role == "driver";
    public bool IsOperator => Role is "operator" or "admin";
    public string Platform => DeviceInfo.Platform == DevicePlatform.iOS ? "ios" : "android";

    public Session()
    {
        Gateway = Preferences.Default.Get("gateway", GatewayUrl.Default());
        var token = Preferences.Default.Get("device", "");
        if (string.IsNullOrWhiteSpace(token))
        {
            token = $"{Platform}-{Guid.NewGuid():N}"[..20];
            Preferences.Default.Set("device", token);
        }

        DeviceToken = token;
    }

    public void RememberGateway() => Preferences.Default.Set("gateway", Gateway);

    public void SignIn(LoginResponse login)
    {
        AccessToken = login.AccessToken;
        Role = login.Role;
        Name = login.Name;
        Email = login.Email;
        VehicleId = login.VehicleId;
    }

    public void SignOut()
    {
        AccessToken = null;
        Role = null;
        Name = null;
        Email = null;
        VehicleId = null;
    }
}

public static class GatewayUrl
{
    public static string Default()
    {
        if (DeviceInfo.DeviceType == DeviceType.Virtual)
        {
            return DeviceInfo.Platform == DevicePlatform.Android
                ? "http://10.0.2.2:5100"
                : "http://127.0.0.1:5100";
        }

        return "http://192.168.1.7:5100";
    }
}
