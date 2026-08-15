using Avtomagazin.ApiClient;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Avtomagazin.Admin;

public sealed class StaffSession : IAccessTokenAccessor
{
    private const string StoreKey = "avtomagazin.staff";
    private readonly ProtectedLocalStorage _store;
    private bool _restored;

    public StaffSession(ProtectedLocalStorage store) => _store = store;

    public string? Role { get; private set; }
    public string? Email { get; private set; }
    public string? Name { get; private set; }
    public Guid? VehicleId { get; private set; }
    public string? AccessToken { get; private set; }

    public bool IsAuthenticated => Role is not null;
    public bool IsDriver => Role == "driver";
    public bool IsAdmin => Role == "admin";
    public bool IsOperator => Role is "operator" or "admin";

    public event Action? Changed;

    public async Task RestoreAsync()
    {
        if (_restored)
        {
            return;
        }

        try
        {
            var result = await _store.GetAsync<StaffSnapshot>(StoreKey);
            if (result.Success && result.Value is { Role.Length: > 0 } saved)
            {
                Role = saved.Role;
                Email = saved.Email;
                Name = saved.Name;
                VehicleId = saved.VehicleId;
                AccessToken = saved.AccessToken;
            }

            _restored = true;
            Changed?.Invoke();
        }
        catch (InvalidOperationException)
        {
            // JS isn't ready until the circuit is connected.
        }
    }

    public void SignIn(string role, string email, string? name, Guid? vehicleId, string? accessToken)
    {
        Role = role;
        Email = email;
        Name = name;
        VehicleId = vehicleId;
        AccessToken = accessToken;
        _restored = true;
        Persist();
        Changed?.Invoke();
    }

    public void SignOut()
    {
        Role = null;
        Email = null;
        Name = null;
        VehicleId = null;
        AccessToken = null;
        _restored = true;
        Persist();
        Changed?.Invoke();
    }

    private void Persist() => _ = PersistCoreAsync();

    private async Task PersistCoreAsync()
    {
        try
        {
            if (Role is null)
            {
                await _store.DeleteAsync(StoreKey);
                return;
            }

            await _store.SetAsync(
                StoreKey,
                new StaffSnapshot(Role, Email, Name, VehicleId, AccessToken));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed record StaffSnapshot(
        string Role,
        string? Email,
        string? Name,
        Guid? VehicleId,
        string? AccessToken);
}
