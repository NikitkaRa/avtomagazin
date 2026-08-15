using Avtomagazin.ApiClient;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.Maui;
using MColor = Mapsui.Styles.Color;

namespace Avtomagazin.MauiShared;

public static class FleetMap
{
    public static void Render(
        MapControl control,
        SnapshotDto data,
        Guid? vehicleId = null,
        bool tiles = true)
    {
        var map = control.Map;
        map.Layers.Clear();
        if (tiles && Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
        {
            map.Layers.Add(OpenStreetMap.CreateTileLayer("AvtomagazinMobile/1.0"));
        }

        var stops = new List<IFeature>();
        var vans = new List<IFeature>();
        var points = new List<MPoint>();

        foreach (var route in data.Routes.Where(r => vehicleId is null || r.VehicleId == vehicleId))
        {
            foreach (var stop in route.Stops ?? [])
            {
                var point = FromLonLat(stop.Longitude, stop.Latitude);
                points.Add(point);
                stops.Add(Point(point, stop.SettlementName));
            }
        }

        foreach (var van in data.Vehicles.Where(v =>
                     v.LastLatitude is not null
                     && (vehicleId is null || v.Id == vehicleId)))
        {
            var point = FromLonLat(van.LastLongitude!.Value, van.LastLatitude!.Value);
            points.Add(point);
            vans.Add(Point(point, van.PlateNumber));
        }

        map.Layers.Add(new MemoryLayer
        {
            Name = "stops",
            Features = stops,
            Style = new SymbolStyle
            {
                Fill = new Mapsui.Styles.Brush(MColor.FromArgb(255, 243, 247, 243)),
                Outline = new Pen(MColor.FromArgb(255, 27, 51, 40)),
                SymbolScale = 0.45
            }
        });
        map.Layers.Add(new MemoryLayer
        {
            Name = "vans",
            Features = vans,
            Style = new SymbolStyle
            {
                Fill = new Mapsui.Styles.Brush(MColor.FromArgb(255, 61, 186, 122)),
                Outline = new Pen(MColor.White),
                SymbolScale = 0.7
            }
        });

        if (points.Count == 1)
        {
            map.Navigator.CenterOnAndZoomTo(points[0], map.Navigator.Resolutions[12]);
        }
        else if (points.Count > 1)
        {
            var minX = points.Min(p => p.X);
            var minY = points.Min(p => p.Y);
            var maxX = points.Max(p => p.X);
            var maxY = points.Max(p => p.Y);
            map.Navigator.ZoomToBox(new MRect(minX, minY, maxX, maxY), MBoxFit.Fit);
        }

        control.Refresh();
    }

    private static MPoint FromLonLat(double lon, double lat)
    {
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        return new MPoint(x, y);
    }

    private static IFeature Point(MPoint point, string label)
    {
        var feature = new PointFeature(point);
        feature["label"] = label;
        return feature;
    }
}
