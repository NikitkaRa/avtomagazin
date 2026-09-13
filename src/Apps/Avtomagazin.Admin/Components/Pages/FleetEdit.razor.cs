using Avtomagazin.ApiClient;
using Avtomagazin.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Avtomagazin.Admin.Components.Pages;

public partial class FleetEdit
{
    [Parameter] public Guid? VehicleId { get; set; }

    private bool IsNew => VehicleId is null || VehicleId == Guid.Empty;

    private bool _loading = true;
    private bool _busy;
    private string? _error;
    private string? _ok;
    private List<StaffMemberDto> _staff = [];

    private string _plate = "";
    private string _operator = "";
    private string _operatorPhone = "";
    private string _driverName = "";
    private string _driverPhone = "";
    private string _sellerName = "";
    private string _sellerPhone = "";
    private Guid _driverUserId;
    private Guid _sellerUserId;
    private Guid _assignedDriverUserId;
    private Guid _assignedSellerUserId;
    private string _isActiveText = "true";
    private string? _photoDataUrl;
    private bool _clearPhoto;
    private bool _photoDirty;

    private IEnumerable<StaffMemberDto> Drivers =>
        _staff.Where(s => s.Role == Roles.Driver);

    private IEnumerable<StaffMemberDto> Sellers =>
        _staff.Where(s => s.Role == Roles.Seller);

    private async Task BootstrapAsync()
    {
        try
        {
            _staff = await Api.GetStaffAsync();
        }
        catch (Exception ex)
        {
            _staff = [];
            _error = $"Не удалось загрузить сотрудников: {ex.Message}";
        }

        if (!IsNew)
        {
            await LoadAsync();
        }
        else
        {
            _loading = false;
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            var v = await Api.GetVehicleAsync(VehicleId!.Value);
            if (v is null)
            {
                _error = "Автолавка не найдена";
                return;
            }

            _plate = v.PlateNumber;
            _operator = v.OperatorName;
            _operatorPhone = v.OperatorPhone ?? "";
            _driverName = v.DriverName ?? "";
            _driverPhone = v.DriverPhone ?? "";
            _sellerName = v.SellerName ?? "";
            _sellerPhone = v.SellerPhone ?? "";
            _driverUserId = v.DriverUserId ?? Guid.Empty;
            _sellerUserId = v.SellerUserId ?? Guid.Empty;
            _assignedDriverUserId = _driverUserId;
            _assignedSellerUserId = _sellerUserId;
            _isActiveText = v.IsActive ? "true" : "false";
            _photoDataUrl = v.PhotoDataUrl;
            _clearPhoto = false;
            _photoDirty = false;
            _error = null;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnDriverPicked()
    {
        if (_driverUserId == Guid.Empty)
        {
            _driverName = "";
            _driverPhone = "";
            return;
        }

        var person = _staff.FirstOrDefault(s => s.Id == _driverUserId);
        if (person is null)
        {
            return;
        }

        _driverName = person.DisplayName;
        _driverPhone = "";
    }

    private void OnSellerPicked()
    {
        if (_sellerUserId == Guid.Empty)
        {
            _sellerName = "";
            _sellerPhone = "";
            return;
        }

        var person = _staff.FirstOrDefault(s => s.Id == _sellerUserId);
        if (person is null)
        {
            return;
        }

        _sellerName = person.DisplayName;
        _sellerPhone = "";
    }

    private async Task OnPhotoSelected(InputFileChangeEventArgs e)
    {
        _error = null;
        try
        {
            var file = e.File;
            if (file.Size > 400_000)
            {
                _error = "Фото больше 400 КБ";
                return;
            }

            await using var stream = file.OpenReadStream(maxAllowedSize: 400_000);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var b64 = Convert.ToBase64String(ms.ToArray());
            var mime = string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType;
            _photoDataUrl = $"data:{mime};base64,{b64}";
            _clearPhoto = false;
            _photoDirty = true;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private void ClearPhoto()
    {
        _photoDataUrl = null;
        _clearPhoto = true;
        _photoDirty = true;
    }

    private async Task SaveAsync()
    {
        _error = null;
        _ok = null;
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var request = new UpsertVehicleRequest(
                _plate,
                _operator,
                OperatorPhone: string.IsNullOrWhiteSpace(_operatorPhone) ? null : _operatorPhone,
                PhotoDataUrl: _photoDirty && !_clearPhoto ? _photoDataUrl : null,
                ClearPhoto: _clearPhoto,
                IsActive: _isActiveText == "true");

            VehicleDto? saved;
            if (IsNew)
            {
                saved = await Api.CreateVehicleAsync(request);
                if (saved is null)
                {
                    _error = "Не удалось создать";
                    return;
                }

                _ = await SyncDriverAssignmentAsync(saved.Id);
                Nav.NavigateTo($"fleet/{saved.Id}");
                return;
            }

            saved = await Api.UpdateVehicleAsync(VehicleId!.Value, request);
            if (saved is null)
            {
                _error = "Не удалось сохранить";
                return;
            }

            if (!await SyncDriverAssignmentAsync(saved.Id))
            {
                return;
            }

            _plate = saved.PlateNumber;
            _photoDataUrl = saved.PhotoDataUrl;
            _clearPhoto = false;
            _photoDirty = false;
            _ok = "Сохранено";
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<bool> SyncDriverAssignmentAsync(Guid vehicleId)
    {
        try
        {
            if (_driverUserId != Guid.Empty)
            {
                await Api.AssignUserVehicleAsync(_driverUserId, vehicleId);
            }
            else if (_assignedDriverUserId != Guid.Empty)
            {
                await Api.AssignUserVehicleAsync(_assignedDriverUserId, null);
            }

            if (_sellerUserId != Guid.Empty)
            {
                await Api.AssignUserVehicleAsync(_sellerUserId, vehicleId);
            }
            else if (_assignedSellerUserId != Guid.Empty)
            {
                await Api.AssignUserVehicleAsync(_assignedSellerUserId, null);
            }

            _assignedDriverUserId = _driverUserId;
            _assignedSellerUserId = _sellerUserId;
            return true;
        }
        catch (Exception ex)
        {
            _error = $"Автолавка сохранена, но назначение экипажа: {ex.Message}";
            return false;
        }
    }
}
