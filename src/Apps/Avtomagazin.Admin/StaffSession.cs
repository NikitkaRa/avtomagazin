using Avtomagazin.ApiClient;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Avtomagazin.Admin;

public sealed class StaffSession : IAccessTokenAccessor
{
    private const string LocalKey = "avtomagazin.staff";
    private const string SessionKey = "avtomagazin.staff.session";

    private readonly ProtectedLocalStorage _local;
    private readonly ProtectedSessionStorage _session;
    private bool _restored;
    private bool _remember = true;
    private bool _persistDirty;

    public StaffSession(ProtectedLocalStorage local, ProtectedSessionStorage session)
    {
        _local = local;
        _session = session;
    }

    public string? Role { get; private set; }
    public string? Email { get; private set; }
    public string? Name { get; private set; }
    public Guid? VehicleId { get; private set; }
    public string? AccessToken { get; private set; }

    public bool IsAuthenticated => Role is not null && AccessToken is { Length: > 0 };
    public bool IsDriver => Role == Roles.Driver;
    public bool IsSeller => Role == Roles.Seller;
    public bool IsVanCrew => Roles.IsVanCrew(Role ?? "");
    public bool IsAdmin => Role == Roles.Admin;
    public bool IsOperator => Role is Roles.Operator or Roles.Admin;

    /// <summary>True after browser storage was read (or sign-in/out). False while JS/circuit is not ready.</summary>
    public bool IsRestored => _restored;

    public SignOutReason? LastSignOutReason { get; private set; }

    public event Action? Changed;

    public async Task RestoreAsync()
    {
        if (_restored)
        {
            return;
        }

        try
        {
            if (await TryRestoreAsync(_local.GetAsync<StaffSnapshot>(LocalKey)))
            {
                _remember = true;
            }
            else if (await TryRestoreAsync(_session.GetAsync<StaffSnapshot>(SessionKey)))
            {
                _remember = false;
            }

            _restored = true;
            if (_persistDirty)
            {
                await PersistCoreAsync();
            }

            Changed?.Invoke();
        }
        catch (InvalidOperationException)
        {
            // JS isn't ready until the circuit is connected — do not mark restored.
        }
    }

    /// <summary>Restore from browser storage when possible. Returns false if not signed in (only after restore).</summary>
    public async Task<bool> EnsureAuthenticatedAsync()
    {
        await RestoreAsync();
        return IsRestored && IsAuthenticated;
    }

    private async Task<bool> TryRestoreAsync(ValueTask<ProtectedBrowserStorageResult<StaffSnapshot>> pending)
    {
        var result = await pending;
        if (!result.Success || result.Value is not { Role.Length: > 0, AccessToken.Length: > 0 } saved)
        {
            return false;
        }

        Role = saved.Role;
        Email = saved.Email;
        Name = saved.Name;
        VehicleId = saved.VehicleId;
        AccessToken = saved.AccessToken;
        return true;
    }

    public async Task SignInAsync(string role, string email, string? name, Guid? vehicleId, string? accessToken, bool remember = true)
    {
        Role = role;
        Email = email;
        Name = name;
        VehicleId = vehicleId;
        AccessToken = accessToken;
        _remember = remember;
        _restored = true;
        LastSignOutReason = null;
        await PersistCoreAsync();
        Changed?.Invoke();
    }

    public Task ApplyLoginAsync(LoginResponse login, bool? remember = null)
        => SignInAsync(login.Role, login.Email, login.Name, login.VehicleId, login.AccessToken, remember ?? _remember);

    public void RefreshProfile(string? name)
    {
        Name = name;
        Persist();
        Changed?.Invoke();
    }

    public void SignOut()
        => _ = SignOutAsync();

    public Task SignOutAsync()
        => ClearAsync(SignOutReason.Manual);

    public void NotifyUnauthorized()
    {
        if (!IsAuthenticated && AccessToken is null)
        {
            return;
        }

        _ = ClearAsync(SignOutReason.Expired);
    }

    private async Task ClearAsync(SignOutReason reason)
    {
        Role = null;
        Email = null;
        Name = null;
        VehicleId = null;
        AccessToken = null;
        LastSignOutReason = reason;
        _remember = true;
        _persistDirty = true;
        await PersistCoreAsync();
        _restored = true;
        Changed?.Invoke();
    }

    private void Persist()
    {
        _persistDirty = true;
        _ = PersistCoreAsync();
    }

    private async Task PersistCoreAsync()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await _local.DeleteAsync(LocalKey);
                await _session.DeleteAsync(SessionKey);

                if (Role is null || AccessToken is null)
                {
                    _persistDirty = false;
                    return;
                }

                var snap = new StaffSnapshot(Role, Email, Name, VehicleId, AccessToken);
                if (_remember)
                {
                    await _local.SetAsync(LocalKey, snap);
                }
                else
                {
                    await _session.SetAsync(SessionKey, snap);
                }

                _persistDirty = false;
                return;
            }
            catch (InvalidOperationException)
            {
                _persistDirty = true;
                await Task.Delay(50 * (attempt + 1));
            }
        }
    }

    private sealed record StaffSnapshot(
        string Role,
        string? Email,
        string? Name,
        Guid? VehicleId,
        string? AccessToken);
}

public enum SignOutReason
{
    Manual,
    Expired
}
