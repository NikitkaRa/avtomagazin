using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;

namespace Avtomagazin.StaffApp;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ApplyChrome();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
        {
            ApplyChrome();
        }
    }

    private void ApplyChrome()
    {
        var window = Window;
        if (window is null)
        {
            return;
        }

        var ink = Android.Graphics.Color.ParseColor("#12241C");
#pragma warning disable CA1422
        window.SetStatusBarColor(ink);
        window.SetNavigationBarColor(ink);
#pragma warning restore CA1422

        var controller = WindowCompat.GetInsetsController(window, window.DecorView);
        if (controller is not null)
        {
            controller.AppearanceLightStatusBars = false;
            controller.AppearanceLightNavigationBars = false;
        }
    }
}
