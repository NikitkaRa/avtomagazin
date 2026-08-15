namespace Avtomagazin.MauiShared;

public partial class ResidentSchedulePage : ContentPage
{
    private readonly SnapshotStore _snapshot;

    public ResidentSchedulePage(SnapshotStore snapshot)
    {
        InitializeComponent();
        _snapshot = snapshot;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        List.ItemsSource = _snapshot.AllStops()
            .OrderBy(s => s.PlannedArrivalUtc)
            .Select(s => new Row(s.Id, s.SettlementName, s.RegionCode, s.PlannedArrivalUtc.ToLocalTime().ToString("HH:mm")))
            .ToList();
    }

    private async void OnSelect(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Row row)
        {
            List.SelectedItem = null;
            await Shell.Current.GoToAsync($"stop?id={row.Id}");
        }
    }

    public sealed record Row(Guid Id, string SettlementName, string Subtitle, string Time);
}
