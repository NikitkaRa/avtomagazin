using Avtomagazin.MauiShared;
using Microsoft.Extensions.DependencyInjection;

namespace Avtomagazin.ResidentApp;

public partial class AppShell : Shell, IAppHost
{
    private readonly IServiceProvider _services;

    public AppShell(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        Routing.RegisterRoute("stop", typeof(ResidentStopPage));
    }

    public void ShowSignedIn()
    {
        Items.Clear();
        Items.Add(ShellTabs.Create(
            ("Карта", _services.GetRequiredService<ResidentMapPage>()),
            ("Рейс", _services.GetRequiredService<ResidentSchedulePage>()),
            ("Избранное", _services.GetRequiredService<ResidentFavoritesPage>())));
    }

    public void SignOut()
    {
        _services.GetRequiredService<Session>().SignOut();
        ShowLogin();
    }

    public void ShowLogin()
    {
        Items.Clear();
        Items.Add(new ShellContent
        {
            Title = "Вход",
            Content = _services.GetRequiredService<LoginPage>()
        });
    }
}
