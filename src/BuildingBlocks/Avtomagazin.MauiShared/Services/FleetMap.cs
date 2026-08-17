using Avtomagazin.ApiClient;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Maui;
using MBrush = Mapsui.Styles.Brush;
using MColor = Mapsui.Styles.Color;
using MFont = Mapsui.Styles.Font;

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
            map.Layers.Add(OsmLayer());
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
                Fill = new MBrush(MColor.FromArgb(255, 243, 247, 243)),
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
                Fill = new MBrush(MColor.FromArgb(255, 61, 186, 122)),
                Outline = new Pen(MColor.White),
                SymbolScale = 0.7
            }
        });

        Fit(map, points);
        control.Refresh();
    }

    /// <summary>Next stop + optional driver fix for the drive card mini-map.</summary>
    public static void RenderFocus(
        MapControl control,
        double stopLat,
        double stopLng,
        string stopLabel,
        double? meLat,
        double? meLng,
        bool tiles = true)
    {
        var map = control.Map;
        map.Layers.Clear();
        if (tiles && Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
        {
            map.Layers.Add(OsmLayer());
        }

        var stopPoint = FromLonLat(stopLng, stopLat);
        var points = new List<MPoint> { stopPoint };

        if (meLat is double lat && meLng is double lng)
        {
            var me = FromLonLat(lng, lat);
            points.Add(me);
            map.Layers.Add(new MemoryLayer
            {
                Name = "me",
                Features = [LabeledPoint(me, "Я")],
                Style = null
            });
        }

        map.Layers.Add(new MemoryLayer
        {
            Name = "stop",
            Features = [LabeledPoint(stopPoint, ShortLabel(stopLabel), isStop: true)],
            Style = null
        });

        Fit(map, points, padFraction: 0.4);
        control.Refresh();
    }

    private static TileLayer OsmLayer()
    {
        var layer = OpenStreetMap.CreateTileLayer("AvtomagazinMobile/1.0");
        if (layer.Attribution is not null)
        {
            layer.Attribution.Enabled = false;
        }

        return layer;
    }

    private static string ShortLabel(string name)
        => name.Length <= 14 ? name : name[..13] + "…";

    private static IFeature LabeledPoint(MPoint point, string label, bool isStop = false)
    {
        var feature = new PointFeature(point);
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = isStop ? SymbolType.Ellipse : SymbolType.Triangle,
            Fill = new MBrush(isStop
                ? MColor.FromArgb(255, 243, 247, 243)
                : MColor.FromArgb(255, 61, 186, 122)),
            Outline = new Pen(
                isStop ? MColor.FromArgb(255, 27, 51, 40) : MColor.White,
                isStop ? 2.5 : 3),
            SymbolScale = isStop ? 0.7 : 0.95
        });
        feature.Styles.Add(new LabelStyle
        {
            Text = label,
            ForeColor = isStop ? MColor.FromArgb(255, 18, 36, 28) : MColor.White,
            BackColor = new MBrush(isStop
                ? MColor.FromArgb(235, 243, 247, 243)
                : MColor.FromArgb(235, 27, 120, 80)),
            BorderThickness = 0,
            CornerRounding = 6,
            Font = new MFont { Size = 13, Bold = !isStop },
            Offset = new Offset(0, isStop ? 22 : -26),
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
            VerticalAlignment = isStop
                ? LabelStyle.VerticalAlignmentEnum.Top
                : LabelStyle.VerticalAlignmentEnum.Bottom
        });
        return feature;
    }

    private static void Fit(Mapsui.Map map, List<MPoint> points, double padFraction = 0.12)
    {
        if (points.Count == 0)
        {
            return;
        }

        if (points.Count == 1)
        {
            map.Navigator.CenterOnAndZoomTo(points[0], map.Navigator.Resolutions[14]);
            return;
        }

        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxX = points.Max(p => p.X);
        var maxY = points.Max(p => p.Y);
        var padX = Math.Max((maxX - minX) * padFraction, 80);
        var padY = Math.Max((maxY - minY) * padFraction, 80);
        map.Navigator.ZoomToBox(new MRect(minX - padX, minY - padY, maxX + padX, maxY + padY), MBoxFit.Fit);
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
