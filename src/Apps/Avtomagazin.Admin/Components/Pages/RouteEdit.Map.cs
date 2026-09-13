using Microsoft.JSInterop;

namespace Avtomagazin.Admin.Components.Pages;

public partial class RouteEdit
{
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_gateReady || _mapReady)
        {
            return;
        }

        try
        {
            _self ??= DotNetObjectReference.Create(this);
            _map ??= await Js.InvokeAsync<IJSObjectReference>("import", "./js/map.js");

            double startLat = 53.55;
            double startLng = 26.4;
            var zoom = 7;
            if (_stops.Count > 0)
            {
                startLat = _stops.Average(s => s.Lat);
                startLng = _stops.Average(s => s.Lng);
                zoom = _stops.Count == 1 ? 14 : 11;
            }

            var ok = await _map.InvokeAsync<bool>("init", "route-editor-map", startLat, startLng, zoom);
            if (!ok)
            {
                return;
            }

            await _map.InvokeAsync<bool>("enableRouteClicks", "route-editor-map", _self);
            _mapReady = true;

            if (_stops.Count > 0)
            {
                await DrawRouteAsync(fit: true);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (JSDisconnectedException)
        {
        }
        catch (Exception ex)
        {
            Fail($"Карта: {ex.Message}");
        }
    }

    [JSInvokable]
    public async Task OnMapClick(double lat, double lng)
    {
        _stops.Add(new RouteDraftStop
        {
            Name = $"Точка {_stops.Count + 1}",
            Lat = lat,
            Lng = lng,
            PlannedArrivalUtc = DateTimeOffset.UtcNow.AddMinutes(20 * (_stops.Count + 1))
        });
        _ok = null;
        await DrawRouteAsync(fit: false);
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task OnStopDrag(int index, double lat, double lng)
    {
        if (index < 0 || index >= _stops.Count)
        {
            return Task.CompletedTask;
        }

        _stops[index].Lat = lat;
        _stops[index].Lng = lng;
        return InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task OnStopDragEnd(int index, double lat, double lng)
    {
        if (index < 0 || index >= _stops.Count)
        {
            return Task.CompletedTask;
        }

        _stops[index].Lat = lat;
        _stops[index].Lng = lng;
        _ok = null;
        return InvokeAsync(StateHasChanged);
    }

    private async Task DrawRouteAsync(bool fit)
    {
        if (_map is null)
        {
            return;
        }

        var payload = _stops.Select((s, i) => new
        {
            lat = s.Lat,
            lng = s.Lng,
            seq = i + 1,
            label = s.Name
        }).ToArray();

        await _map.InvokeVoidAsync("setRouteStops", "route-editor-map", payload, fit);
    }

    public async ValueTask DisposeAsync()
    {
        _self?.Dispose();
        if (_map is not null)
        {
            try
            {
                await _map.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }
}
