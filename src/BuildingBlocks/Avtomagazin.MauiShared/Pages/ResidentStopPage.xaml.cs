using Avtomagazin.ApiClient;

namespace Avtomagazin.MauiShared;

[QueryProperty(nameof(StopId), "id")]
public partial class ResidentStopPage : ContentPage
{
    private readonly SnapshotStore _snapshot;
    private readonly ApiHub _api;
    private readonly Session _session;
    private RouteStopDto? _stop;

    public string StopId { get; set; } = "";

    public ResidentStopPage(SnapshotStore snapshot, ApiHub api, Session session)
    {
        InitializeComponent();
        _snapshot = snapshot;
        _api = api;
        _session = session;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (Guid.TryParse(StopId, out var id))
        {
            _stop = _snapshot.FindStop(id);
        }

        if (_stop is null)
        {
            TitleLabel.Text = "Остановка не найдена";
            return;
        }

        TitleLabel.Text = _stop.SettlementName;
        PlanLabel.Text = $"План {BelarusTime.Clock(_stop.PlannedArrivalUtc)}";
        WindowLabel.Text = GeoMath.InReportWindow(_stop.PlannedArrivalUtc, DateTimeOffset.UtcNow)
            ? "Сейчас окно отметки: можно сказать «на месте» / «не приехала»."
            : "Окно отметки закрыто (за 15 мин до плана и час после).";
        var route = _snapshot.Current.Routes.FirstOrDefault(r => r.Id == _stop.RouteId);
        var note = route is null
            ? null
            : _snapshot.ActiveNotes().FirstOrDefault(n => n.VehicleId == route.VehicleId);
        if (note is null)
        {
            DriverNoteLabel.IsVisible = false;
            DriverNoteLabel.Text = "";
        }
        else
        {
            DriverNoteLabel.IsVisible = true;
            DriverNoteLabel.Text = $"Водитель: {note.Body} · {BelarusTime.Clock(note.CreatedAtUtc)}";
        }

        FavoriteButton.Text = FavoriteStore.Contains(_stop.Id) ? "Убрать из избранного" : "В избранное";
    }

    private async void OnFavorite(object? sender, EventArgs e)
    {
        if (_stop is null)
        {
            return;
        }

        var added = FavoriteStore.Toggle(_stop.Id, _stop.SettlementName);
        FavoriteButton.Text = added ? "Убрать из избранного" : "В избранное";
        try
        {
            if (added)
            {
                await _api.Client.AddFavoriteStopAsync(_session.DeviceToken, _stop.Id, _stop.SettlementName, _session.Platform);
                Message.Text = "В избранном: пуш, когда водитель будет на месте.";
            }
            else
            {
                await _api.Client.RemoveFavoriteStopAsync(_stop.Id);
                Message.Text = "Убрали из избранного.";
            }
        }
        catch (Exception ex)
        {
            FavoriteStore.Toggle(_stop.Id, _stop.SettlementName);
            FavoriteButton.Text = FavoriteStore.Contains(_stop.Id) ? "Убрать из избранного" : "В избранное";
            Message.Text = _snapshot.Online
                ? ApiErrors.Friendly(ex)
                : "Нет сети — избранное не сохранилось. Попробуйте ещё раз.";
        }
    }

    private void OnSite(object? sender, EventArgs e) => _ = ReportAsync(PresenceKinds.OnSite);

    private void OnNoShow(object? sender, EventArgs e) => _ = ReportAsync(PresenceKinds.NoShow);

    private async Task ReportAsync(string kind)
    {
        if (_stop is null)
        {
            return;
        }

        try
        {
            await _api.Client.ReportStopPresenceAsync(_stop.Id, kind, _session.DeviceToken);
            Message.Text = kind == PresenceKinds.OnSite ? "Записали: автолавка на месте." : "Записали: не приехала.";
        }
        catch (Exception ex)
        {
            Message.Text = ex.Message;
        }
    }
}
