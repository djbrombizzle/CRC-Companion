using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace CrcCompanion.Core
{
    /// <summary>A frozen departure release (EDCT) issued in VATFLOW.</summary>
    public sealed class FcaRelease
    {
        public long? CtaMs { get; init; }
        public long? EdctMs { get; init; }
    }

    /// <summary>
    /// One VATFLOW FCA (flow constrained area) program, as stored in VATFLOW's Supabase table <c>fcas</c>
    /// (<c>{id, data, updated_at}</c>; <c>data</c> is the program). Field meanings follow
    /// vatflow/shared/fca-metering.js.
    /// </summary>
    public sealed class FcaProgram
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        /// <summary>Owning ARTCC ("ZDC").</summary>
        public string Artcc { get; init; } = "";
        /// <summary>"mit" (miles in trail) or "rate" (per hour).</summary>
        public string Mode { get; init; } = "rate";
        public double Mit { get; init; }
        public double Rate { get; init; }
        public int? MinFL { get; init; }
        public int? MaxFL { get; init; }
        public string Dir { get; init; } = "any";
        public double? LookaheadNm { get; init; }
        public List<string> Dests { get; init; } = new List<string>();
        public List<string> Origins { get; init; } = new List<string>();
        public List<string> Fixes { get; init; } = new List<string>();
        public List<string> Scope { get; init; } = new List<string>();
        public List<LatLon> Points { get; init; } = new List<LatLon>();
        public bool Enabled { get; init; } = true;
        public List<string> Excluded { get; init; } = new List<string>();
        public List<string> Order { get; init; } = new List<string>();
        public bool ManualSeq { get; init; }
        public Dictionary<string, FcaRelease> Releases { get; init; } = new Dictionary<string, FcaRelease>(StringComparer.OrdinalIgnoreCase);
        /// <summary>VATFLOW display color ("#a3e635").</summary>
        public string Color { get; init; } = "";
        public DateTime UpdatedUtc { get; init; }

        /// <summary>Sublist title: the fix in the name's brackets ("DCA [CAPSS#]" → CAPSS), else the fixes, else the name.</summary>
        public string FixTitle
        {
            get
            {
                var n = Name ?? "";
                int a = n.IndexOfAny(new[] { '[', '(' });
                int b = a < 0 ? -1 : n.LastIndexOf(n[a] == '[' ? ']' : ')');
                if (a >= 0 && b > a)
                {
                    var inner = n.Substring(a + 1, b - a - 1).Replace("#", "").Trim();
                    // "RAVNN (WALKN)" -> "RAVNN": drop a nested parenthetical.
                    int nested = inner.IndexOf('(');
                    if (nested > 0) inner = inner.Substring(0, nested).Trim();
                    if (inner.StartsWith("VIA ", StringComparison.OrdinalIgnoreCase)) inner = inner.Substring(4).Trim();
                    if (inner.Length > 0) return inner.ToUpperInvariant();
                }
                if (Fixes.Count > 0) return string.Join("/", Fixes).ToUpperInvariant();
                return n.ToUpperInvariant();
            }
        }

        /// <summary>Header: destination, then the restriction ("DCA MIT 35", "MCO RATE 30").</summary>
        public string Header
        {
            get
            {
                var dest = Dests.Count > 0 ? Dests[0].ToUpperInvariant() : "";
                if (dest.Length == 4 && dest[0] == 'K') dest = dest.Substring(1);
                var r = Mode == "mit"
                    ? "MIT " + Mit.ToString("0.#", CultureInfo.InvariantCulture)
                    : "RATE " + Rate.ToString("0.#", CultureInfo.InvariantCulture);
                return (dest.Length > 0 ? dest + " " : "") + r;
            }
        }

        /// <summary>Parses Supabase rows (<c>[{id, data, updated_at}, ...]</c>).</summary>
        public static List<FcaProgram> ParseRows(string json)
        {
            var list = new List<FcaProgram>();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (!row.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object) continue;
                var updated = row.TryGetProperty("updated_at", out var u) && u.ValueKind == JsonValueKind.String &&
                              DateTime.TryParse(u.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ut)
                    ? ut : DateTime.MinValue;
                var f = Parse(d, Str(row, "id"), updated);
                if (f != null) list.Add(f);
            }
            return list;
        }

        public static FcaProgram? Parse(JsonElement d, string rowId = "", DateTime updated = default)
        {
            var id = Str(d, "id");
            if (id.Length == 0) id = rowId;
            if (id.Length == 0) return null;
            var points = new List<LatLon>();
            if (d.TryGetProperty("points", out var pts) && pts.ValueKind == JsonValueKind.Array)
                foreach (var pt in pts.EnumerateArray())
                    if (pt.ValueKind == JsonValueKind.Array && pt.GetArrayLength() >= 2 &&
                        pt[0].TryGetDouble(out var la) && pt[1].TryGetDouble(out var lo))
                        points.Add(new LatLon(la, lo));
            var rel = new Dictionary<string, FcaRelease>(StringComparer.OrdinalIgnoreCase);
            if (d.TryGetProperty("releases", out var r) && r.ValueKind == JsonValueKind.Object)
                foreach (var kv in r.EnumerateObject())
                    if (kv.Value.ValueKind == JsonValueKind.Object)
                        rel[kv.Name] = new FcaRelease { CtaMs = Long(kv.Value, "ctaMs"), EdctMs = Long(kv.Value, "edctMs") };
            return new FcaProgram
            {
                Id = id,
                Name = Str(d, "name"),
                Artcc = Str(d, "artcc").ToUpperInvariant(),
                Mode = Str(d, "mode") == "mit" ? "mit" : "rate",
                Mit = Num(d, "mit") ?? 0,
                Rate = Num(d, "rate") ?? 0,
                MinFL = (int?)Num(d, "minFL"),
                MaxFL = (int?)Num(d, "maxFL"),
                Dir = Str(d, "dir") is { Length: > 0 } dir ? dir : "any",
                LookaheadNm = Num(d, "lookaheadNm"),
                Dests = Strs(d, "dests"),
                Origins = Strs(d, "origins"),
                Fixes = Strs(d, "fixes"),
                Scope = Strs(d, "scope"),
                Points = points,
                Enabled = !(d.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False),
                Excluded = Strs(d, "excluded"),
                Order = Strs(d, "order"),
                ManualSeq = d.TryGetProperty("manualSeq", out var ms) && ms.ValueKind == JsonValueKind.True,
                Releases = rel,
                Color = Str(d, "color"),
                UpdatedUtc = updated,
            };
        }

        private static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        private static double? Num(JsonElement e, string n)
        {
            if (!e.TryGetProperty(n, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var x)) return x;
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return y;
            return null;
        }
        private static long? Long(JsonElement e, string n) => Num(e, n) is double x ? (long)x : null;
        private static List<string> Strs(JsonElement e, string n)
        {
            var l = new List<string>();
            if (e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Array)
                foreach (var s in v.EnumerateArray())
                    if (s.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(s.GetString())) l.Add(s.GetString()!.Trim().ToUpperInvariant());
            return l;
        }
    }

    /// <summary>A pilot as the metering engine sees it (vatsimToMeterPilot in VATFLOW).</summary>
    public sealed class FcaPilot
    {
        public string Callsign { get; init; } = "";
        public LatLon Pos { get; init; }
        /// <summary>Feet.</summary>
        public int Alt { get; init; }
        public int Gs { get; init; }
        public int Hdg { get; init; }
        public string Dep { get; init; } = "";
        public string Arr { get; init; } = "";
        public string Route { get; init; } = "";
        /// <summary>Filed altitude, feet.</summary>
        public int FpAlt { get; init; }
        public int Tas { get; init; }
        /// <summary>Filed departure time "HHMM".</summary>
        public string DepTime { get; init; } = "";
        public bool Airborne => Gs >= FcaEngine.AirMinGs;
    }

    /// <summary>Positions the engine needs: airports, fixes, ARTCC membership.</summary>
    public interface IFcaWorld
    {
        /// <summary>An airport by its ICAO code (exact, as VATFLOW's airport table).</summary>
        LatLon? Airport(string icao);
        /// <summary>VATFLOW's route data, when loaded; without it routes resolve through <see cref="Fix"/>.</summary>
        VatflowNav? Nav { get; }
        /// <summary>A named fix or navaid, nearest <paramref name="near"/>.</summary>
        LatLon? Fix(string name, LatLon near);
        /// <summary>Whether a point is inside an ARTCC ("ZDC"); null when that ARTCC's boundary is unknown.</summary>
        bool? InArtcc(string artcc, LatLon p);
    }

    /// <summary>One flight in an FCA's sequence.</summary>
    public sealed class FcaItem
    {
        public FcaPilot Pilot { get; init; } = null!;
        public bool Ground { get; init; }
        /// <summary>Distance along the route to the FCA line, NM.</summary>
        public double DistNm { get; init; }
        /// <summary>Unconstrained crossing, seconds from now.</summary>
        public double EtaSec { get; init; }
        public double CrossSpd { get; init; }
        public double TransitSec { get; init; }
        /// <summary>Scheduled crossing (CTA), seconds from now.</summary>
        public double SchedSec { get; set; }
        public bool Frozen { get; set; }
        public bool Conflict { get; set; }
        /// <summary>Airborne auto sequence: seconds short of the required spacing behind the previous ETA.</summary>
        public double ConflictShortSec { get; set; }
        public double DelaySec => SchedSec - EtaSec;
    }

    public sealed class FcaSequence
    {
        public FcaProgram Fca { get; init; } = null!;
        /// <summary>In crossing order (earliest first).</summary>
        public List<FcaItem> Items { get; } = new List<FcaItem>();
        public bool Manual { get; set; }
    }

    /// <summary>
    /// C# port of VATFLOW's FCA sequencing (vatflow/shared/fca-metering.js computeSequence): filters (destination,
    /// origin, route fix, ARTCC scope, altitude band, exclusions), crossing along the filed route, ETA (groundspeed
    /// when cruising; climb profile for departures still climbing and for ground departures), then scheduling:
    /// airborne at ETA with conflicts flagged, frozen releases pinned, advisory ground departures slotted around
    /// them; or the program's manual order chained by separation.
    /// Differences: routes resolve through the given fixes and airports (airways and procedures aren't expanded,
    /// so a fix filter matches filed tokens only), and winds aloft aren't applied (VATFLOW's still-air fallback).
    /// </summary>
    public static class FcaEngine
    {
        public const int AirMinGs = 50;
        public const double LookaheadNm = 2500;
        public const double ReadyBufferSec = 180;
        public const double SpdBelow10k = 250, SpdClimb = 290, ClimbFpmLow = 2000, ClimbFpmHigh = 1500;
        public const double DefaultGroundGs = 250;
        private const double FreezeTolSec = 30;
        private const long ComplianceLateMs = 60000;

        // ---------------- filters ----------------

        public static bool AirportCodesMatch(string a, string b)
        {
            a = (a ?? "").Trim().ToUpperInvariant();
            b = (b ?? "").Trim().ToUpperInvariant();
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            if (a.Length == 4 && b.Length == 3 && a.Substring(1) == b) return true;
            return b.Length == 4 && a.Length == 3 && b.Substring(1) == a;
        }

        public static bool MatchesAlt(FcaProgram f, int altFt)
        {
            double min = f.MinFL != null ? f.MinFL.Value * 100.0 : -1;
            double max = f.MaxFL != null ? f.MaxFL.Value * 100.0 : 1e9;
            return altFt >= min && altFt <= max;
        }

        public static bool MatchesDest(FcaProgram f, string arr) => f.Dests.Count == 0 || f.Dests.Any(d => AirportCodesMatch(d, arr));
        public static bool MatchesOrigin(FcaProgram f, string dep) => f.Origins.Count == 0 || f.Origins.Any(d => AirportCodesMatch(d, dep));

        public static List<string> RouteTokens(string route) =>
            (route ?? "").ToUpperInvariant().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t != "DCT").ToList();

        /// <summary>Filed-token fix match: "LAIRI" matches LAIRI and a procedure named after it (LAIRI4).</summary>
        public static bool MatchesFix(FcaProgram f, string route)
        {
            if (f.Fixes.Count == 0) return true;
            var toks = RouteTokens(route).Select(t => t.Split('/')[0]).ToList();
            return f.Fixes.Any(fx => toks.Any(t => t == fx || StripProcedureSuffix(t) == fx));
        }

        /// <summary>Fix match on the filed tokens, then (with nav data) on the expanded route ("ESSSO" inside a filed PAATS4).</summary>
        public static bool MatchesFix(FcaProgram f, FcaPilot p, IFcaWorld w)
        {
            if (MatchesFix(f, p.Route)) return true;
            var nav = w.Nav;
            if (nav == null) return false;
            var key = p.Dep + "|" + p.Arr + "|" + p.Route;
            if (!nav.RouteNameCache.TryGetValue(key, out var names))
            {
                if (nav.RouteNameCache.Count > 4000) nav.RouteNameCache.Clear();
                names = new HashSet<string>(FcaRoute.BuildAnchors(nav, p.Dep, p.Arr, p.Route, w.Airport).Select(a => a.Name.ToUpperInvariant()));
                nav.RouteNameCache[key] = names;
            }
            return f.Fixes.Any(names.Contains);
        }

        private static string StripProcedureSuffix(string t)
        {
            // JS: t.replace(/\d[A-Z]?$/, "")
            if (t.Length >= 2 && char.IsLetter(t[^1]) && char.IsDigit(t[^2])) return t.Substring(0, t.Length - 2);
            if (t.Length >= 1 && char.IsDigit(t[^1])) return t.Substring(0, t.Length - 1);
            return t;
        }

        /// <summary>Inside one of the scoped ARTCCs; fails open when no scoped boundary is known.</summary>
        public static bool MatchesScope(FcaProgram f, LatLon? pos, IFcaWorld w)
        {
            if (f.Scope.Count == 0 || pos == null) return true;
            bool anyKnown = false;
            foreach (var id in f.Scope)
            {
                var r = w.InArtcc(id, pos.Value);
                if (r == true) return true;
                if (r != null) anyKnown = true;
            }
            return !anyKnown;
        }

        // ---------------- geometry ----------------

        private static (double X, double Y) ToLocal(LatLon p, LatLon o) =>
            ((p.Lon - o.Lon) * 60 * Math.Cos(o.Lat * Math.PI / 180), (p.Lat - o.Lat) * 60);

        private static LatLon FromLocal(double x, double y, LatLon o) =>
            new LatLon(o.Lat + y / 60, o.Lon + x / (60 * Math.Cos(o.Lat * Math.PI / 180)));

        private static (double T, double U)? SegInt((double X, double Y) p1, (double X, double Y) p2, (double X, double Y) a, (double X, double Y) b)
        {
            double rx = p2.X - p1.X, ry = p2.Y - p1.Y, sx = b.X - a.X, sy = b.Y - a.Y;
            double den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-9) return null;
            double qx = a.X - p1.X, qy = a.Y - p1.Y;
            double t = (qx * sy - qy * sx) / den, u = (qx * ry - qy * rx) / den;
            return t >= 0 && t <= 1 && u >= 0 && u <= 1 ? (t, u) : null;
        }

        /// <summary>First crossing of the FCA line along a path: distance (NM) from the path's start, and where.</summary>
        public static (double Dist, LatLon Pos)? PathCrossing(IReadOnlyList<LatLon> path, IReadOnlyList<LatLon> line)
        {
            double cum = 0;
            for (int k = 0; k < path.Count - 1; k++)
            {
                var a = path[k];
                var b = path[k + 1];
                double leg = Geo.DistanceNm(a, b);
                if (leg < 0.01) continue;
                var p2 = ToLocal(b, a);
                (double Dist, LatLon Pos)? best = null;
                for (int i = 0; i < line.Count - 1; i++)
                {
                    var x = SegInt((0, 0), p2, ToLocal(line[i], a), ToLocal(line[i + 1], a));
                    if (x == null) continue;
                    double d = cum + x.Value.T * leg;
                    if (best == null || d < best.Value.Dist) best = (d, FromLocal(x.Value.T * p2.X, x.Value.T * p2.Y, a));
                }
                if (best != null) return best;
                cum += leg;
            }
            return null;
        }

        private static (LatLon Pos, double Dist)? NearestOnLine(LatLon p, IReadOnlyList<LatLon> pts)
        {
            (LatLon, double)? best = null;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var o = pts[i];
                var a = ToLocal(pts[i], o);
                var b = ToLocal(pts[i + 1], o);
                var q = ToLocal(p, o);
                double abx = b.X - a.X, aby = b.Y - a.Y, len2 = abx * abx + aby * aby;
                double t = len2 > 0 ? ((q.X - a.X) * abx + (q.Y - a.Y) * aby) / len2 : 0;
                t = Math.Clamp(t, 0, 1);
                double px = a.X + t * abx, py = a.Y + t * aby;
                double d = Math.Sqrt((px - q.X) * (px - q.X) + (py - q.Y) * (py - q.Y));
                if (best == null || d < best.Value.Item2) best = (FromLocal(px, py, o), d);
            }
            return best;
        }

        private static double AngleDiff(double a, double b) => Math.Abs(((a - b) + 540) % 360 - 180);

        /// <summary>Directional FCA: airborne, on the exit side and heading away.</summary>
        public static bool HasPassed(FcaPilot p, FcaProgram f)
        {
            if (!p.Airborne || f.Points.Count < 2) return false;
            double? flow = f.Dir switch { "N" => 0, "E" => 90, "S" => 180, "W" => 270, _ => null };
            if (flow == null) return false;
            var near = NearestOnLine(p.Pos, f.Points);
            if (near == null || near.Value.Dist < 2) return false;
            double toAc = Geo.BearingDeg(near.Value.Pos, p.Pos);
            return AngleDiff(toAc, flow.Value) < 85 && AngleDiff(p.Hdg, flow.Value) < 85;
        }

        /// <summary>
        /// The route path: departure airport (or the present position when airborne, joining the route at the leg
        /// nearest the aircraft), filed fixes that resolve, destination. Null when the destination is unknown.
        /// </summary>
        public static List<LatLon>? RoutePath(FcaPilot p, IFcaWorld w)
        {
            var dst = w.Airport(p.Arr);
            if (dst == null) return null;
            if (w.Nav != null)
            {
                // As VATFLOW: expanded route (airways, SID/STAR), trimmed to what's ahead when airborne, great-circle legs.
                var route = FcaRoute.BuildAnchors(w.Nav, p.Dep, p.Arr, p.Route, w.Airport);
                if (p.Airborne) route = FcaRoute.TrimAhead(route, p.Pos, p.Hdg);
                else if (route.Count == 0 || !route[0].Airport) route.Insert(0, new RouteAnchor("DEP", p.Pos));
                return route.Count >= 2 ? FcaRoute.PathOf(route) : null;
            }
            var dep = w.Airport(p.Dep);
            var anchors = new List<LatLon>();
            var start = dep ?? p.Pos;
            anchors.Add(start);
            var near = start;
            foreach (var raw in RouteTokens(p.Route))
            {
                var t = raw.Split('/')[0];
                if (t.Length < 2 || t.Length > 5 || !t.All(c => c >= 'A' && c <= 'Z')) continue;
                if (AirportCodesMatch(t, p.Dep) || AirportCodesMatch(t, p.Arr)) continue;
                var pos = w.Fix(t, near);
                // Skip resolutions that jump implausibly far from the previous point (wrong same-named fix).
                if (pos == null || Geo.DistanceNm(near, pos.Value) > 1500) continue;
                anchors.Add(pos.Value);
                near = pos.Value;
            }
            anchors.Add(dst.Value);
            if (!p.Airborne) return anchors;

            // Join at the leg nearest the aircraft, then fly the rest.
            int bestLeg = 0;
            double bestD = double.MaxValue;
            for (int i = 0; i < anchors.Count - 1; i++)
            {
                var n = NearestOnLine(p.Pos, new[] { anchors[i], anchors[i + 1] });
                if (n != null && n.Value.Dist < bestD) { bestD = n.Value.Dist; bestLeg = i; }
            }
            var path = new List<LatLon> { p.Pos };
            path.AddRange(anchors.Skip(bestLeg + 1));
            return path;
        }

        // ---------------- ETA ----------------

        public static int TasOrDefault(FcaPilot p)
        {
            if (p.Tas >= 60) return p.Tas;
            int cruise = p.FpAlt > 0 ? p.FpAlt : p.Alt;
            return cruise >= 28000 ? 440 : cruise >= 15000 ? 300 : 170;
        }

        /// <summary>Climb profile (still air): 250 kt / 2000 fpm below 10,000 ft, 290 kt / 1500 fpm to cruise, then TAS.</summary>
        public static double ProfileTransitSec(double distNm, double fromAlt, double cruiseAlt, double tas)
        {
            double remaining = Math.Max(0, distNm), alt = Math.Max(0, fromAlt);
            double cruise = Math.Max(alt, cruiseAlt);
            double cruiseGs = Math.Max(120, tas);
            double t = 0;
            double topA = Math.Min(10000, cruise);
            if (alt < topA)
            {
                double climb = (topA - alt) / ClimbFpmLow * 60, dA = SpdBelow10k * climb / 3600;
                if (dA >= remaining) return t + remaining / SpdBelow10k * 3600;
                t += climb; remaining -= dA; alt = topA;
            }
            if (alt < cruise)
            {
                double climb = (cruise - alt) / ClimbFpmHigh * 60, gsB = Math.Max(150, SpdClimb), dB = gsB * climb / 3600;
                if (dB >= remaining) return t + remaining / gsB * 3600;
                t += climb; remaining -= dB;
            }
            return t + remaining / cruiseGs * 3600;
        }

        private static bool IsClimbingDeparture(FcaPilot p, IFcaWorld w)
        {
            int cruise = p.FpAlt;
            var arr = w.Airport(p.Arr);
            if (arr != null && Geo.DistanceNm(p.Pos, arr.Value) < 120) return false;
            var dep = w.Airport(p.Dep);
            if (dep != null && Geo.DistanceNm(p.Pos, dep.Value) < 150 && (cruise == 0 || p.Alt < cruise - 2000)) return true;
            return p.Alt < 10000 && cruise > 12000;
        }

        private static FcaItem? AirCandidate(FcaPilot p, FcaProgram f, IFcaWorld w)
        {
            var path = RoutePath(p, w);
            if (path == null || path.Count < 2) return null;
            var c = PathCrossing(path, f.Points);
            if (c == null || c.Value.Dist > (f.LookaheadNm is double la && la > 50 ? la : LookaheadNm)) return null;
            if (HasPassed(p, f)) return null;
            double eta, spd;
            if (IsClimbingDeparture(p, w))
            {
                eta = ProfileTransitSec(c.Value.Dist, p.Alt, p.FpAlt > 0 ? p.FpAlt : p.Alt, TasOrDefault(p));
                spd = Math.Max(120, TasOrDefault(p));
            }
            else
            {
                double gs = Math.Max(100, p.Gs);
                eta = c.Value.Dist / gs * 3600;
                spd = gs;
            }
            return new FcaItem { Pilot = p, Ground = false, DistNm = c.Value.Dist, EtaSec = eta, CrossSpd = spd, TransitSec = eta };
        }

        /// <summary>"HHMM" filed departure → epoch ms, snapped to within ±12 h of now.</summary>
        public static long? DepTimeMs(string dep, long nowMs)
        {
            var digits = new string((dep ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length == 0) return null;
            digits = digits.PadLeft(4, '0').Substring(0, 4);
            int h = int.Parse(digits.Substring(0, 2), CultureInfo.InvariantCulture), m = int.Parse(digits.Substring(2, 2), CultureInfo.InvariantCulture);
            if (h > 23 || m > 59) return null;
            var now = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).UtcDateTime;
            long ms = new DateTimeOffset(now.Year, now.Month, now.Day, h, m, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
            if (ms - nowMs > 12 * 3600000L) ms -= 86400000L;
            else if (nowMs - ms > 12 * 3600000L) ms += 86400000L;
            return ms;
        }

        private static FcaItem? GroundCandidate(FcaPilot p, FcaProgram f, IFcaWorld w, long nowMs, bool ready)
        {
            var path = RoutePath(p, w);
            if (path == null || path.Count < 2) return null;
            var c = PathCrossing(path, f.Points);
            if (c == null || c.Value.Dist > (f.LookaheadNm is double la && la > 50 ? la : LookaheadNm)) return null;
            double transit = ProfileTransitSec(c.Value.Dist, 0, p.FpAlt, TasOrDefault(p));
            double off = ReadyBufferSec;
            if (!ready)
            {
                var dep = DepTimeMs(p.DepTime, nowMs);
                if (dep != null && dep.Value > nowMs + ReadyBufferSec * 1000) off = (dep.Value - nowMs) / 1000.0;
            }
            return new FcaItem
            {
                Pilot = p, Ground = true, DistNm = c.Value.Dist, EtaSec = off + transit,
                CrossSpd = Math.Max(120, TasOrDefault(p)), TransitSec = transit,
            };
        }

        private static bool AtDepartureAirport(FcaPilot p, IFcaWorld w)
        {
            var dep = w.Airport(p.Dep);
            if (dep != null) return Geo.DistanceNm(p.Pos, dep.Value) <= 10;
            var arr = w.Airport(p.Arr);
            return arr == null || Geo.DistanceNm(p.Pos, arr.Value) > 10;
        }

        /// <summary>Required time behind the previous crosser: MIT at the crossing speed, or 3600 / rate.</summary>
        public static double SepSeconds(FcaProgram f, FcaItem c)
        {
            if (f.Mode == "mit") return f.Mit > 0 ? f.Mit / Math.Max(120, c.CrossSpd > 0 ? c.CrossSpd : DefaultGroundGs) * 3600 : 0;
            return f.Rate > 0 ? 3600 / f.Rate : 0;
        }

        private static double SlotAgainst(double eta, List<double> committed, double sep)
        {
            double t = eta;
            var list = committed.OrderBy(x => x).ToList();
            for (int guard = 0; guard < 1000; guard++)
            {
                bool moved = false;
                foreach (var ct in list)
                    if (Math.Abs(t - ct) < sep - 1e-6) { t = ct + sep; moved = true; }
                if (!moved) break;
            }
            return t;
        }

        /// <summary>Order with newcomers inserted: airborne before ground, then by ETA, then distance.</summary>
        public static List<string> BuildManualOrder(IEnumerable<string> manual, Dictionary<string, FcaItem> byCs)
        {
            var order = manual.Where(byCs.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var inOrder = new HashSet<string>(order, StringComparer.OrdinalIgnoreCase);
            int Cmp(string a, string b)
            {
                var ca = byCs[a];
                var cb = byCs[b];
                if (ca.Ground != cb.Ground) return ca.Ground ? 1 : -1;
                int e = ca.EtaSec.CompareTo(cb.EtaSec);
                return e != 0 ? e : ca.DistNm.CompareTo(cb.DistNm);
            }
            var newcomers = byCs.Keys.Where(cs => !inOrder.Contains(cs)).ToList();
            newcomers.Sort(Cmp);
            foreach (var cs in newcomers)
            {
                int idx = order.FindIndex(o => Cmp(o, cs) > 0);
                if (idx < 0) idx = order.Count;
                order.Insert(idx, cs);
            }
            return order;
        }

        /// <summary>
        /// The FCA's sequence now. <paramref name="localOrder"/> (a re-sequence made in this app) overrides the
        /// program's own manual order.
        /// </summary>
        public static FcaSequence Compute(FcaProgram f, IEnumerable<FcaPilot> pilots, IFcaWorld w, long nowMs, IReadOnlyList<string>? localOrder = null)
        {
            var seq = new FcaSequence { Fca = f };
            if (f.Points.Count < 2) return seq;
            var ex = new HashSet<string>(f.Excluded, StringComparer.OrdinalIgnoreCase);
            var all = pilots.ToList();

            bool Matches(FcaPilot p) =>
                !ex.Contains(p.Callsign) && MatchesDest(f, p.Arr) && MatchesOrigin(f, p.Dep) && MatchesFix(f, p, w);

            var air = new List<FcaItem>();
            foreach (var p in all)
            {
                if (!p.Airborne || !Matches(p) || !MatchesScope(f, p.Pos, w)) continue;
                if (!MatchesAlt(f, p.Alt) && !MatchesAlt(f, p.FpAlt)) continue;
                var c = AirCandidate(p, f, w);
                if (c != null) air.Add(c);
            }
            var airCs = new HashSet<string>(air.Select(c => c.Pilot.Callsign), StringComparer.OrdinalIgnoreCase);

            // Releases of flights that left without being tracked keep their slot until well past crossing.
            var carry = new List<double>();
            var byCsPilot = all.GroupBy(p => p.Callsign, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var liveReleases = new Dictionary<string, FcaRelease>(f.Releases, StringComparer.OrdinalIgnoreCase);
            foreach (var (cs, rel) in f.Releases)
            {
                byCsPilot.TryGetValue(cs, out var pl);
                bool airborne = pl != null && pl.Airborne;
                if (airborne && airCs.Contains(cs)) { liveReleases.Remove(cs); continue; }
                if ((airborne || pl == null) && rel.CtaMs != null)
                {
                    if (nowMs > rel.CtaMs.Value + 600000) liveReleases.Remove(cs);
                    else carry.Add((rel.CtaMs.Value - nowMs) / 1000.0);
                }
            }

            var gnd = new List<FcaItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in all)
            {
                if (p.Airborne || !seen.Add(p.Callsign) || !AtDepartureAirport(p, w) || !Matches(p)) continue;
                var depPos = w.Airport(p.Dep) ?? p.Pos;
                if (!MatchesScope(f, depPos, w) || !MatchesAlt(f, p.FpAlt)) continue;
                var c = GroundCandidate(p, f, w, nowMs, liveReleases.ContainsKey(p.Callsign));
                if (c != null) gnd.Add(c);
            }

            var byCs = new Dictionary<string, FcaItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in air.Concat(gnd)) byCs[c.Pilot.Callsign] = c;

            List<FcaItem> ordered;
            var manualOrder = localOrder != null && localOrder.Count > 0 ? localOrder
                : f.ManualSeq && f.Order.Count > 0 ? f.Order : null;
            if (manualOrder != null)
            {
                seq.Manual = true;
                ordered = new List<FcaItem>();
                double prev = -1e9, prevHard = -1e9;
                foreach (var cs in BuildManualOrder(manualOrder, byCs))
                {
                    var c = byCs[cs];
                    double sep = SepSeconds(f, c);
                    if (c.Ground && liveReleases.TryGetValue(cs, out var rel) && rel.CtaMs != null)
                    {
                        double t = (rel.CtaMs.Value - nowMs) / 1000.0;
                        if (prevHard + sep - t > FreezeTolSec || prev + sep - t > FreezeTolSec || nowMs > (rel.EdctMs ?? 0) + ComplianceLateMs)
                            c.Conflict = true;
                        c.SchedSec = t;
                        c.Frozen = true;
                        prevHard = Math.Max(prevHard, t);
                    }
                    else
                    {
                        c.SchedSec = Math.Max(c.EtaSec, prev + sep);
                        if (!c.Ground) prevHard = Math.Max(prevHard, c.SchedSec);
                    }
                    prev = Math.Max(prev, c.SchedSec);
                    ordered.Add(c);
                }
            }
            else
            {
                // Airborne: fixed at ETA, conflicts flagged.
                air.Sort((a, b) => a.EtaSec.CompareTo(b.EtaSec));
                for (int i = 0; i < air.Count; i++)
                {
                    var c = air[i];
                    c.SchedSec = c.EtaSec;
                    c.Frozen = true;
                    if (i > 0)
                    {
                        double gap = c.EtaSec - air[i - 1].EtaSec, need = SepSeconds(f, c);
                        if (need > 0 && gap < need - 1) { c.Conflict = true; c.ConflictShortSec = need - gap; }
                    }
                }
                var committed = air.Select(c => c.SchedSec).Concat(carry).ToList();
                // Ready ground: frozen releases.
                var ready = gnd.Where(c => liveReleases.TryGetValue(c.Pilot.Callsign, out var r) && r.CtaMs != null).ToList();
                foreach (var c in ready)
                {
                    var rel = liveReleases[c.Pilot.Callsign];
                    c.SchedSec = (rel.CtaMs!.Value - nowMs) / 1000.0;
                    c.Frozen = true;
                    if (nowMs > (rel.EdctMs ?? 0) + ComplianceLateMs) c.Conflict = true;
                    else if (air.Any(a => Math.Abs(c.SchedSec - a.SchedSec) < SepSeconds(f, c) - FreezeTolSec)) c.Conflict = true;
                }
                committed.AddRange(ready.Select(c => c.SchedSec));
                // Advisory ground: slotted around everything committed.
                foreach (var c in gnd.Except(ready).OrderBy(c => c.EtaSec))
                {
                    c.SchedSec = SlotAgainst(c.EtaSec, committed, SepSeconds(f, c));
                    committed.Add(c.SchedSec);
                }
                ordered = air.Concat(gnd).OrderBy(c => c.SchedSec).ToList();
            }
            seq.Items.AddRange(ordered);
            return seq;
        }

        /// <summary>MRP delay to absorb: the scheduled delay, or for an airborne conflict the seconds it is short.</summary>
        public static double DelayToAbsorb(FcaItem i) => i.DelaySec > 0.5 ? i.DelaySec : i.Conflict ? i.ConflictShortSec : 0;

        /// <summary>ERAM DCT: rounded minutes ("6", "-1"), or rounded 10 s of seconds as m:ss ("2:40").</summary>
        public static string FormatDct(double sec, bool secFormat)
        {
            if (secFormat)
            {
                long tens = (long)Math.Round(sec / 10.0);
                long total = Math.Abs(tens) * 10;
                return (tens < 0 ? "-" : "") + (total / 60).ToString(CultureInfo.InvariantCulture) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
            }
            return ((long)Math.Round(sec / 60.0)).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The order after an MRP re-sequence: <paramref name="picked"/> (in the order clicked) take consecutive
        /// places starting where the earliest of them was; everyone else keeps their order.
        /// </summary>
        public static List<string> Resequence(IReadOnlyList<string> current, IReadOnlyList<string> picked)
        {
            var set = new HashSet<string>(picked, StringComparer.OrdinalIgnoreCase);
            int first = current.Select((cs, i) => (cs, i)).Where(x => set.Contains(x.cs)).Select(x => x.i).DefaultIfEmpty(-1).Min();
            var rest = current.Where(cs => !set.Contains(cs)).ToList();
            if (first < 0) return current.ToList();
            int insertAt = current.Take(first).Count(cs => !set.Contains(cs));
            rest.InsertRange(insertAt, picked.Where(current.Contains));
            return rest;
        }
    }
}
