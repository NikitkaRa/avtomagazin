using Avtomagazin.ApiClient;
using Microsoft.Maui.Controls.Shapes;

namespace Avtomagazin.MauiShared;

public partial class DrivePage : ContentPage
{
    private const double WeakGpsMeters = 50;
    private const double OnSiteMeters = 150;
    private static readonly TimeSpan SlowRefresh = TimeSpan.FromSeconds(3.5);

    private readonly Session _session;
    private readonly ApiHub _api;
    private readonly SnapshotStore _snapshot;
    private CancellationTokenSource? _broadcast;
    private IDispatcherTimer? _poll;
    private bool _gpsTab;
    private RouteStopDto? _nextStop;
    private bool _busy;
    private bool _gpsLive;
    private bool _trackerToggleBusy;
    private bool _hasActiveNote;
    private bool _noteBusy;
    private double? _gpsAccuracyM;
    private DateTimeOffset? _lastGpsAt;
    private double? _meLat;
    private double? _meLng;

    public DrivePage(Session session, ApiHub api, SnapshotStore snapshot)
    {
        InitializeComponent();
        _session = session;
        _api = api;
        _snapshot = snapshot;
        PaintTabs();
        GpsStatus.Text = "Выключен";
        PaintLinkBanner();
        PaintNoteAction();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Connectivity.ConnectivityChanged += OnConnectivityChanged;
        await ReloadAsync();
        _poll ??= Dispatcher.CreateTimer();
        _poll.Interval = TimeSpan.FromSeconds(8);
        _poll.Tick -= OnTick;
        _poll.Tick += OnTick;
        _poll.Start();
    }

    protected override void OnDisappearing()
    {
        Connectivity.ConnectivityChanged -= OnConnectivityChanged;
        _poll?.Stop();
        base.OnDisappearing();
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        => MainThread.BeginInvokeOnMainThread(PaintLinkBanner);

    private async void OnTick(object? sender, EventArgs e) => await ReloadAsync();

    private void OnTabRoute(object? sender, EventArgs e)
    {
        _gpsTab = false;
        PaintTabs();
    }

    private void OnTabGps(object? sender, EventArgs e)
    {
        _gpsTab = true;
        PaintTabs();
    }

    private void PaintTabs()
    {
        RoutePanel.IsVisible = !_gpsTab;
        GpsPanel.IsVisible = _gpsTab;
        PaintTab(TabRoute, !_gpsTab);
        PaintTab(TabGps, _gpsTab);
    }

    private static void PaintTab(Button button, bool on)
    {
        button.BackgroundColor = Color.FromArgb(on ? "#3DBA7A" : "#1B3328");
        button.TextColor = Color.FromArgb(on ? "#082014" : "#F3F7F3");
    }

    private void PaintLinkBanner()
    {
        var access = Connectivity.Current.NetworkAccess;
        string? text = null;
        var warn = true;

        if (access is NetworkAccess.None or NetworkAccess.Unknown)
        {
            text = "Нет интернета — данные могут быть устаревшими";
        }
        else if (access == NetworkAccess.ConstrainedInternet)
        {
            text = "Слабый интернет — соединение ограничено";
        }
        else if (!_snapshot.Online)
        {
            text = string.IsNullOrWhiteSpace(_snapshot.LastError)
                ? "Нет связи с сервером — показаны последние данные"
                : $"Нет связи с сервером — {_snapshot.LastError}";
        }
        else if (_snapshot.LastRefreshDuration > SlowRefresh)
        {
            text = "Слабый интернет — сервер отвечает медленно";
        }
        else if (_gpsLive)
        {
            if (_lastGpsAt is null || DateTimeOffset.UtcNow - _lastGpsAt > TimeSpan.FromSeconds(20))
            {
                text = "Слабый GPS — нет свежей точки";
            }
            else if (_gpsAccuracyM is double acc && acc > WeakGpsMeters)
            {
                text = $"Слабый GPS — точность ±{acc:0} м";
            }
            else
            {
                warn = false;
            }
        }
        else
        {
            warn = false;
        }

        LinkBanner.IsVisible = text is not null;
        LinkBannerText.Text = text ?? "";
        LinkBanner.BackgroundColor = Color.FromArgb(warn ? "#3A2A1C" : "#1B3328");
        LinkBannerText.TextColor = Color.FromArgb(warn ? "#F0C7B0" : "#A7B8AD");
    }

    private async Task ReloadAsync()
    {
        await _snapshot.RefreshAsync(_api.Client);
        var van = Vehicle();
        // Prefer the operational trip (few stops) over demo heat dump on the same van.
        var route = _snapshot.Current.Routes
            .Where(r => van is not null && r.VehicleId == van.Id)
            .OrderBy(r => r.Stops?.Count ?? int.MaxValue)
            .FirstOrDefault();

        VanLabel.Text = van is null ? "Нет автолавки" : van.PlateNumber;
        RouteLabel.Text = van is null
            ? "Назначьте автолавку в админке"
            : $"{van.OperatorName}{(route is null ? "" : $" · {route.Name}")}";

        var stops = (route?.Stops ?? [])
            .OrderBy(s => s.Sequence)
            .ToList();
        _nextStop = stops.FirstOrDefault(s => s.ArrivedAtUtc is null);

        if (_nextStop is null)
        {
            NextTitle.Text = stops.Count == 0 ? "Нет остановок на сегодня" : "Рейс завершён";
            NextMeta.Text = stops.Count == 0 ? "" : "Все остановки отмечены";
            NextMeta.TextColor = Color.FromArgb("#A7B8AD");
            NextActions.IsVisible = false;
            NextMap.IsVisible = false;
            MapLegend.IsVisible = false;
            NextDistance.IsVisible = false;
            NextCard.Stroke = Color.FromArgb("#2A4638");
            ArriveBtn.BackgroundColor = Color.FromArgb("#3DBA7A");
        }
        else
        {
            NextTitle.Text = _nextStop.SettlementName;
            PaintNextSchedule();
            NextActions.IsVisible = true;
            NextMap.IsVisible = true;
            MapLegend.IsVisible = true;
            await RefreshProximityAsync();
        }

        var myNote = _snapshot.ActiveNotes().FirstOrDefault(n => van is not null && n.VehicleId == van.Id);
        if (myNote is not null)
        {
            NoteStatus.Text = $"Сейчас: «{myNote.Body}» · {myNote.CreatedAtUtc.ToLocalTime():HH:mm}";
            NoteStatus.TextColor = Color.FromArgb("#7BC9A0");
        }
        else if (_hasActiveNote)
        {
            NoteStatus.Text = "";
        }

        PaintNoteAction();

        RenderStops(stops, _nextStop?.Id);

        // Banner owns connectivity messaging; keep RouteStatus for action feedback only.
        var routeStatus = RouteStatus.Text ?? "";
        if (routeStatus.Contains("Нет связи", StringComparison.Ordinal)
            || routeStatus.Contains("502", StringComparison.Ordinal)
            || routeStatus.Contains("Bad Gateway", StringComparison.OrdinalIgnoreCase))
        {
            RouteStatus.Text = "";
        }

        PaintLinkBanner();
    }

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
        var onSite = meters <= OnSiteMeters;
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

        var plan = _nextStop.PlannedArrivalUtc.ToLocalTime().ToString("HH:mm");
        var late = DateTimeOffset.UtcNow - _nextStop.PlannedArrivalUtc;
        if (late < TimeSpan.FromMinutes(1))
        {
            NextMeta.Text = plan;
            NextMeta.TextColor = Color.FromArgb("#A7B8AD");
            return;
        }

        var mins = Math.Max(1, (int)late.TotalMinutes);
        NextMeta.Text = mins < 60
            ? $"{plan} · опоздание {mins} мин"
            : $"{plan} · опоздание {mins / 60} ч {mins % 60} мин";
        NextMeta.TextColor = Color.FromArgb("#F0C7B0");
    }

