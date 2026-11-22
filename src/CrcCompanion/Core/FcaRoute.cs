using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrcCompanion.Core
{
    /// <summary>A SID or STAR from VATFLOW's nav data: common legs and named transitions.</summary>
    public sealed class NavProcedure
    {
        public bool Star { get; init; }
        public List<(string Name, LatLon Pos)> Common { get; init; } = new List<(string, LatLon)>();
        public Dictionary<string, List<(string Name, LatLon Pos)>> Transitions { get; init; } = new Dictionary<string, List<(string, LatLon)>>();
    }

    /// <summary>
    /// VATFLOW's US route data (vatflow.io/data/nav: FAA NASR fixes and navaids, CIFP airways and procedures,
    /// preferred routes), so FCA routes expand exactly as on VATFLOW.
    /// </summary>
    public sealed class VatflowNav
    {
        public Dictionary<string, List<LatLon>> Fixes { get; } = new Dictionary<string, List<LatLon>>();
        public Dictionary<string, List<LatLon>> Navaids { get; } = new Dictionary<string, List<LatLon>>();
        public Dictionary<string, List<(string Name, LatLon Pos)>> Airways { get; } = new Dictionary<string, List<(string, LatLon)>>();
        public Dictionary<string, NavProcedure> Procedures { get; } = new Dictionary<string, NavProcedure>();
        public Dictionary<string, string> Preferred { get; } = new Dictionary<string, string>();
        /// <summary>Coverage box: min lat, min lon, max lat, max lon.</summary>
        public double[] Bbox { get; private set; } = { 23.5, -130, 51.5, -63 };

        public static VatflowNav Parse(string meta, string fixes, string navaids, string airways, string procedures, string preferred)
        {
            var n = new VatflowNav();
            using (var d = JsonDocument.Parse(meta))
                if (d.RootElement.TryGetProperty("bbox", out var b) && b.ValueKind == JsonValueKind.Array && b.GetArrayLength() == 4)
                    n.Bbox = b.EnumerateArray().Select(x => x.GetDouble()).ToArray();
            ReadPoints(fixes, n.Fixes);
            ReadPoints(navaids, n.Navaids);
            using (var d = JsonDocument.Parse(airways))
                foreach (var a in d.RootElement.EnumerateObject())
                    if (a.Value.TryGetProperty("w", out var w)) n.Airways[a.Name] = Legs(w);
            using (var d = JsonDocument.Parse(procedures))
                foreach (var pr in d.RootElement.EnumerateObject())
                {
                    var v = pr.Value;
                    var proc = new NavProcedure
                    {
                        Star = v.TryGetProperty("type", out var t) && t.GetString() == "STAR",
                        Common = v.TryGetProperty("common", out var c) ? Legs(c) : new List<(string, LatLon)>(),
                    };
                    if (v.TryGetProperty("transitions", out var tr) && tr.ValueKind == JsonValueKind.Object)
                        foreach (var x in tr.EnumerateObject()) proc.Transitions[x.Name] = Legs(x.Value);
                    n.Procedures[pr.Name] = proc;
                }
            using (var d = JsonDocument.Parse(preferred))
                foreach (var p in d.RootElement.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) n.Preferred[p.Name] = p.Value.GetString() ?? "";
            return n;
        }

        private static void ReadPoints(string json, Dictionary<string, List<LatLon>> into)
        {
            using var d = JsonDocument.Parse(json);
            foreach (var f in d.RootElement.EnumerateObject())
            {
                var l = new List<LatLon>();
                foreach (var p in f.Value.EnumerateArray())
                    if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 2) l.Add(new LatLon(p[0].GetDouble(), p[1].GetDouble()));
                if (l.Count > 0) into[f.Name] = l;
            }
        }

        private static List<(string, LatLon)> Legs(JsonElement arr)
        {
            var l = new List<(string, LatLon)>();
            if (arr.ValueKind != JsonValueKind.Array) return l;
            foreach (var leg in arr.EnumerateArray())
                if (leg.ValueKind == JsonValueKind.Array && leg.GetArrayLength() >= 3)
                    l.Add((leg[0].GetString() ?? "", new LatLon(leg[1].GetDouble(), leg[2].GetDouble())));
            return l;
        }

        /// <summary>Expanded-route fix names by "dep|arr|route" (for FCA fix filters).</summary>
        internal readonly Dictionary<string, HashSet<string>> RouteNameCache = new Dictionary<string, HashSet<string>>();

        public bool InCoverage(LatLon p) => p.Lat >= Bbox[0] && p.Lat <= Bbox[2] && p.Lon >= Bbox[1] && p.Lon <= Bbox[3];
    }

    /// <summary>One point of an expanded route.</summary>
    public readonly struct RouteAnchor
    {
        public readonly string Name;
        public readonly LatLon Pos;
        public readonly bool Airport;
        public RouteAnchor(string name, LatLon pos, bool airport = false) { Name = name; Pos = pos; Airport = airport; }
    }

    /// <summary>Port of vatflow/shared/route-engine.js buildRouteAnchors / trimAnchorsAhead.</summary>
    public static class FcaRoute
    {
        private static readonly Regex AirwayRe = new Regex(@"^(?:[ABGJLMNPQRTVWY]|U[LMNPQT])\d{1,4}[A-Z]?$");
        private static readonly Regex ProcRe = new Regex(@"^([A-Z]{3,6})\d[A-Z]?$");

        private static string Clean(string t) => (t ?? "").Split('/')[0].ToUpperInvariant();

        private static LatLon Nearest(List<LatLon> c, LatLon? near)
        {
            if (near == null || c.Count == 1) return c[0];
            return c.OrderBy(x => Geo.DistanceNm(near.Value, x)).First();
        }

        public static NavProcedure? FindProcedure(VatflowNav nav, string id)
        {
            var key = Clean(id);
            if (key.Length == 0) return null;
            if (nav.Procedures.TryGetValue(key, out var p)) return p;
            var m = ProcRe.Match(key);
            if (!m.Success) return null;
            var pick = nav.Procedures.Keys.Where(k => k.StartsWith(m.Groups[1].Value, StringComparison.Ordinal))
                .OrderBy(k => k.Length).ThenBy(k => k, StringComparer.Ordinal).FirstOrDefault();
            return pick == null ? null : nav.Procedures[pick];
        }

        private static (string Name, LatLon Pos)? Resolve(VatflowNav nav, string token, LatLon? near, string dep, string arr, Func<string, LatLon?> airport)
        {
            var id = Clean(token);
            if (id.Length < 2 || id == dep || id == arr) return null;
            var apt = airport(id);
            if (apt != null) return (id, apt.Value);
            if (nav.Navaids.TryGetValue(id, out var nv)) return (id, Nearest(nv, near));
            if (nav.Fixes.TryGetValue(id, out var fx)) return (id, Nearest(fx, near));
            var proc = nav.Procedures.TryGetValue(id, out var p0) ? p0 : ProcRe.IsMatch(id) ? FindProcedure(nav, id) : null;
            if (proc != null)
            {
                var legs = proc.Common.Count >= 2 ? proc.Common : proc.Transitions.Values.FirstOrDefault();
                if (legs != null && legs.Count > 0) return (id, legs[0].Pos);
            }
            return null;
        }

        private static List<(string, LatLon)> Expand(NavProcedure proc, string? transition)
        {
            List<(string Name, LatLon Pos)> legs;
            if (transition != null && proc.Transitions.TryGetValue(transition, out var tr))
            {
                legs = tr.ToList();
                var common = proc.Common;
                if (common.Count > 0)
                {
                    int start = legs.Count > 0 && common[0].Name == legs[^1].Name ? 1 : 0;
                    legs.AddRange(common.Skip(start));
                }
            }
            else if (proc.Common.Count >= 2) legs = proc.Common;
            else return new List<(string, LatLon)>();
            return legs;
        }

        private static List<(string, LatLon)> ExpandAirway(VatflowNav nav, string id, LatLon? from, LatLon? to)
        {
            if (!nav.Airways.TryGetValue(Clean(id), out var w) || w.Count < 2) return new List<(string, LatLon)>();
            if (from == null && to == null) return w.ToList();
            var f = from ?? w[0].Pos;
            var t = to ?? w[^1].Pos;
            int Near(LatLon ll) { int best = 0; double bd = 1e9; for (int i = 0; i < w.Count; i++) { var d = Geo.DistanceNm(ll, w[i].Pos); if (d < bd) { bd = d; best = i; } } return best; }
            int i0 = Near(f), i1 = Near(t);
            if (i0 == i1) return new List<(string, LatLon)> { w[i0] };
            return i0 < i1 ? w.Skip(i0).Take(i1 - i0 + 1).ToList() : w.Skip(i1).Take(i0 - i1 + 1).Reverse().ToList();
        }

        /// <summary>International when the destination isn't a US/territory airport (VATFLOW's oceanic cut-off).</summary>
        public static bool IsInternational(string arr, LatLon? dest, VatflowNav nav)
        {
            var code = (arr ?? "").ToUpperInvariant();
            if (code.Length >= 3)
            {
                if (code[0] == 'K') return false;
                if (Regex.IsMatch(code, "^P[AHTGJF]") || Regex.IsMatch(code, "^T[JIP]") || Regex.IsMatch(code, "^MD[A-Z0-9]")) return false;
                return true;
            }
            return dest != null && !nav.InCoverage(dest.Value);
        }

        private static string PreferredOr(VatflowNav nav, string dep, string arr, string route, Func<string, LatLon?> airport)
        {
            dep = dep.ToUpperInvariant(); arr = arr.ToUpperInvariant();
            if (dep.Length == 0 || arr.Length == 0) return route;
            if (!nav.Preferred.TryGetValue(dep + "|" + arr, out var pr)) return route;
            var toks = FcaEngine.RouteTokens(route);
            return toks.Count <= 2 && toks.All(t => airport(Clean(t)) != null || Clean(t) == "DCT") ? pr : route;
        }

        /// <summary>Departure, the expanded filed route, destination. Airport positions come from <paramref name="airport"/>.</summary>
        public static List<RouteAnchor> BuildAnchors(VatflowNav nav, string dep, string arr, string route, Func<string, LatLon?> airport)
        {
            dep = (dep ?? "").ToUpperInvariant();
            arr = (arr ?? "").ToUpperInvariant();
            var origin = dep.Length > 0 ? airport(dep) : null;
            var dest = arr.Length > 0 ? airport(arr) : null;
            var tokens = FcaEngine.RouteTokens(PreferredOr(nav, dep, arr, route ?? "", airport));
            bool intl = IsInternational(arr, dest, nav);
            var anchors = new List<RouteAnchor>();
            LatLon? refLL = origin;
            if (origin != null) anchors.Add(new RouteAnchor(dep, origin.Value, true));

            void Push(string name, LatLon ll)
            {
                if (anchors.Count == 0 || anchors[^1].Pos.Lat != ll.Lat || anchors[^1].Pos.Lon != ll.Lon) anchors.Add(new RouteAnchor(name, ll));
                refLL = anchors[^1].Pos;
            }

            for (int i = 0; i < tokens.Count; i++)
            {
                var tok = Clean(tokens[i]);
                var next = i + 1 < tokens.Count ? Clean(tokens[i + 1]) : "";
                var nextProc = next.Length > 0 ? FindProcedure(nav, next) : null;
                if (nextProc != null && nextProc.Transitions.ContainsKey(tok)) continue;   // consumed by the procedure

                if (AirwayRe.IsMatch(tok))
                {
                    LatLon? to = null;
                    for (int j = i + 1; j < tokens.Count; j++)
                    {
                        var nx = Resolve(nav, tokens[j], refLL, dep, arr, airport);
                        if (nx != null) { to = nx.Value.Pos; break; }
                        if (AirwayRe.IsMatch(Clean(tokens[j]))) break;
                    }
                    if (refLL == null) { if (intl) break; continue; }
                    var ex = ExpandAirway(nav, tok, refLL, to);
                    if (ex.Count == 0) { if (intl) break; continue; }
                    bool cut = false;
                    foreach (var (n, ll) in ex)
                    {
                        if (intl && !nav.InCoverage(ll)) { cut = true; break; }
                        Push(n, ll);
                    }
                    if (cut) break;
                    continue;
                }

                bool hasDigit = tok.Any(char.IsDigit);
                bool atEdge = i <= 1 || i >= tokens.Count - 2;
                bool atArrivalEdge = i >= tokens.Count - 2;
                bool known = nav.Navaids.ContainsKey(tok) || nav.Fixes.ContainsKey(tok) || airport(tok) != null;
                var proc = hasDigit || (atEdge && !known) ? FindProcedure(nav, tok) : null;
                if (proc != null)
                {
                    if (intl && atArrivalEdge && proc.Star) break;
                    var prev = i > 0 ? Clean(tokens[i - 1]) : "";
                    var legs = Expand(proc, proc.Transitions.ContainsKey(prev) ? prev : null);
                    bool cut = false;
                    foreach (var (n, ll) in legs)
                    {
                        if (intl && !nav.InCoverage(ll)) { cut = true; break; }
                        Push(n, ll);
                    }
                    if (cut) break;
                    continue;
                }

                var r = Resolve(nav, tok, refLL, dep, arr, airport);
                if (r == null) { if (intl) break; continue; }
                if (intl && !nav.InCoverage(r.Value.Pos)) break;
                if (refLL != null && Geo.DistanceNm(refLL.Value, r.Value.Pos) > 900) { if (intl) break; continue; }
                Push(r.Value.Name, r.Value.Pos);
            }

            if (dest != null && (anchors.Count == 0 || anchors[^1].Pos.Lat != dest.Value.Lat || anchors[^1].Pos.Lon != dest.Value.Lon))
                anchors.Add(new RouteAnchor(arr, dest.Value, true));
            return anchors;
        }

        private static double AngleDiff(double a, double b) => Math.Abs(((a - b) + 540) % 360 - 180);

        /// <summary>Index of the first anchor ahead of the aircraft (closest leg, then heading checks).</summary>
        public static int ProgressIndex(IReadOnlyList<RouteAnchor> a, LatLon pos, double? hdg)
        {
            if (a.Count < 2) return 0;
            int bestLeg = 0;
            double bestXt = double.MaxValue, bestAlong = 0;
            for (int i = 0; i < a.Count - 1; i++)
            {
                var A = Local(a[i].Pos, pos);
                var B = Local(a[i + 1].Pos, pos);
                double abx = B.X - A.X, aby = B.Y - A.Y, len2 = abx * abx + aby * aby;
                double t = len2 > 0 ? (-A.X * abx + -A.Y * aby) / len2 : 0;
                t = Math.Clamp(t, 0, 1);
                double xt = Math.Sqrt(Math.Pow(A.X + t * abx, 2) + Math.Pow(A.Y + t * aby, 2));
                double along = Math.Sqrt(Math.Pow(t * abx, 2) + Math.Pow(t * aby, 2));
                if (xt < bestXt) { bestXt = xt; bestLeg = i; bestAlong = along; }
            }
            double legLen = Geo.DistanceNm(a[bestLeg].Pos, a[bestLeg + 1].Pos);
            int idx = bestAlong >= legLen - 3 ? bestLeg + 1 : bestLeg;
            if (hdg != null)
            {
                while (idx < a.Count - 1)
                {
                    double d = Geo.DistanceNm(pos, a[idx].Pos);
                    if (d > 5 && AngleDiff(hdg.Value, Geo.BearingDeg(pos, a[idx].Pos)) > 110) idx++;
                    else break;
                }
                if (idx < a.Count - 1 && Geo.DistanceNm(pos, a[idx].Pos) < 12 &&
                    AngleDiff(hdg.Value, Geo.BearingDeg(pos, a[idx + 1].Pos)) < 95) idx++;
            }
            return Math.Min(idx, a.Count - 1);
        }

        private static (double X, double Y) Local(LatLon p, LatLon o) =>
            ((p.Lon - o.Lon) * 60 * Math.Cos(o.Lat * Math.PI / 180), (p.Lat - o.Lat) * 60);

        /// <summary>The route still ahead of an airborne aircraft, starting at its present position.</summary>
        public static List<RouteAnchor> TrimAhead(List<RouteAnchor> anchors, LatLon pos, double hdg)
        {
            if (anchors.Count == 0) return anchors;
            int idx = ProgressIndex(anchors, pos, hdg);
            if (anchors[0].Airport && idx == 0 && anchors.Count > 1 && Geo.DistanceNm(pos, anchors[0].Pos) > 25) idx = 1;
            var ahead = anchors.Skip(idx).ToList();
            if (ahead.Count > 0 && Geo.DistanceNm(pos, ahead[0].Pos) < 0.5) ahead.RemoveAt(0);
            ahead.Insert(0, new RouteAnchor("NOW", pos));
            return ahead;
        }

        /// <summary>Great-circle points between two anchors (VATFLOW gcLine: one per ~120 NM, 2-48 segments).</summary>
        public static List<LatLon> GreatCircle(LatLon a, LatLon b)
        {
            double r = Math.PI / 180;
            double lat1 = a.Lat * r, lon1 = a.Lon * r, lat2 = b.Lat * r, lon2 = b.Lon * r;
            double d = 2 * Math.Asin(Math.Sqrt(Math.Pow(Math.Sin((lat2 - lat1) / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin((lon2 - lon1) / 2), 2)));
            if (double.IsNaN(d) || d == 0) return new List<LatLon> { a, b };
            int n = Math.Max(2, Math.Min(48, (int)Math.Round(Geo.DistanceNm(a, b) / 120)));
            var outp = new List<LatLon>();
            for (int i = 0; i <= n; i++)
            {
                double f = (double)i / n, s = Math.Sin(d), A = Math.Sin((1 - f) * d) / s, B = Math.Sin(f * d) / s;
                double x = A * Math.Cos(lat1) * Math.Cos(lon1) + B * Math.Cos(lat2) * Math.Cos(lon2);
                double y = A * Math.Cos(lat1) * Math.Sin(lon1) + B * Math.Cos(lat2) * Math.Sin(lon2);
                double z = A * Math.Sin(lat1) + B * Math.Sin(lat2);
                outp.Add(new LatLon(Math.Atan2(z, Math.Sqrt(x * x + y * y)) / r, Math.Atan2(y, x) / r));
            }
            return outp;
        }

        /// <summary>The anchors as a densified path (great-circle legs).</summary>
        public static List<LatLon> PathOf(IReadOnlyList<RouteAnchor> anchors)
        {
            var path = new List<LatLon>();
            for (int i = 0; i < anchors.Count - 1; i++)
            {
                var seg = GreatCircle(anchors[i].Pos, anchors[i + 1].Pos);
                path.AddRange(i == 0 ? seg : seg.Skip(1));
            }
            if (anchors.Count == 1) path.Add(anchors[0].Pos);
            return path;
        }
    }
}
