using Microsoft.Extensions.DependencyInjection;

namespace Avtomagazin.Mobile;

public partial class AppShell : Shell
{
    private readonly IServiceProvider _services;

    public AppShell(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        Routing.RegisterRoute("stop", typeof(ResidentStopPage));
        ShowRole();
    }

    public void ShowRole()
    {
        Items.Clear();
        Items.Add(new ShellContent
        {
            Title = "Автомагазин",
            Content = _services.GetRequiredService<RolePage>()
        });
    }

    public void ShowResident()
    {
        Items.Clear();
        Items.Add(Tabs(
            ("Карта", _services.GetRequiredService<ResidentMapPage>()),
            ("Рейс", _services.GetRequiredService<ResidentSchedulePage>()),
            ("Избранное", _services.GetRequiredService<ResidentFavoritesPage>())));
    }

    public void ShowDriver()
    {
        Items.Clear();
        Items.Add(Tabs(("Рейс", _services.GetRequiredService<DrivePage>())));
    }

    public void ShowOperator()
    {
        Items.Clear();
        Items.Add(Tabs(
            ("Карта", _services.GetRequiredService<DispatchPage>()),
            ("Визиты", _services.GetRequiredService<CoveragePage>())));
    }

    private static TabBar Tabs(params (string Title, Page Page)[] items)
    {
        var bar = new TabBar();
        foreach (var item in items)
        {
            bar.Items.Add(new ShellContent
            {
                Title = item.Title,
                Content = item.Page
            });
        }

        return bar;
    }
}
