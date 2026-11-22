using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CrcCompanion.Core
{
    /// <summary>
    /// Fixes and airports read from CRC's own NavData.dat (%LOCALAPPDATA%\CRC\NavData.dat).
    /// The file is protobuf: top-level field 1 = airport {1 id, 2 icao, 3 artcc, 4 name, 5 elevation, 6 pos},
    /// field 2 = fix {1 id, 2 pos}, pos = {1 lat (double), 2 lon (double)}. Other fields (airways,
    /// procedures) are skipped. Names aren't unique worldwide, so lookups pick the one nearest a reference point.
    /// </summary>
    public sealed class NavDatabase
    {
        private readonly Dictionary<string, List<LatLon>> _fixes = new Dictionary<string, List<LatLon>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<LatLon>> _airports = new Dictionary<string, List<LatLon>>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Flat list of fixes for nearest-fix searches.</summary>
        private readonly List<(string Id, LatLon Pos)> _fixList = new List<(string, LatLon)>();

        public int FixCount => _fixList.Count;
        public int AirportCount { get; private set; }

        public static string DefaultPath() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CRC", "NavData.dat");

        public static NavDatabase? TryLoad(string path)
        {
            try { return File.Exists(path) ? Parse(File.ReadAllBytes(path)) : null; }
            catch (Exception) { return null; }
        }

        public static NavDatabase Parse(byte[] data)
        {
            var db = new NavDatabase();
            var r = new ProtoReader(data, 0, data.Length);
            while (r.Next(out int field, out int wire))
            {
                if (wire != 2) { r.Skip(wire); continue; }
                var (start, len) = r.Bytes();
                if (field == 1) db.ReadAirport(new ProtoReader(data, start, len));
                else if (field == 2) db.ReadFix(new ProtoReader(data, start, len));
            }
            return db;
        }

        /// <summary>Adds a point by hand (used by tests and to merge map-data airports).</summary>
        public void AddFix(string id, LatLon pos) => Add(_fixes, id, pos, true);
        public void AddAirport(string id, LatLon pos) { Add(_airports, id, pos, false); AirportCount++; }

        private void Add(Dictionary<string, List<LatLon>> d, string id, LatLon pos, bool isFix)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            id = id.Trim().ToUpperInvariant();
            if (!d.TryGetValue(id, out var l)) d[id] = l = new List<LatLon>(1);
            l.Add(pos);
            if (isFix) _fixList.Add((id, pos));
        }

        private void ReadAirport(ProtoReader r)
        {
            string id = "", icao = "";
            LatLon? pos = null;
            while (r.Next(out int f, out int w))
            {
                if (w == 2 && f == 1) id = r.String();
                else if (w == 2 && f == 2) icao = r.String();
                else if (w == 2 && f == 6) pos = ReadPos(ref r);
                else r.Skip(w);
            }
            if (pos == null) return;
            if (id.Length > 0) Add(_airports, id, pos.Value, false);
            if (icao.Length > 0 && !string.Equals(icao, id, StringComparison.OrdinalIgnoreCase)) Add(_airports, icao, pos.Value, false);
            AirportCount++;
        }

        private void ReadFix(ProtoReader r)
        {
            string id = "";
            LatLon? pos = null;
            while (r.Next(out int f, out int w))
            {
                if (w == 2 && f == 1) id = r.String();
                else if (w == 2 && f == 2) pos = ReadPos(ref r);
                else r.Skip(w);
            }
            if (pos != null && id.Length > 0) Add(_fixes, id, pos.Value, true);
        }

        private static LatLon? ReadPos(ref ProtoReader outer)
        {
            var (s, n) = outer.Bytes();
            var r = outer.Sub(s, n);
            double lat = double.NaN, lon = double.NaN;
            while (r.Next(out int f, out int w))
            {
                if (w == 1 && f == 1) lat = r.Double();
                else if (w == 1 && f == 2) lon = r.Double();
                else r.Skip(w);
            }
            if (double.IsNaN(lat) || double.IsNaN(lon) || Math.Abs(lat) > 90 || Math.Abs(lon) > 180) return null;
            return new LatLon(lat, lon);
        }

        /// <summary>Fix or airport by name, nearest to <paramref name="near"/>. Fixes win over airports at the same name.</summary>
        public LatLon? Find(string id, LatLon near)
        {
            id = (id ?? "").Trim().ToUpperInvariant();
            if (id.Length == 0) return null;
            if (_fixes.TryGetValue(id, out var f)) return Nearest(f, near);
            if (_airports.TryGetValue(id, out var a)) return Nearest(a, near);
            return null;
        }

        public bool IsAirport(string id) => _airports.ContainsKey((id ?? "").Trim());

        /// <summary>Nearest fix to a point within maxNm (named fixes only, not lat/lon-style names).</summary>
        public (string Id, double Nm)? NearestFix(LatLon p, double maxNm)
        {
            (string, double)? best = null;
            double dLat = maxNm / 60.0;
            foreach (var (id, pos) in _fixList)
            {
                if (Math.Abs(pos.Lat - p.Lat) > dLat) continue;
                if (!IsNamedFix(id)) continue;
                var d = Geo.DistanceNm(p, pos);
                if (d <= maxNm && (best == null || d < best.Value.Item2)) best = (id, d);
            }
            return best;
        }

        /// <summary>Five-letter intersections and 2-3 letter navaids (skips "090A"-style grid points).</summary>
        private static bool IsNamedFix(string id)
        {
            if (id.Length < 2 || id.Length > 5) return false;
            foreach (var c in id) if (c < 'A' || c > 'Z') return false;
            return true;
        }

        private static LatLon Nearest(List<LatLon> l, LatLon near)
        {
            var best = l[0];
            double bd = double.MaxValue;
            foreach (var p in l)
            {
                var d = Geo.DistanceNm(p, near);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        /// <summary>Minimal protobuf reader over a byte range.</summary>
        private struct ProtoReader
        {
            private readonly byte[] _b;
            private int _i;
            private readonly int _end;

            public ProtoReader(byte[] b, int start, int len) { _b = b; _i = start; _end = Math.Min(b.Length, start + len); }

            public ProtoReader Sub(int start, int len) => new ProtoReader(_b, start, len);

            public bool Next(out int field, out int wire)
            {
                field = wire = 0;
                if (_i >= _end) return false;
                ulong key = Varint();
                field = (int)(key >> 3);
                wire = (int)(key & 7);
                return true;
            }

            public ulong Varint()
            {
                ulong r = 0;
                int shift = 0;
                while (_i < _end)
                {
                    byte c = _b[_i++];
                    r |= (ulong)(c & 0x7F) << shift;
                    if (c < 0x80) return r;
                    shift += 7;
                    if (shift > 63) throw new FormatException("bad varint");
                }
                throw new FormatException("truncated varint");
            }

            public (int Start, int Len) Bytes()
            {
                int n = (int)Varint();
                if (n < 0 || _i + n > _end) throw new FormatException("bad length");
                int s = _i;
                _i += n;
                return (s, n);
            }

            public string String()
            {
                var (s, n) = Bytes();
                return System.Text.Encoding.UTF8.GetString(_b, s, n);
            }

            public double Double()
            {
                if (_i + 8 > _end) throw new FormatException("truncated double");
                double d = BitConverter.ToDouble(_b, _i);
                _i += 8;
                return d;
            }

            public void Skip(int wire)
            {
                switch (wire)
                {
                    case 0: Varint(); break;
                    case 1: _i += 8; break;
                    case 2: Bytes(); break;
                    case 5: _i += 4; break;
                    default: throw new FormatException("unsupported wire type " + wire.ToString(CultureInfo.InvariantCulture));
                }
            }
        }
    }

    /// <summary>
    /// ERAM location entry: a fix or airport ("BOS"), a fix-radial-distance ("BOS090010" = 090°, 10 NM),
    /// or a latitude/longitude ("4220N/7110W"). An optional "//" prefix is accepted, as in CRC's LF command.
    /// Bearings are used as true (no magnetic variation data).
    /// </summary>
    public static class Locations
    {
        public enum Kind { Fix, Frd, LatLon }

        public static (LatLon Pos, Kind Kind, string Name)? Parse(string text, Func<string, LatLon?> lookup)
        {
            var t = (text ?? "").Trim().ToUpperInvariant();
            if (t.StartsWith("//", StringComparison.Ordinal)) t = t.Substring(2);
            if (t.Length == 0) return null;

            var ll = ParseLatLon(t);
            if (ll != null) return (ll.Value, Kind.LatLon, t);

            var direct = lookup(t);
            if (direct != null) return (direct.Value, Kind.Fix, t);

            // FRD: name + 3-digit bearing + 3-digit distance.
            if (t.Length > 6 && AllDigits(t.Substring(t.Length - 6)))
            {
                var name = t.Substring(0, t.Length - 6);
                int brg = int.Parse(t.Substring(t.Length - 6, 3), CultureInfo.InvariantCulture);
                int dist = int.Parse(t.Substring(t.Length - 3), CultureInfo.InvariantCulture);
                var basePos = lookup(name);
                if (basePos != null && brg <= 360)
                    return (Geo.Move(basePos.Value, brg % 360, dist), Kind.Frd, t);
            }
            return null;
        }

        /// <summary>"4220N/7110W", "4220N07110W", "422030N/0711015W" (degrees, minutes, optional seconds).</summary>
        public static LatLon? ParseLatLon(string t)
        {
            t = t.Replace("/", "");
            int ns = t.IndexOfAny(new[] { 'N', 'S' });
            if (ns <= 0 || ns == t.Length - 1) return null;
            char ew = t[^1];
            if (ew != 'E' && ew != 'W') return null;
            var latPart = t.Substring(0, ns);
            var lonPart = t.Substring(ns + 1, t.Length - ns - 2);
            var lat = Dms(latPart, 2);
            var lon = Dms(lonPart, 3);
            if (lat == null || lon == null || lat > 90 || lon > 180) return null;
            return new LatLon(t[ns] == 'S' ? -lat.Value : lat.Value, ew == 'W' ? -lon.Value : lon.Value);
        }

        private static double? Dms(string s, int degDigits)
        {
            if (!AllDigits(s)) return null;
            // Allow a missing leading zero on longitude (7110 = 071°10').
            if (degDigits == 3 && (s.Length == 4 || s.Length == 6)) s = "0" + s;
            if (s.Length != degDigits + 2 && s.Length != degDigits + 4) return null;
            int deg = int.Parse(s.Substring(0, degDigits), CultureInfo.InvariantCulture);
            int min = int.Parse(s.Substring(degDigits, 2), CultureInfo.InvariantCulture);
            int sec = s.Length > degDigits + 2 ? int.Parse(s.Substring(degDigits + 2, 2), CultureInfo.InvariantCulture) : 0;
            if (min >= 60 || sec >= 60) return null;
            return deg + min / 60.0 + sec / 3600.0;
        }

        private static bool AllDigits(string s)
        {
            if (s.Length == 0) return false;
            foreach (var c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>8-point compass direction for a bearing ("N", "NE", ...).</summary>
        public static string Compass(double bearing)
        {
            string[] d = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return d[(int)Math.Round(((bearing % 360) + 360) % 360 / 45.0) % 8];
        }
    }
}
