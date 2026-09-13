using Avtomagazin.ApiClient;
using Avtomagazin.Contracts;

namespace Avtomagazin.Admin.Components.Pages;

public partial class Users
{
    private string? _error;
    private List<AccountDto> _pending = [];
    private List<AccountDto> _all = [];
    private List<VehicleDto> _vans = [];
    private Dictionary<Guid, string> _vanByUser = [];
    private Guid? _resetId;
    private string _resetPassword = "";
    private string _segment = "staff";
    private string _staffRoleFilter = "all";
    private string _staffStatusFilter = "all";
    private string _staffAssignFilter = "all";
    private string _residentStatusFilter = "all";
    private int _residentSkip;
    private int _residentTotal;
    private const int ResidentPageSize = 50;

    private IEnumerable<AccountDto> Filtered
    {
        get
        {
            IEnumerable<AccountDto> items = _segment == "residents"
                ? _all.Where(u => u.Role == Roles.Resident)
                : _all.Where(u => u.Role != Roles.Resident);

            if (_segment == "staff" && _staffRoleFilter is not "all")
            {
                items = items.Where(u => u.Role == _staffRoleFilter);
            }

            var statusFilter = _segment == "residents" ? _residentStatusFilter : _staffStatusFilter;
            if (statusFilter is not "all")
            {
                items = items.Where(u => u.Status == statusFilter);
            }

            if (_segment == "staff" && _staffAssignFilter is not "all")
            {
                items = _staffAssignFilter switch
                {
                    "assigned" => items.Where(u => u.VehicleId is not null),
                    "free" => items.Where(u => u.VehicleId is null && Roles.IsVanCrew(u.Role)),
                    _ => items
                };
            }

            return items
                .OrderBy(u => u.Role switch
                {
                    Roles.Admin => 0,
                    Roles.Operator => 1,
                    Roles.Driver => 2,
                    Roles.Seller => 3,
                    Roles.Resident => 4,
                    _ => 9
                })
                .ThenBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase);
        }
    }

    private string SegmentOn(string s) => _segment == s ? "chip-on" : null!;
    private string RoleOn(string role) => _staffRoleFilter == role ? "chip-on" : null!;
    private string StatusOn(string status)
        => (_segment == "residents" ? _residentStatusFilter : _staffStatusFilter) == status ? "chip-on" : null!;
    private string AssignOn(string a) => _staffAssignFilter == a ? "chip-on" : null!;

    private async Task SetSegment(string segment)
    {
        if (_segment == segment)
        {
            return;
        }

        _segment = segment;
        _residentSkip = 0;
        await ReloadAsync();
    }

    private Task SetRole(string role)
    {
        _staffRoleFilter = role;
        return Task.CompletedTask;
    }

    private Task SetStatus(string status)
    {
        if (_segment == "residents")
        {
            _residentStatusFilter = status;
            _residentSkip = 0;
            return ReloadAsync();
        }

        _staffStatusFilter = status;
        return Task.CompletedTask;
    }

    private Task SetAssign(string assign)
    {
        _staffAssignFilter = assign;
        return Task.CompletedTask;
    }

    private Task NextResidentsAsync()
    {
        if (_residentSkip + ResidentPageSize >= _residentTotal)
        {
            return Task.CompletedTask;
        }

        _residentSkip += ResidentPageSize;
        return ReloadAsync();
    }

    private Task PrevResidentsAsync()
    {
        _residentSkip = Math.Max(0, _residentSkip - ResidentPageSize);
        return ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            var staff = await Api.GetUsersAsync(kind: "staff", take: 200);
            var staffItems = staff.Items;
            _pending = staffItems.Where(u => u.Status == UserStatuses.Pending).ToList();
            if (_segment == "residents")
            {
                var status = _residentStatusFilter is "all" or "" ? null : _residentStatusFilter;
                var page = await Api.GetUsersAsync(
                    kind: "resident",
                    status: status,
                    skip: _residentSkip,
                    take: ResidentPageSize);
                _all = page.Items;
                _residentTotal = page.Total;
            }
            else
            {
                _all = staffItems;
            }

            _vans = await Api.GetVehiclesAsync();
            foreach (var u in _pending.Where(x =>
                         Roles.IsVanCrew(x.Role) && !_vanByUser.ContainsKey(x.Id)))
            {
                _vanByUser[u.Id] = u.VehicleId?.ToString() ?? "";
            }

            _error = null;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private async Task ApproveAsync(AccountDto user)
    {
        try
        {
            Guid? van = user.VehicleId;
            if (Roles.IsVanCrew(user.Role)
                && _vanByUser.TryGetValue(user.Id, out var raw)
                && Guid.TryParse(raw, out var picked))
            {
                van = picked;
            }

            await Api.ApproveUserAsync(user.Id, user.Role, van);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private async Task RejectAsync(AccountDto user)
    {
        try
        {
            await Api.RejectUserAsync(user.Id);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private async Task DisableAsync(AccountDto user)
    {
        try
        {
            await Api.DisableUserAsync(user.Id);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private void ToggleReset(Guid id)
    {
        _resetId = _resetId == id ? null : id;
        _resetPassword = "";
    }

    private async Task ResetPasswordAsync()
    {
        if (_resetId is not Guid id)
        {
            return;
        }

        try
        {
            await Api.ResetUserPasswordAsync(id, _resetPassword);
            _resetId = null;
            _resetPassword = "";
            _error = null;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private string VanOf(Guid id)
        => _vanByUser.GetValueOrDefault(id) ?? "";

    private void SetVan(Guid id, string? value)
        => _vanByUser[id] = value ?? "";

    private string VanLabel(AccountDto u)
    {
        if (u.VehicleId is not Guid id)
        {
            return Roles.IsVanCrew(u.Role) ? "—" : "";
        }

        var van = _vans.FirstOrDefault(v => v.Id == id);
        return van is null ? "назначена" : van.PlateNumber;
    }
}
