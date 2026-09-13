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
    public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }
    public string DeviceToken { get; }
    public bool TokenStorageDegraded { get; private set; }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);
    public bool IsResident => Role == Roles.Resident;
    public bool IsDriver => Role == Roles.Driver;
    public bool IsSeller => Role == Roles.Seller;
    public bool IsVanCrew => Roles.IsVanCrew(Role ?? "");
    public bool IsOperator => Role is Roles.Operator or Roles.Admin;
    public string Platform => DeviceInfo.Platform == DevicePlatform.iOS ? "ios" : "android";

    public event Action? Unauthorized;

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

    private const string TokenKey = "accessToken";
    private const string ExpiresKey = "accessTokenExpires";

    public bool TryRestore()
    {
        Role = Preferences.Default.Get("role", "");
        Name = Preferences.Default.Get("name", "");
        Email = Preferences.Default.Get("email", "");
        UserId = Guid.TryParse(Preferences.Default.Get("userId", ""), out var id) ? id : null;
        VehicleId = Guid.TryParse(Preferences.Default.Get("vehicleId", ""), out var van) ? van : null;
        if (DateTimeOffset.TryParse(Preferences.Default.Get(ExpiresKey, ""), out var expires))
        {
            AccessTokenExpiresAtUtc = expires;
        }

        AccessToken = null;
        return !string.IsNullOrWhiteSpace(Role);
    }

    public async Task HydrateTokenAsync()
    {
        try
        {
            var stored = await SecureStorage.Default.GetAsync(TokenKey);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                AccessToken = stored;
                Preferences.Default.Remove(TokenKey);
                return;
            }
        }
        catch (Exception)
        {
            TokenStorageDegraded = true;
        }

        var legacy = Preferences.Default.Get(TokenKey, "");
        Preferences.Default.Remove(TokenKey);
        if (string.IsNullOrWhiteSpace(legacy))
        {
            return;
        }

        AccessToken = legacy;
        try
        {
            await SecureStorage.Default.SetAsync(TokenKey, legacy);
        }
        catch (Exception)
        {
            TokenStorageDegraded = true;
        }
    }

    public async Task SignInAsync(LoginResponse login)
    {
        AccessToken = login.AccessToken;
        Role = login.Role;
        Name = login.Name;
        Email = login.Email;
        UserId = login.UserId;
        VehicleId = login.VehicleId;
        AccessTokenExpiresAtUtc = login.AccessTokenExpiresAtUtc;
        TokenStorageDegraded = false;
        Preferences.Default.Set("role", login.Role ?? "");
        Preferences.Default.Set("name", login.Name ?? "");
        Preferences.Default.Set("email", login.Email ?? "");
        Preferences.Default.Set("userId", login.UserId.ToString());
        Preferences.Default.Set("vehicleId", login.VehicleId?.ToString() ?? "");
        Preferences.Default.Set(ExpiresKey, login.AccessTokenExpiresAtUtc?.ToString("O") ?? "");
        Preferences.Default.Remove(TokenKey);
        try
        {
            if (!string.IsNullOrWhiteSpace(login.AccessToken))
            {
                await SecureStorage.Default.SetAsync(TokenKey, login.AccessToken);
            }
            else
            {
                SecureStorage.Default.Remove(TokenKey);
            }
        }
        catch (Exception)
        {
            TokenStorageDegraded = true;
        }
    }

    public void SignOut()
    {
        AccessToken = null;
        Role = null;
        Name = null;
        Email = null;
        UserId = null;
        VehicleId = null;
        AccessTokenExpiresAtUtc = null;
        TokenStorageDegraded = false;
        Preferences.Default.Remove(TokenKey);
        Preferences.Default.Remove(ExpiresKey);
        Preferences.Default.Remove("role");
        Preferences.Default.Remove("name");
        Preferences.Default.Remove("email");
        Preferences.Default.Remove("userId");
        Preferences.Default.Remove("vehicleId");
        try
        {
            SecureStorage.Default.Remove(TokenKey);
        }
        catch (Exception)
        {
        }
    }

    public void NotifyUnauthorized()
    {
        if (!IsAuthenticated && AccessToken is null)
        {
            return;
        }

        SignOut();
        Unauthorized?.Invoke();
    }
}
