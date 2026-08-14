using Avtomagazin.ApiClient;

namespace Avtomagazin.Mobile;

public partial class DrivePage : ContentPage
{
    private readonly Session _session;
    private readonly ApiHub _api;
    private readonly SnapshotStore _snapshot;
    private CancellationTokenSource? _broadcast;
    private IDispatcherTimer? _poll;

    public DrivePage(Session session, ApiHub api, SnapshotStore snapshot)
    {
        InitializeComponent();
        _session = session;
        _api = api;
        _snapshot = snapshot;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReloadAsync();
        _poll ??= Dispatcher.CreateTimer();
        _poll.Interval = TimeSpan.FromSeconds(8);
        _poll.Tick -= OnTick;
        _poll.Tick += OnTick;
        _poll.Start();
    }

    protected override void OnDisappearing()
    {
        _poll?.Stop();
        base.OnDisappearing();
    }

    private async void OnTick(object? sender, EventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        await _snapshot.RefreshAsync(_api.Client);
        var van = Vehicle();
        VanLabel.Text = van is null ? "Нет автолавки" : $"{van.PlateNumber} · {van.OperatorName}";
        var next = _snapshot.Current.Eta
            .Where(e => van is null || e.VehicleId == van.Id)
            .OrderBy(e => e.MinutesUntilArrival)
            .FirstOrDefault();
        NextLabel.Text = next is null ? "Следующая остановка появится по GPS." : $"Следующая: {next.SettlementName} · ~{next.MinutesUntilArrival} мин";
        RenderStops(van);
        FleetMap.Render(MapView, _snapshot.Current, van?.Id, _snapshot.Online);
    }

    private void RenderStops(VehicleDto? van)
    {
        Stops.Children.Clear();
        foreach (var stop in _snapshot.Current.Routes
            .Where(r => van is null || r.VehicleId == van.Id)
            .SelectMany(r => r.Stops ?? [])
            .OrderBy(s => s.Sequence))
        {
            var captured = stop;
            var row = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                ],
                Padding = new Thickness(0, 4)
            };
            row.Add(new VerticalStackLayout
            {
                Children =
                {
                    new Label { Text = stop.SettlementName, TextColor = Color.FromArgb("#F3F7F3"), FontAttributes = FontAttributes.Bold },
                    new Label { Text = $"план {stop.PlannedArrivalUtc.ToLocalTime():HH:mm}", TextColor = Color.FromArgb("#A7B8AD"), FontSize = 13 }
                }
            });
            var btn = new Button { Text = "На месте", BackgroundColor = Color.FromArgb("#3DBA7A"), TextColor = Color.FromArgb("#082014") };
            btn.Clicked += async (_, _) => await ArriveAsync(captured);
            row.Add(btn, 1);
            Stops.Children.Add(row);
        }
    }

    private async Task ArriveAsync(RouteStopDto stop)
    {
        var van = Vehicle();
        if (van is null)
        {
            return;
        }

        try
        {
            await _api.Client.ArriveAtStopAsync(stop.Id, van.Id);
            Status.Text = $"На месте: {stop.SettlementName}. Пуш ушёл в избранное.";
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
        }
    }

    private async void OnLive(object? sender, EventArgs e)
    {
        var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            Status.Text = "Нет доступа к GPS.";
            return;
        }

        StartBroadcast(async ct =>
        {
            var loc = await Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(8)), ct);
            return loc is null ? null : ((double Lat, double Lng, double? Speed)?)(loc.Latitude, loc.Longitude, loc.Speed is null ? null : loc.Speed * 3.6);
        });
        Status.Text = "Транслируем GPS с телефона.";
    }

    private void OnDemo(object? sender, EventArgs e)
    {
        var points = _snapshot.Current.Routes
            .Where(r => Vehicle() is null || r.VehicleId == Vehicle()!.Id)
            .SelectMany(r => r.Stops ?? [])
            .OrderBy(s => s.Sequence)
            .Select(s => (s.Latitude, s.Longitude))
            .ToList();
        if (points.Count == 0)
        {
            points = [(53.5774, 27.7472), (53.5085, 27.8372), (53.6786, 27.9525)];
        }

        var i = 0;
        StartBroadcast(_ =>
        {
            var p = points[i % points.Count];
            i++;
            return Task.FromResult<(double Lat, double Lng, double? Speed)?>((p.Latitude, p.Longitude, 32));
        });
        Status.Text = "Демо-маршрут по остановкам рейса.";
    }

    private void OnStop(object? sender, EventArgs e)
    {
        _broadcast?.Cancel();
        _broadcast = null;
        Status.Text = "Трансляция остановлена.";
    }

    private void OnRole(object? sender, EventArgs e)
    {
        OnStop(sender, e);
        _session.SignOut();
        ((AppShell)Shell.Current).ShowRole();
    }

    private void StartBroadcast(Func<CancellationToken, Task<(double Lat, double Lng, double? Speed)?>> ping)
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
                    var point = await ping(ct);
                    if (van is not null && point is not null)
                    {
                        await _api.Client.IngestPositionAsync(van.Id, point.Value.Lat, point.Value.Lng, point.Value.Speed, ct);
                        MainThread.BeginInvokeOnMainThread(() =>
                            Status.Text = $"В эфире {point.Value.Lat:F4}, {point.Value.Lng:F4}");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    MainThread.BeginInvokeOnMainThread(() => Status.Text = ex.Message);
                }

                await Task.Delay(4000, ct);
            }
        }, ct);
    }

    private VehicleDto? Vehicle()
        => _snapshot.Current.Vehicles.FirstOrDefault(v => _session.VehicleId is null || v.Id == _session.VehicleId)
           ?? _snapshot.Current.Vehicles.FirstOrDefault();
}