    private void RenderStops(List<RouteStopDto> stops, Guid? nextStopId)
    {
        Stops.Children.Clear();
        foreach (var stop in stops)
        {
            var done = stop.ArrivedAtUtc is not null;
            var isNext = !done && nextStopId == stop.Id;
            var status = stop.Skipped
                ? "пропущена"
                : done
                    ? $"был {stop.ArrivedAtUtc!.Value.ToLocalTime():HH:mm}"
                    : isNext ? "сейчас" : "";

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
                Text = stop.PlannedArrivalUtc.ToLocalTime().ToString("HH:mm"),
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
            await _api.Client.ArriveAtStopAsync(_nextStop.Id, van.Id, skipped);
            RouteStatus.TextColor = Color.FromArgb("#3DBA7A");
            RouteStatus.Text = skipped
                ? $"Пропущено: {_nextStop.SettlementName}"
                : $"На месте: {_nextStop.SettlementName}";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            RouteStatus.TextColor = Color.FromArgb("#F0C7B0");
            RouteStatus.Text = FriendlyError(ex);
            PaintLinkBanner();
        }
        finally
        {
            _busy = false;
        }
    }

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

    private void StopTracker()
    {
        _broadcast?.Cancel();
        _broadcast = null;
        _gpsLive = false;
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
                                ? $"В эфире · ±{a:0} м · {DateTime.Now:HH:mm:ss}"
                                : $"В эфире · {DateTime.Now:HH:mm:ss}";
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
                        GpsStatus.Text = FriendlyError(ex);
                        PaintLinkBanner();
                    });
                }

                await Task.Delay(4000, ct);
            }
        }, ct);
    }

    private async void OnNoteStuck(object? sender, EventArgs e)
    {
        if (_hasActiveNote)
        {
            return;
        }

        await SendNoteAsync("Завяз");
    }

    private async void OnNoteTire(object? sender, EventArgs e)
    {
        if (_hasActiveNote)
        {
            return;
        }

        await SendNoteAsync("Пробил колесо");
    }

    private async void OnNoteWait(object? sender, EventArgs e)
    {
        if (_hasActiveNote)
        {
            return;
        }

        await SendNoteAsync("Стою на месте");
    }

    private async void OnNoteAction(object? sender, EventArgs e)
    {
        if (_hasActiveNote)
        {
            await ClearNoteAsync();
            return;
        }

        var text = NoteEntry.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            NoteStatus.TextColor = Color.FromArgb("#F0C7B0");
            NoteStatus.Text = "Напишите текст или выберите быстрый вариант";
            return;
        }

        await SendNoteAsync(text);
    }

    private async Task ClearNoteAsync()
    {
        var van = Vehicle();
        if (van is null || _noteBusy)
        {
            return;
        }

        _noteBusy = true;
        PaintNoteAction();
        try
        {
            await _api.Client.ClearDriverNoteAsync(van.Id);
            NoteEntry.Text = "";
            NoteStatus.TextColor = Color.FromArgb("#A7B8AD");
            NoteStatus.Text = "Сообщение снято";
            _hasActiveNote = false;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            NoteStatus.TextColor = Color.FromArgb("#F0C7B0");
            NoteStatus.Text = FriendlyError(ex);
            PaintLinkBanner();
        }
        finally
        {
            _noteBusy = false;
            PaintNoteAction();
        }
    }

    private void PaintNoteAction()
    {
        var van = Vehicle();
        var note = _snapshot.ActiveNotes().FirstOrDefault(n => van is not null && n.VehicleId == van.Id);
        _hasActiveNote = note is not null;
        var locked = _hasActiveNote || _noteBusy;
        SetNoteChip(NoteChipStuck, !locked);
        SetNoteChip(NoteChipTire, !locked);
        SetNoteChip(NoteChipWait, !locked);
        NoteEntry.IsEnabled = !locked;
        NoteEntry.Opacity = locked ? 0.45 : 1;
        if (_hasActiveNote)
        {
            NoteActionBtn.Text = "Снять";
            NoteActionBtn.BackgroundColor = Color.FromArgb("#1B3328");
            NoteActionBtn.TextColor = Color.FromArgb("#F3F7F3");
        }
        else
        {
            NoteActionBtn.Text = "Отправить";
            NoteActionBtn.BackgroundColor = Color.FromArgb("#3DBA7A");
            NoteActionBtn.TextColor = Color.FromArgb("#082014");
        }
    }

    private static void SetNoteChip(Button button, bool on)
    {
        button.IsEnabled = on;
        button.Opacity = on ? 1 : 0.45;
    }

    private async Task SendNoteAsync(string body)
    {
        if (_hasActiveNote || _noteBusy)
        {
            NoteStatus.TextColor = Color.FromArgb("#F0C7B0");
            NoteStatus.Text = "Сначала снимите текущее сообщение";
            return;
        }

        var van = Vehicle();
        if (van is null)
        {
            NoteStatus.TextColor = Color.FromArgb("#F0C7B0");
            NoteStatus.Text = "Нет автолавки";
            return;
        }

        _noteBusy = true;
        PaintNoteAction();
        try
        {
            var (lat, lng) = await ResolveNoteCoordsAsync(van);
            await _api.Client.PostDriverNoteAsync(new PostDriverNoteRequest(van.Id, body, lat, lng));
            NoteEntry.Text = body;
            NoteStatus.TextColor = Color.FromArgb("#3DBA7A");
            NoteStatus.Text = $"Отправлено диспетчеру · {DateTime.Now:HH:mm}";
            _hasActiveNote = true;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            NoteStatus.TextColor = Color.FromArgb("#F0C7B0");
            NoteStatus.Text = FriendlyError(ex);
            PaintLinkBanner();
        }
        finally
        {
            _noteBusy = false;
            PaintNoteAction();
        }
    }

    private async Task<(double Lat, double Lng)> ResolveNoteCoordsAsync(VehicleDto van)
    {
        try
        {
            var loc = await Geolocation.GetLastKnownLocationAsync()
                      ?? await Geolocation.GetLocationAsync(
                          new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(6)));
            if (loc is not null)
            {
                return (loc.Latitude, loc.Longitude);
            }
        }
        catch
        {
            // fall back to last known van fix
        }

        if (van.LastLatitude is double lat && van.LastLongitude is double lng)
        {
            return (lat, lng);
        }

        throw new InvalidOperationException("Нет координат — включите GPS");
    }

    private static string FriendlyError(Exception ex)
    {
        if (ex is SessionExpiredException)
        {
            return "Сессия истекла — войдите снова";
        }

        var msg = ex.Message;
        if (msg.Contains("502", StringComparison.Ordinal) || msg.Contains("Bad Gateway", StringComparison.OrdinalIgnoreCase))
        {
            return "Сервер временно недоступен";
        }

        if (msg.Contains("401 (", StringComparison.Ordinal))
        {
            return "Сессия истекла — войдите снова";
        }

        return msg.Length > 120 ? msg[..120] + "…" : msg;
    }

    private VehicleDto? Vehicle()
        => _session.VehicleId is Guid id
            ? _snapshot.Current.Vehicles.FirstOrDefault(v => v.Id == id)
            : null;
}
