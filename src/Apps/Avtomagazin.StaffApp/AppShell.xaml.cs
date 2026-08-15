using Avtomagazin.MauiShared;
using Microsoft.Extensions.DependencyInjection;

namespace Avtomagazin.StaffApp;

public partial class AppShell : Shell, IAppHost
{
    private readonly IServiceProvider _services;

    public AppShell(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        ShowLogin();
    }

    public void ShowSignedIn()
    {
        var session = _services.GetRequiredService<Session>();
        Items.Clear();
        if (session.IsDriver)
        {
            Items.Add(ShellTabs.Create(("Рейс", _services.GetRequiredService<DrivePage>())));
            return;
        }

        Items.Add(ShellTabs.Create(
            ("Карта", _services.GetRequiredService<DispatchPage>()),
            ("Визиты", _services.GetRequiredService<CoveragePage>())));
    }

    public void SignOut()
    {
        _services.GetRequiredService<Session>().SignOut();
        ShowLogin();
    }

    private void ShowLogin()
    {
        Items.Clear();
        Items.Add(new ShellContent
        {
            Title = "Вход",
            Content = _services.GetRequiredService<LoginPage>()
        });
    }
}
