using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Avtomagazin.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<Session>();
        builder.Services.AddSingleton<SnapshotStore>();
        builder.Services.AddSingleton<ApiHub>();
        builder.Services.AddTransient<AppShell>();
        builder.Services.AddTransient<RolePage>();
        builder.Services.AddTransient<ResidentMapPage>();
        builder.Services.AddTransient<ResidentSchedulePage>();
        builder.Services.AddTransient<ResidentFavoritesPage>();
        builder.Services.AddTransient<ResidentStopPage>();
        builder.Services.AddTransient<DrivePage>();
        builder.Services.AddTransient<DispatchPage>();
        builder.Services.AddTransient<CoveragePage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
