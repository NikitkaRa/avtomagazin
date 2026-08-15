using Avtomagazin.ApiClient;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Avtomagazin.MauiShared;

public static class MauiSharedHost
{
    public static MauiAppBuilder AddAvtomagazinShared(this MauiAppBuilder builder, AppFlavor flavor)
    {
        builder.UseSkiaSharp();
        builder.Services.AddSingleton(flavor);
        builder.Services.AddSingleton<Session>();
        builder.Services.AddSingleton<SnapshotStore>();
        builder.Services.AddSingleton<ApiHub>();
        builder.Services.AddTransient<LoginPage>();
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
        return builder;
    }
}
