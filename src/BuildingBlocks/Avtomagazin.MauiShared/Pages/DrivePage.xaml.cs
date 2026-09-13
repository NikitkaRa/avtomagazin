using Avtomagazin.ApiClient;

namespace Avtomagazin.MauiShared;

public partial class DrivePage : ContentPage
{
    private const double WeakGpsMeters = 50;
    private bool _trackerWanted;
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
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

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
        if (_trackerWanted)
        {
            await StartTrackerAsync();
        }
    }

    protected override void OnDisappearing()
    {
        Connectivity.ConnectivityChanged -= OnConnectivityChanged;
        _poll?.Stop();
        PauseBroadcast();
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
        else if (_snapshot.SessionExpired)
        {
            text = _snapshot.LastError ?? "Сессия истекла — войдите снова";
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
        await _reloadGate.WaitAsync();
        try
        {
            await ReloadCoreAsync();
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task ReloadCoreAsync()
    {
        await _snapshot.RefreshAsync(_api.Client);
        var van = Vehicle();
        var route = van is null ? null : RouteDto.ForVehicle(_snapshot.Current.Routes, van.Id);

        VanLabel.Text = van is null ? "Нет автолавки" : van.PlateNumber;
        RouteLabel.Text = DriveItinerary.Subtitle(van, route);

        var trip = DriveItinerary.FromStops(route?.Stops);
        _nextStop = trip.Next;

        if (_nextStop is null)
        {
            NextTitle.Text = trip.NextTitle;
            NextMeta.Text = trip.NextMeta;
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
        if (_snapshot.Current.NotesUnavailable && myNote is null)
        {
            NoteStatus.Text = "Заметки недоступны";
            NoteStatus.TextColor = Color.FromArgb("#F0C7B0");
        }
        else if (myNote is not null)
        {
            NoteStatus.Text = $"Сейчас: «{myNote.Body}» · {BelarusTime.Clock(myNote.CreatedAtUtc)}";
            NoteStatus.TextColor = Color.FromArgb("#7BC9A0");
        }
        else if (_hasActiveNote)
        {
            NoteStatus.Text = "";
        }

        PaintNoteAction();

        RenderStops(trip);

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

    private VehicleDto? Vehicle()
        => _session.VehicleId is Guid id
            ? _snapshot.Current.Vehicles.FirstOrDefault(v => v.Id == id)
            : null;
}
