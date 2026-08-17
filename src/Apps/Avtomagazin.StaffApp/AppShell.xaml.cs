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
    }

    public void ShowSignedIn()
    {
        var session = _services.GetRequiredService<Session>();
        Items.Clear();
        if (session.IsVanCrew)
        {
            FlyoutBehavior = FlyoutBehavior.Disabled;
            Shell.SetNavBarIsVisible(this, false);
            // Single content — no Shell tab/title chrome; DrivePage owns bottom bar.
            Items.Add(new ShellContent
            {
                Content = _services.GetRequiredService<DrivePage>()
            });
            return;
        }

        Shell.SetNavBarIsVisible(this, true);
        Items.Add(ShellTabs.Create(
            ("Карта", _services.GetRequiredService<DispatchPage>()),
            ("Визиты", _services.GetRequiredService<CoveragePage>())));
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
