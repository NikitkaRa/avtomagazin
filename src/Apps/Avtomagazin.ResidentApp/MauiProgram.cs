using Avtomagazin.MauiShared;
using Microsoft.Extensions.DependencyInjection;

namespace Avtomagazin.ResidentApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .AddAvtomagazinShared(AppFlavor.Resident)
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });
        builder.Services.AddTransient<AppShell>();
        return builder.Build();
    }
}
