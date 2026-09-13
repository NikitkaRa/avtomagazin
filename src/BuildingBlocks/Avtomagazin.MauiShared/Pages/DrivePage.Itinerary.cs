using Avtomagazin.ApiClient;
using Microsoft.Maui.Controls.Shapes;

namespace Avtomagazin.MauiShared;

public partial class DrivePage
{
    private async Task RefreshProximityAsync()
    {
        if (_nextStop is null)
        {
            return;
        }

        await TryUpdateMyLocationAsync();
        PaintProximity();
    }

    private async Task TryUpdateMyLocationAsync()
    {
        // Prefer a live fix from broadcast; otherwise last-known / van ping.
        if (_gpsLive && _meLat is not null && _lastGpsAt is { } at
            && DateTimeOffset.UtcNow - at < TimeSpan.FromSeconds(25))
        {
            return;
        }

        try
        {
            var loc = await Geolocation.GetLastKnownLocationAsync();
            if (loc is null && _meLat is null)
            {
                loc = await Geolocation.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.Low, TimeSpan.FromSeconds(3)));
            }

            if (loc is not null)
            {
                _meLat = loc.Latitude;
                _meLng = loc.Longitude;
                if (!_gpsLive)
                {
                    _gpsAccuracyM = loc.Accuracy;
                }

                return;
            }
        }
        catch
        {
            // fall through to van last fix
        }

        if (_meLat is not null)
        {
            return;
        }

        var van = Vehicle();
        if (van?.LastLatitude is double lat && van.LastLongitude is double lng)
        {
            _meLat = lat;
            _meLng = lng;
        }
    }

    private void PaintProximity()
    {
        if (_nextStop is null)
        {
            NextMap.IsVisible = false;
            MapLegend.IsVisible = false;
            NextDistance.IsVisible = false;
            return;
        }

        PaintNextSchedule();
        NextMap.IsVisible = true;
        MapLegend.IsVisible = true;
        FleetMap.RenderFocus(
            NextMap,
            _nextStop.Latitude,
            _nextStop.Longitude,
            _nextStop.SettlementName,
            _meLat,
            _meLng,
            tiles: Connectivity.Current.NetworkAccess == NetworkAccess.Internet);

        if (_meLat is not double lat || _meLng is not double lng)
        {
            NextDistance.IsVisible = true;
            NextDistance.Text = "Нет GPS — включите на вкладке GPS или разрешите геолокацию";
            NextDistance.TextColor = Color.FromArgb("#F0C7B0");
            NextCard.Stroke = Color.FromArgb("#2A4638");
            ArriveBtn.BackgroundColor = Color.FromArgb("#3DBA7A");
            return;
        }

        var meters = GeoMath.DistanceMeters(lat, lng, _nextStop.Latitude, _nextStop.Longitude);
        var onSite = GeoFence.IsOnSite(lat, lng, _nextStop.Latitude, _nextStop.Longitude);
        NextDistance.IsVisible = true;
        NextDistance.Text = onSite
            ? "Вы на точке"
            : meters < 1000
                ? $"{meters:0} м до точки"
                : $"{meters / 1000:0.0} км до точки";
        NextDistance.TextColor = Color.FromArgb(onSite ? "#3DBA7A" : "#A7B8AD");
        NextCard.Stroke = Color.FromArgb(onSite ? "#3DBA7A" : "#2A4638");
        NextCard.StrokeThickness = onSite ? 2 : 1;
        ArriveBtn.BackgroundColor = Color.FromArgb(onSite ? "#4FD68F" : "#3DBA7A");
    }

    private void PaintNextSchedule()
    {
        if (_nextStop is null)
        {
            return;
        }

        var plan = DriveItinerary.DelayMeta(_nextStop, DateTimeOffset.UtcNow);
        var late = DateTimeOffset.UtcNow - _nextStop.PlannedArrivalUtc;
        NextMeta.Text = plan;
        NextMeta.TextColor = Color.FromArgb(late < TimeSpan.FromMinutes(1) ? "#A7B8AD" : "#F0C7B0");
    }

    private void RenderStops(DriveItinerary trip)
    {
        Stops.Children.Clear();
        foreach (var row in trip.Rows)
        {
            var stop = row.Stop;
            var done = row.Done;
            var isNext = row.IsNext;
            var status = row.Status;

            var card = new Border
            {
                Stroke = Color.FromArgb(isNext ? "#3DBA7A" : "#2A4638"),
                StrokeThickness = isNext ? 1.5 : 1,
                BackgroundColor = Color.FromArgb("#1B3328"),
                Padding = new Thickness(14, 12),
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Opacity = done ? 0.72 : 1
            };

            var grid = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                ],
                ColumnSpacing = 12
            };

            grid.Add(new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = $"{stop.Sequence}. {stop.SettlementName}",
                        TextColor = Color.FromArgb("#F3F7F3"),
                        FontAttributes = FontAttributes.Bold,
                        FontSize = 16
                    },
                    new Label
                    {
                        Text = status,
                        TextColor = stop.Skipped
                            ? Color.FromArgb("#F0C7B0")
                            : done
                                ? Color.FromArgb("#7BC9A0")
                                : Color.FromArgb("#6B7F74"),
                        FontSize = 12,
                        IsVisible = status.Length > 0
                    }
                }
            });

            grid.Add(new Label
            {
                Text = row.Time,
                TextColor = Color.FromArgb(isNext ? "#3DBA7A" : "#F3F7F3"),
                FontAttributes = FontAttributes.Bold,
                FontSize = 28,
                VerticalOptions = LayoutOptions.Center
            }, 1);

            card.Content = grid;
            Stops.Children.Add(card);
        }
    }

    private async void OnArriveNext(object? sender, EventArgs e) => await CompleteNextAsync(skipped: false);

    private async void OnSkipNext(object? sender, EventArgs e) => await CompleteNextAsync(skipped: true);

    private async Task CompleteNextAsync(bool skipped)
    {
        if (_busy || _nextStop is null)
        {
            return;
        }

        var van = Vehicle();
        if (van is null)
        {
            return;
        }

        _busy = true;
        try
        {
            await _api.Client.ArriveAtStopAsync(_nextStop.Id, van.Id, skipped, _meLat, _meLng);
            RouteStatus.TextColor = Color.FromArgb("#3DBA7A");
            RouteStatus.Text = skipped
                ? $"Пропущено: {_nextStop.SettlementName}"
                : $"На месте: {_nextStop.SettlementName}";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            RouteStatus.TextColor = Color.FromArgb("#F0C7B0");
            RouteStatus.Text = ApiErrors.Friendly(ex);
            PaintLinkBanner();
        }
        finally
        {
            _busy = false;
        }
    }
}
