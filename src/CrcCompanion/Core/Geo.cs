using System;
using System.Collections.Generic;

namespace CrcCompanion.Core
{
    public readonly struct LatLon
    {
        public readonly double Lat, Lon;
        public LatLon(double lat, double lon) { Lat = lat; Lon = lon; }
        public override string ToString() => $"{Lat:0.0000},{Lon:0.0000}";
    }

    /// <summary>A boundary made of one or more outer rings (holes are ignored).</summary>
    public sealed class Boundary
    {
        public string Id { get; }
        public IReadOnlyList<IReadOnlyList<LatLon>> Rings { get; }
        private readonly double _minLat, _maxLat, _minLon, _maxLon;

        public Boundary(string id, IReadOnlyList<IReadOnlyList<LatLon>> rings)
        {
            Id = id;
            Rings = rings;
            _minLat = double.MaxValue; _maxLat = double.MinValue; _minLon = double.MaxValue; _maxLon = double.MinValue;
            foreach (var r in rings)
                foreach (var p in r)
                {
                    _minLat = Math.Min(_minLat, p.Lat); _maxLat = Math.Max(_maxLat, p.Lat);
                    _minLon = Math.Min(_minLon, p.Lon); _maxLon = Math.Max(_maxLon, p.Lon);
                }
        }

        /// <summary>Middle of the bounding box (a reference point for picking among same-named fixes).</summary>
        public LatLon Center => new LatLon((_minLat + _maxLat) / 2, (_minLon + _maxLon) / 2);

        public bool Contains(LatLon p)
        {
            if (p.Lat < _minLat || p.Lat > _maxLat || p.Lon < _minLon || p.Lon > _maxLon) return false;
            foreach (var ring in Rings)
                if (Geo.InRing(ring, p)) return true;
            return false;
        }
    }

    public static class Geo
    {
        private const double EarthRadiusNm = 3440.065;

        /// <summary>Ray-casting point-in-polygon in the lat/lon plane (fine at ARTCC scale away from the antimeridian).</summary>
        public static bool InRing(IReadOnlyList<LatLon> ring, LatLon p)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[i]; var b = ring[j];
                if ((a.Lat > p.Lat) != (b.Lat > p.Lat))
                {
                    double x = (b.Lon - a.Lon) * (p.Lat - a.Lat) / (b.Lat - a.Lat) + a.Lon;
                    if (p.Lon < x) inside = !inside;
                }
            }
            return inside;
        }

        public static double DistanceNm(LatLon a, LatLon b)
        {
            double la1 = Rad(a.Lat), la2 = Rad(b.Lat), dLat = la2 - la1, dLon = Rad(b.Lon - a.Lon);
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(la1) * Math.Cos(la2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * EarthRadiusNm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
        }

        public static double BearingDeg(LatLon a, LatLon b)
        {
            double la1 = Rad(a.Lat), la2 = Rad(b.Lat), dLon = Rad(b.Lon - a.Lon);
            double y = Math.Sin(dLon) * Math.Cos(la2);
            double x = Math.Cos(la1) * Math.Sin(la2) - Math.Sin(la1) * Math.Cos(la2) * Math.Cos(dLon);
            return (Deg(Math.Atan2(y, x)) + 360) % 360;
        }

        /// <summary>Point reached from p after distanceNm along a great circle with the given initial course.</summary>
        public static LatLon Move(LatLon p, double courseDeg, double distanceNm)
        {
            double d = distanceNm / EarthRadiusNm, c = Rad(courseDeg), la1 = Rad(p.Lat), lo1 = Rad(p.Lon);
            double la2 = Math.Asin(Math.Sin(la1) * Math.Cos(d) + Math.Cos(la1) * Math.Sin(d) * Math.Cos(c));
            double lo2 = lo1 + Math.Atan2(Math.Sin(c) * Math.Sin(d) * Math.Cos(la1), Math.Cos(d) - Math.Sin(la1) * Math.Sin(la2));
            return new LatLon(Deg(la2), (Deg(lo2) + 540) % 360 - 180);
        }

        /// <summary>
        /// Minutes until a point moving on courseDeg at groundspeedKt first enters the boundary,
        /// checked every stepSeconds up to maxMinutes. Null if it doesn't enter in that time.
        /// </summary>
        public static double? MinutesToEnter(Boundary b, LatLon start, double courseDeg, double groundspeedKt,
            double maxMinutes, double stepSeconds = 20)
        {
            if (groundspeedKt <= 0) return null;
            for (double t = stepSeconds; t <= maxMinutes * 60 + 0.001; t += stepSeconds)
            {
                var p = Move(start, courseDeg, groundspeedKt * t / 3600.0);
                if (b.Contains(p)) return t / 60.0;
            }
            return null;
        }

        private static double Rad(double d) => d * Math.PI / 180;
        private static double Deg(double r) => r * 180 / Math.PI;
    }
}
