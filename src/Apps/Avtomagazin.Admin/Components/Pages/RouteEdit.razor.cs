using Avtomagazin.Admin;
using Avtomagazin.ApiClient;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Avtomagazin.Admin.Components.Pages;

public partial class RouteEdit
{
    [Parameter] public Guid? RouteId { get; set; }

    private bool IsNew => RouteId is null || RouteId == Guid.Empty;

    private readonly List<RouteDraftStop> _stops = [];
    private List<VehicleDto> _vehicles = [];
    private string _name = "";
    private Guid _vehicleId;
    private string _region = "BY-MI";
    private string? _error;
    private string? _ok;
    private bool _busy;
    private bool _mapReady;
    private IJSObjectReference? _map;
    private DotNetObjectReference<RouteEdit>? _self;
    private bool _gateReady;

    private async Task BootstrapAsync()
    {
        _vehicles = await Api.GetVehiclesAsync();
        if (!IsNew)
        {
            await LoadRouteAsync();
        }

        _gateReady = true;
    }

    private async Task LoadRouteAsync()
    {
        if (RouteId is not Guid id)
        {
            return;
        }

        try
        {
            var route = await Api.GetRouteAsync(id);
            if (route is null)
            {
                Fail("Маршрут не найден");
                return;
            }

            _name = route.Name;
            _vehicleId = route.VehicleId;
            _stops.Clear();
            foreach (var s in (route.Stops ?? []).OrderBy(x => x.Sequence))
            {
                _stops.Add(FromDto(s));
                if (!string.IsNullOrWhiteSpace(s.RegionCode))
                {
                    _region = s.RegionCode;
                }
            }

            _error = null;
            if (_mapReady)
            {
                await DrawRouteAsync(fit: true);
            }
        }
        catch (SessionExpiredException)
        {
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    private async Task ReloadAsync()
    {
        _ok = null;
        await LoadRouteAsync();
        await DrawRouteAsync(fit: true);
    }

    private async Task SaveAsync()
    {
        _error = null;
        _ok = null;

        await Session.RestoreAsync();
        if (string.IsNullOrEmpty(Session.AccessToken))
        {
            Session.NotifyUnauthorized();
            return;
        }

        if (string.IsNullOrWhiteSpace(_name) || _vehicleId == Guid.Empty)
        {
            Fail("Укажите название и автолавку");
            return;
        }

        if (_stops.Count == 0)
        {
            Fail("Добавьте хотя бы одну остановку на карте");
            return;
        }

        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            Guid routeId;
            if (IsNew)
            {
                var created = await Api.CreateRouteAsync(new CreateRouteRequest(_name.Trim(), _vehicleId));
                if (created is null)
                {
                    Fail("Не удалось создать маршрут");
                    return;
                }

                routeId = created.Id;
            }
            else
            {
                routeId = RouteId!.Value;
                await Api.UpdateRouteAsync(routeId, new UpdateRouteRequest(_name.Trim(), _vehicleId));
            }

            var region = string.IsNullOrWhiteSpace(_region) ? "BY-MI" : _region.Trim();
            if (_stops.Any(s => s.PlannedArrivalUtc is null))
            {
                Fail("Укажите время прибытия для каждой остановки");
                return;
            }

            var items = _stops.Select((s, i) => new ReplaceStopItem(
                s.Id,
                i + 1,
                string.IsNullOrWhiteSpace(s.Name) ? $"Точка {i + 1}" : s.Name,
                region,
                s.Lat,
                s.Lng,
                s.PlannedArrivalUtc,
                s.PhotoDirty && !s.ClearPhoto ? s.PhotoDataUrl : null,
                s.ClearPhoto)).ToList();

            var saved = await Api.ReplaceRouteStopsAsync(routeId, items);
            if (saved is not null)
            {
                _stops.Clear();
                foreach (var s in (saved.Stops ?? []).OrderBy(x => x.Sequence))
                {
                    _stops.Add(FromDto(s));
                }
            }

            if (IsNew)
            {
                Toasts.Success("Маршрут создан");
                Nav.NavigateTo($"routes/{routeId}");
                return;
            }

            await DrawRouteAsync(fit: false);
            Ok("Сохранено");
        }
        catch (SessionExpiredException)
        {
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (IsNew || RouteId is null || RouteId == Guid.Empty || _busy)
        {
            return;
        }

        _error = null;
        _ok = null;
        _busy = true;
        try
        {
            await Session.RestoreAsync();
            if (string.IsNullOrEmpty(Session.AccessToken))
            {
                Session.NotifyUnauthorized();
                return;
            }

            await Api.DeleteRouteAsync(RouteId.Value);
            Toasts.Success("Маршрут удалён");
            Nav.NavigateTo("routes");
        }
        catch (SessionExpiredException)
        {
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private static RouteDraftStop FromDto(RouteStopDto s) => new()
    {
        Id = s.Id,
        Name = s.SettlementName,
        Lat = s.Latitude,
        Lng = s.Longitude,
        PlannedArrivalUtc = s.PlannedArrivalUtc,
        PhotoDataUrl = s.PhotoDataUrl
    };

    private void Fail(string message)
    {
        _error = message;
        _ok = null;
        Toasts.Error(message);
    }

    private void Ok(string message)
    {
        _ok = message;
        _error = null;
        Toasts.Success(message);
    }
}
