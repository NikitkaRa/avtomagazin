namespace Avtomagazin.Mobile;

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
        try
        {
            var items = await _api.Client.GetCoverageAsync();
            List.ItemsSource = items.Select(c => new Row(
                c.SettlementName,
                $"{c.RegionCode} · {(c.WithinScheduledWindow ? "в окне" : "вне окна")}",
                c.ArrivedAtUtc.ToLocalTime().ToString("HH:mm"))).ToList();
        }
        catch
        {
            List.ItemsSource = Array.Empty<Row>();
        }
    }

    public sealed record Row(string SettlementName, string Subtitle, string Time);
}
