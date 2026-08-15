namespace Avtomagazin.MauiShared;

public partial class ResidentFavoritesPage : ContentPage
{
    private readonly ApiHub _api;

    public ResidentFavoritesPage(ApiHub api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await FavoriteStore.PullAsync(_api.Client);
        }
        catch
        {
            // offline: local cache
        }

        List.Children.Clear();
        foreach (var item in FavoriteStore.All())
        {
            var id = item.Id;
            var btn = new Button
            {
                Text = item.SettlementName,
                BackgroundColor = Color.FromArgb("#1B3328"),
                TextColor = Color.FromArgb("#F3F7F3")
            };
            btn.Clicked += async (_, _) => await Shell.Current.GoToAsync($"stop?id={id}");
            List.Children.Add(btn);
        }

        if (FavoriteStore.All().Count == 0)
        {
            List.Children.Add(new Label { Text = "Пока пусто. Открой остановку в рейсе.", TextColor = Color.FromArgb("#A7B8AD") });
        }
    }

    private void OnBack(object? sender, EventArgs e)
    {
        ((IAppHost)Shell.Current).SignOut();
    }
}
