using Avtomagazin.ApiClient;

namespace Avtomagazin.MauiShared;

public partial class ResidentMapPage : ContentPage
{
    private readonly SnapshotStore _snapshot;
    private readonly ApiHub _api;
    private string _settlement = "";
    private IDispatcherTimer? _timer;

    public ResidentMapPage(SnapshotStore snapshot, ApiHub api)
    {
        InitializeComponent();
        _snapshot = snapshot;
        _api = api;
        _settlement = Preferences.Default.Get("settlement", "");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReloadAsync();
        _timer ??= Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(8);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        _timer?.Stop();
        base.OnDisappearing();
    }

    private async void OnTick(object? sender, EventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        await _snapshot.RefreshAsync(_api.Client);
        Banner.Text = _snapshot.Online
            ? DriverNoteBanner()
            : "Нет сети. Показано последнее расписание.";
        Banner.IsVisible = !string.IsNullOrWhiteSpace(Banner.Text);
        Banner.TextColor = _snapshot.Online
            ? Color.FromArgb("#F0C7B0")
            : Color.FromArgb("#F0C7B0");
        RenderChips();
        RenderUpcoming();
        FleetMap.Render(MapView, _snapshot.Current, tiles: _snapshot.Online);
    }

    private string DriverNoteBanner()
    {
        var note = _snapshot.ActiveNotes()
            .OrderByDescending(n => n.CreatedAtUtc)
            .FirstOrDefault(n =>
            {
                var van = _snapshot.Current.Vehicles.FirstOrDefault(v => v.Id == n.VehicleId);
                if (van is null)
                {
                    return false;
                }

                return _snapshot.Current.Routes.Any(r =>
                    r.VehicleId == n.VehicleId
                    && (r.Stops ?? []).Any(s =>
                        string.IsNullOrWhiteSpace(_settlement)
                        || s.SettlementName.Contains(_settlement, StringComparison.OrdinalIgnoreCase)));
            })
            ?? _snapshot.ActiveNotes().OrderByDescending(n => n.CreatedAtUtc).FirstOrDefault();

        if (note is null)
        {
            return "";
        }

        var plate = _snapshot.Current.Vehicles.FirstOrDefault(v => v.Id == note.VehicleId)?.PlateNumber ?? "автолавка";
        return $"Водитель ({plate}): {note.Body} · {BelarusTime.Clock(note.CreatedAtUtc)}";
    }

    private void RenderChips()
    {
        Chips.Children.Clear();
        var names = _snapshot.AllStops().Select(s => s.SettlementName).Distinct().ToList();
        if (names.Count > 0 && string.IsNullOrWhiteSpace(_settlement))
        {
            _settlement = names[0];
        }

        foreach (var name in names)
        {
            var chip = new Button
            {
                Text = name,
                BackgroundColor = name == _settlement ? Color.FromArgb("#3DBA7A") : Color.FromArgb("#1B3328"),
                TextColor = name == _settlement ? Color.FromArgb("#082014") : Color.FromArgb("#F3F7F3"),
                FontSize = 13,
                Padding = new Thickness(12, 6)
            };
            var captured = name;
            chip.Clicked += (_, _) =>
            {
                _settlement = captured;
                Preferences.Default.Set("settlement", captured);
                RenderChips();
                RenderUpcoming();
                _ = ReloadAsync();
            };
            Chips.Children.Add(chip);
        }
    }

    private void RenderUpcoming()
    {
        UpcomingList.Children.Clear();
        var stops = _snapshot.AllStops()
            .Where(s => string.IsNullOrWhiteSpace(_settlement)
                        || s.SettlementName.Contains(_settlement, StringComparison.OrdinalIgnoreCase))
            .Where(s => s.ArrivedAtUtc is null)
            .OrderBy(s => s.PlannedArrivalUtc)
            .Take(3)
            .ToList();

        if (stops.Count == 0)
        {
            UpcomingList.Children.Add(new Label
            {
                Text = $"По {_settlement} на сегодня плана нет — смотри «Рейс».",
                TextColor = Color.FromArgb("#A7B8AD")
            });
            return;
        }

        foreach (var stop in stops)
        {
            var route = _snapshot.Current.Routes.FirstOrDefault(r => r.Id == stop.RouteId);
            var note = route is null
                ? null
                : _snapshot.ActiveNotes().FirstOrDefault(n => n.VehicleId == route.VehicleId);
            var plate = route is null
                ? null
                : _snapshot.Current.Vehicles.FirstOrDefault(v => v.Id == route.VehicleId)?.PlateNumber;
            var sub = note is null
                ? $"по расписанию{(plate is null ? "" : $" · {plate}")}"
                : $"водитель: {note.Body}";
            UpcomingList.Children.Add(Card(
                $"{stop.SettlementName} · план {BelarusTime.Clock(stop.PlannedArrivalUtc)}",
                sub));
        }
    }

    private async void OnNearest(object? sender, EventArgs e)
    {
        try
        {
            var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                NearestHint.Text = "Нет доступа к геолокации.";
                return;
            }

            var pos = await Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(8)));
            if (pos is null)
            {
                NearestHint.Text = "Точку не получили.";
                return;
            }

            var found = GeoMath.Nearest(_snapshot.AllStops(), pos.Latitude, pos.Longitude);
            if (found is null)
            {
                NearestHint.Text = "Остановок в кэше нет.";
                return;
            }

            var km = GeoMath.DistanceKm(pos.Latitude, pos.Longitude, found.Latitude, found.Longitude);
            var label = km < 1 ? $"{Math.Round(km * 1000)} м" : $"{km:0.0} км";
            NearestHint.Text = $"Ближайшая: {found.SettlementName} ({label}).";
            _settlement = found.SettlementName;
            Preferences.Default.Set("settlement", _settlement);
            await Shell.Current.GoToAsync($"stop?id={found.Id}");
        }
        catch (Exception ex)
        {
            NearestHint.Text = ex.Message;
        }
    }

    private static Border Card(string title, string sub) => new()
    {
        BackgroundColor = Color.FromArgb("#1B3328"),
        Padding = 12,
        Stroke = Color.FromArgb("#2C4A3C"),
        Content = new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = title, TextColor = Color.FromArgb("#F3F7F3"), FontAttributes = FontAttributes.Bold },
                new Label { Text = sub, TextColor = Color.FromArgb("#A7B8AD"), FontSize = 13 }
            }
        }
    };
}
