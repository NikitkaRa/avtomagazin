namespace Avtomagazin.MauiShared;

public partial class CoveragePage : ContentPage
{
    private readonly ApiHub _api;

    public CoveragePage(ApiHub api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Subs.Children.Clear();
        Visits.Children.Clear();
        try
        {
            var stats = await _api.Client.GetFavoriteStatsAsync();
            if (stats.Count == 0)
            {
                Subs.Children.Add(new Label { Text = "Пока никто не подписался.", TextColor = Color.FromArgb("#A7B8AD") });
            }
            else
            {
                foreach (var row in stats)
                {
                    Subs.Children.Add(new Label
                    {
                        Text = $"{row.SettlementName} — {row.SubscriberCount} чел.",
                        TextColor = Color.FromArgb("#F3F7F3")
                    });
                }
            }
        }
        catch
        {
            Subs.Children.Add(new Label { Text = "Статистика недоступна.", TextColor = Color.FromArgb("#A7B8AD") });
        }

        try
        {
            var items = await _api.Client.GetCoverageAsync();
            foreach (var c in items)
            {
                Visits.Children.Add(new Label
                {
                    Text = $"{c.SettlementName} · {(c.WithinScheduledWindow ? "в окне" : "вне окна")} · {BelarusTime.Clock(c.ArrivedAtUtc)}",
                    TextColor = Color.FromArgb("#F3F7F3")
                });
            }
        }
        catch
        {
            Visits.Children.Add(new Label { Text = "Визиты недоступны офлайн.", TextColor = Color.FromArgb("#A7B8AD") });
        }
    }

    private void OnRole(object? sender, EventArgs e)
    {
        ((IAppHost)Shell.Current).SignOut();
    }
}
