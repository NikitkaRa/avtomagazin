namespace Avtomagazin.MauiShared;

public partial class DispatchPage : ContentPage
{
    private readonly Session _session;
    private readonly ApiHub _api;
    private readonly SnapshotStore _snapshot;
    private IDispatcherTimer? _poll;

    public DispatchPage(Session session, ApiHub api, SnapshotStore snapshot)
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
        _poll.Interval = TimeSpan.FromSeconds(7);
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
        FleetMap.Render(MapView, _snapshot.Current, tiles: _snapshot.Online);
        Vans.Children.Clear();
        foreach (var van in _snapshot.Current.Vehicles)
        {
            Vans.Children.Add(new Label
            {
                Text = $"{van.PlateNumber} · {van.OperatorName} · {van.LastSource ?? "нет сигнала"}",
                TextColor = Color.FromArgb("#F3F7F3"),
                Padding = new Thickness(0, 4)
            });
        }

        Pushes.Children.Clear();
        try
        {
            foreach (var push in (await _api.Client.GetNotificationsAsync()).Take(6))
            {
                Pushes.Children.Add(new Label
                {
                    Text = $"{BelarusTime.Clock(push.SentAtUtc)} · {push.Title} · {push.SettlementName}",
                    TextColor = Color.FromArgb("#A7B8AD"),
                    FontSize = 13
                });
            }
        }
        catch
        {
            Pushes.Children.Add(new Label { Text = "Лог пушей недоступен офлайн.", TextColor = Color.FromArgb("#A7B8AD") });
        }
    }

    private void OnRole(object? sender, EventArgs e)
    {
        ((IAppHost)Shell.Current).SignOut();
    }
}
