using Avtomagazin.ApiClient;

namespace Avtomagazin.MauiShared;

public partial class DrivePage
{
    private async void OnTrackerToggled(object? sender, ToggledEventArgs e)
    {
        if (_trackerToggleBusy || e.Value == _gpsLive)
        {
            return;
        }

        if (e.Value)
        {
            await StartTrackerAsync();
        }
        else
        {
            StopTracker();
        }
    }

    private async Task StartTrackerAsync()
    {
        if (Vehicle() is null)
        {
            GpsStatus.Text = "Нет автолавки — трекер недоступен";
            GpsStatus.TextColor = Color.FromArgb("#F0C7B0");
            SetTrackerSwitch(false);
            PaintLinkBanner();
            return;
        }

        var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            GpsStatus.Text = "Нет доступа к геолокации";
            GpsStatus.TextColor = Color.FromArgb("#F0C7B0");
            SetTrackerSwitch(false);
            PaintLinkBanner();
            return;
        }

        _gpsLive = true;
        _trackerWanted = true;
        _gpsAccuracyM = null;
        _lastGpsAt = null;
        SetTrackerSwitch(true);
        StartBroadcast(async ct =>
        {
            var loc = await Geolocation.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(8)), ct);
            if (loc is null)
            {
                return null;
            }

            return ((double Lat, double Lng, double? Speed, double? Acc)?)
                (loc.Latitude, loc.Longitude, loc.Speed is null ? null : loc.Speed * 3.6, loc.Accuracy);
        });
        GpsStatus.TextColor = Color.FromArgb("#3DBA7A");
        GpsStatus.Text = "Включён";
        PaintLinkBanner();
    }

    private void PauseBroadcast()
    {
        _broadcast?.Cancel();
        _broadcast = null;
        _gpsLive = false;
    }

    private void StopTracker()
    {
        _trackerWanted = false;
        PauseBroadcast();
        _gpsAccuracyM = null;
        _lastGpsAt = null;
        SetTrackerSwitch(false);
        GpsStatus.TextColor = Color.FromArgb("#A7B8AD");
        GpsStatus.Text = "Выключен";
        PaintLinkBanner();
    }

    private void SetTrackerSwitch(bool on)
    {
        if (TrackerSwitch.IsToggled == on)
        {
            return;
        }

        _trackerToggleBusy = true;
        TrackerSwitch.IsToggled = on;
        _trackerToggleBusy = false;
    }

    private void OnRole(object? sender, EventArgs e)
    {
        StopTracker();
        ((IAppHost)Shell.Current).SignOut();
    }

    private void StartBroadcast(Func<CancellationToken, Task<(double Lat, double Lng, double? Speed, double? Acc)?>> ping)
    {
        _broadcast?.Cancel();
        _broadcast = new CancellationTokenSource();
        var ct = _broadcast.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var van = Vehicle();
                    (double Lat, double Lng, double? Speed, double? Acc)? point;
                    try
                    {
                        point = await ping(ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            GpsStatus.TextColor = Color.FromArgb("#F0C7B0");
                            GpsStatus.Text = "Нет точки GPS";
                            PaintLinkBanner();
                        });
                        await Task.Delay(4000, ct);
                        continue;
                    }

                    if (van is not null && point is not null)
                    {
                        await _api.Client.IngestPositionAsync(van.Id, point.Value.Lat, point.Value.Lng, point.Value.Speed, ct);
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            _meLat = point.Value.Lat;
                            _meLng = point.Value.Lng;
                            _gpsAccuracyM = point.Value.Acc;
                            _lastGpsAt = DateTimeOffset.UtcNow;
                            var acc = point.Value.Acc;
                            GpsStatus.TextColor = Color.FromArgb("#3DBA7A");
                            GpsStatus.Text = acc is double a
                                ? $"В эфире · ±{a:0} м · {BelarusTime.ToMinsk(DateTimeOffset.UtcNow):HH:mm:ss}"
                                : $"В эфире · {BelarusTime.ToMinsk(DateTimeOffset.UtcNow):HH:mm:ss}";
                            PaintLinkBanner();
                            PaintProximity();
                        });
                    }
                    else
                    {
                        MainThread.BeginInvokeOnMainThread(PaintLinkBanner);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        GpsStatus.TextColor = Color.FromArgb("#F0C7B0");
                        GpsStatus.Text = ApiErrors.Friendly(ex);
                        PaintLinkBanner();
                    });
                }

                await Task.Delay(4000, ct);
            }
        }, ct);
    }
}
