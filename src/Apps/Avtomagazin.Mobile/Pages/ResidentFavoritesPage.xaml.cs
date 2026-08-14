namespace Avtomagazin.Mobile;

public partial class ResidentFavoritesPage : ContentPage
{
    public ResidentFavoritesPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
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
        ((AppShell)Shell.Current).ShowRole();
    }
}
