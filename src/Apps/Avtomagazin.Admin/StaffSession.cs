using Avtomagazin.ApiClient;

namespace Avtomagazin.Admin;

public sealed class StaffSession : IAccessTokenAccessor
{
    public string? Role { get; private set; }
    public string? Email { get; private set; }
    public string? Name { get; private set; }
    public Guid? VehicleId { get; private set; }
    public string? AccessToken { get; private set; }

    public bool IsAuthenticated => Role is not null;
    public bool IsDriver => Role == "driver";
    public bool IsOperator => Role is "operator" or "admin";

    public event Action? Changed;

    public void SignIn(string role, string email, string? name, Guid? vehicleId, string? accessToken)
    {
        Role = role;
        Email = email;
        Name = name;
        VehicleId = vehicleId;
        AccessToken = accessToken;
        Changed?.Invoke();
    }

    public void SignOut()
    {
        Role = null;
        Email = null;
        Name = null;
        VehicleId = null;
        AccessToken = null;
        Changed?.Invoke();
    }
}
