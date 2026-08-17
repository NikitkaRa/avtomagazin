using Avtomagazin.ApiClient;
using BruTile.Cache;
using Mapsui.Tiling;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Avtomagazin.MauiShared;

public static class MauiSharedHost
{
    private static bool _entryChromeMapped;

    public static MauiAppBuilder AddAvtomagazinShared(this MauiAppBuilder builder, AppFlavor flavor)
    {
        builder.UseSkiaSharp();
        EnsureOsmTileCache();
        MapEntryChrome();
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

    private static void MapEntryChrome()
    {
        if (_entryChromeMapped)
        {
            return;
        }

        _entryChromeMapped = true;
        EntryHandler.Mapper.AppendToMapping("AvtoNoUnderline", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.Background = null;
            handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
            handler.PlatformView.BackgroundTintList =
                Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif IOS || MACCATALYST
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
            handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;
#endif
        });
    }

    private static void EnsureOsmTileCache()
    {
        if (OpenStreetMap.DefaultCache is not null)
        {
            return;
        }

        try
        {
            var dir = Path.Combine(FileSystem.CacheDirectory, "osm-tiles");
            Directory.CreateDirectory(dir);
            OpenStreetMap.DefaultCache = new FileCache(dir, "png");
        }
        catch
        {
            // Map still works without disk cache.
        }
    }
}
