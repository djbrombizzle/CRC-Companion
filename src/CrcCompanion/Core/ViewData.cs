using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CrcCompanion.Core
{
    // ---------------- Conflict alert ----------------

    public sealed class ConflictPair
    {
        public string A { get; init; } = "";
        public string B { get; init; } = "";
        public double NowNm { get; init; }
        public int NowVerticalFt { get; init; }
        /// <summary>0 = in conflict now; otherwise minutes until first loss of separation.</summary>
        public double Minutes { get; init; }
        public int AltA { get; init; }
        public int AltB { get; init; }

        /// <summary>"AAL1     DAL2     3.2 08 NOW" / "... E2" (lateral NM, vertical hundreds, time).</summary>
        public string Text =>
            A.PadRight(StripFormat.CallsignChars) + " " + B.PadRight(StripFormat.CallsignChars) + " " +
            NowNm.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5) + " " +
            Math.Clamp(NowVerticalFt / 100, 0, 999).ToString("00", CultureInfo.InvariantCulture).PadLeft(3) + " " +
            (Minutes <= 0 ? "NOW" : "E" + Math.Max(1, (int)Math.Ceiling(Minutes)).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Conflict probe like ERAM's: loss of separation now or within the look-ahead (default 4 minutes),
    /// projecting each aircraft straight ahead at its groundspeed and current vertical rate.
    /// Lateral minimum is configurable (5 NM en route, 3 NM terminal); vertical is 1,000 ft,
    /// 2,000 ft when either aircraft is above FL410. Pairs that are both close to the same
    /// airport (parallel approaches, pattern work) are skipped.
    /// </summary>
    public static class Conflicts
    {
        public const double AirportExclusionNm = 5;

        public static List<ConflictPair> Find(FeedSnapshot s, FacilityContext ctx, Func<string, double?> vsFpm,
            double lateralNm = 5, double lookaheadMin = 4, double stepSec = 10)
        {
            var airborne = s.Pilots.Where(p => p.Groundspeed >= Traffic.AirborneKt).ToList();
            var inside = new HashSet<Pilot>(airborne.Where(p => ctx.Boundary.Contains(p.Pos)));
            var result = new List<ConflictPair>();
            var airports = ctx.Airports.Values.ToList();

            for (int i = 0; i < airborne.Count; i++)
            {
                var a = airborne[i];
                for (int j = i + 1; j < airborne.Count; j++)
                {
                    var b = airborne[j];
                    if (!inside.Contains(a) && !inside.Contains(b)) continue;
                    // Quick reject: can't close the gap within the look-ahead.
                    double reach = (a.Groundspeed + b.Groundspeed) * lookaheadMin / 60.0 + lateralNm;
                    if (Math.Abs(a.Pos.Lat - b.Pos.Lat) * 60 > reach) continue;
                    double now = Geo.DistanceNm(a.Pos, b.Pos);
                    if (now > reach) continue;
                    if (NearSameAirport(a.Pos, b.Pos, airports)) continue;

                    double va = vsFpm(a.Callsign) ?? 0, vb = vsFpm(b.Callsign) ?? 0;
                    double? hit = null;
                    for (double t = 0; t <= lookaheadMin * 60 + 0.001; t += stepSec)
                    {
                        var pa = t == 0 ? a.Pos : Geo.Move(a.Pos, a.Heading, a.Groundspeed * t / 3600.0);
                        var pb = t == 0 ? b.Pos : Geo.Move(b.Pos, b.Heading, b.Groundspeed * t / 3600.0);
                        double altA = a.Altitude + va * t / 60.0, altB = b.Altitude + vb * t / 60.0;
                        double vmin = Math.Max(altA, altB) > 41000 ? 2000 : 1000;
                        if (Math.Abs(altA - altB) < vmin && Geo.DistanceNm(pa, pb) < lateralNm) { hit = t / 60.0; break; }
                    }
                    if (hit == null) continue;
                    bool aFirst = string.CompareOrdinal(a.Callsign, b.Callsign) <= 0;
                    result.Add(new ConflictPair
                    {
                        A = aFirst ? a.Callsign : b.Callsign,
                        B = aFirst ? b.Callsign : a.Callsign,
                        AltA = aFirst ? a.Altitude : b.Altitude,
                        AltB = aFirst ? b.Altitude : a.Altitude,
                        NowNm = now,
                        NowVerticalFt = Math.Abs(a.Altitude - b.Altitude),
                        Minutes = hit.Value,
                    });
                }
            }
            result.Sort((x, y) =>
            {
                int c = x.Minutes.CompareTo(y.Minutes);
                return c != 0 ? c : x.NowNm.CompareTo(y.NowNm);
            });
            return result;
        }

        private static bool NearSameAirport(LatLon a, LatLon b, List<MapAirport> airports)
        {
            foreach (var ap in airports)
            {
                if (Math.Abs(ap.Pos.Lat - a.Lat) > 0.1) continue;
                if (Geo.DistanceNm(ap.Pos, a) <= AirportExclusionNm && Geo.DistanceNm(ap.Pos, b) <= AirportExclusionNm) return true;
            }
            return false;
        }
    }

    // ---------------- Beacon codes (CODE, GROUP SUP) ----------------

    public static class BeaconCodes
    {
        /// <summary>Four octal digits.</summary>
        public static bool IsCode(string s) => s.Length == 4 && s.All(c => c >= '0' && c <= '7');

        /// <summary>
        /// GROUP SUP entry: a code ("1200"), a two-digit block ("12" = 1200-1277) or an octal range ("4000-4077").
        /// </summary>
        public static bool IsGroup(string s)
        {
            if (IsCode(s)) return true;
            if (s.Length == 2 && s.All(c => c >= '0' && c <= '7')) return true;
            var parts = s.Split('-');
            return parts.Length == 2 && IsCode(parts[0]) && IsCode(parts[1]) &&
                   Convert.ToInt32(parts[0], 8) <= Convert.ToInt32(parts[1], 8);
        }

        public static bool Matches(string group, string code)
        {
            code = (code ?? "").Trim();
            if (!IsCode(code)) return false;
            if (IsCode(group)) return group == code;
            if (group.Length == 2) return code.StartsWith(group, StringComparison.Ordinal);
            var parts = group.Split('-');
            if (parts.Length != 2) return false;
            int c = Convert.ToInt32(code, 8);
            return c >= Convert.ToInt32(parts[0], 8) && c <= Convert.ToInt32(parts[1], 8);
        }

        public static bool Suppressed(IEnumerable<string> groups, string code) => groups.Any(g => Matches(g, code));

        /// <summary>
        /// Beacon Code View entries: codes of airborne targets in the facility, plus manual (QB) codes
        /// shown with a period. Numeric order, or manual codes first with SORT MAN.
        /// </summary>
        public static List<string> CodeViewEntries(IEnumerable<string> trackedCodes, IReadOnlyList<string> manual, bool sortManual)
        {
            var man = new HashSet<string>(manual);
            var tracked = new HashSet<string>(trackedCodes.Where(IsCode));
            var all = tracked.Union(man).ToList();
            all.Sort((a, b) =>
            {
                if (sortManual)
                {
                    int m = man.Contains(b).CompareTo(man.Contains(a));
                    if (m != 0) return m;
                }
                return string.CompareOrdinal(a, b);
            });
            return all.Select(c => man.Contains(c) ? c + "." : c).ToList();
        }
    }

    // ---------------- Typed entries shared by several views ----------------

    public static class EntryList
    {
        /// <summary>
        /// "A B -C" adds A and B and removes C. Tokens pass through <paramref name="normalize"/>
        /// (null = invalid). Returns feedback in the same style as station entry.
        /// </summary>
        public static string Apply(List<string> list, string entry, Func<string, string?> normalize, int max = 100)
        {
            var added = new List<string>(); var removed = new List<string>();
            var dup = new List<string>(); var missing = new List<string>(); var bad = new List<string>();
            foreach (var raw in (entry ?? "").ToUpperInvariant().Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool remove = raw.Length > 1 && raw[0] == '-';
                var tok = remove ? raw.Substring(1) : raw;
                var n = normalize(tok);
                if (n == null) { bad.Add(raw); continue; }
                if (remove)
                {
                    if (list.Remove(n)) removed.Add(n); else missing.Add(n);
                }
                else if (list.Contains(n)) dup.Add(n);
                else if (list.Count >= max) bad.Add(raw);
                else { list.Add(n); added.Add(n); }
            }
            var parts = new List<string>();
            if (added.Count > 0) parts.Add("ADD " + string.Join(" ", added));
            if (removed.Count > 0) parts.Add("DEL " + string.Join(" ", removed));
            if (dup.Count > 0) parts.Add("DUP " + string.Join(" ", dup));
            if (missing.Count > 0) parts.Add("NOT IN LIST " + string.Join(" ", missing));
            if (bad.Count > 0) parts.Add("INVALID " + string.Join(" ", bad));
            return string.Join("  ", parts);
        }

        /// <summary>Callsign: 2-10 letters/digits, starting with a letter.</summary>
        public static string? Callsign(string s)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            if (s.Length < 2 || s.Length > 10 || !char.IsLetter(s[0]) || !s.All(char.IsLetterOrDigit)) return null;
            return s;
        }

        /// <summary>Free-form name such as an SAA ("R4001A", "W386", "MOA-EVERS"): 2-12 characters.</summary>
        public static string? Name(string s)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            if (s.Length < 2 || s.Length > 12 || !s.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')) return null;
            return s;
        }
    }

    // ---------------- CRR ----------------

    public sealed class CrrGroup
    {
        public string Label { get; set; } = "";
        public string Location { get; set; } = "";
        public double Lat { get; set; }
        public double Lon { get; set; }
        public List<string> Aircraft { get; set; } = new List<string>();
        public LatLon Pos => new LatLon(Lat, Lon);
    }

    /// <summary>
    /// Continuous Range Readout groups, entered like CRC's LF command (without "LF"):
    ///   LOCATION [LABEL] [ACID ...]   create a group (label optional for a plain fix)
    ///   LABEL ACID [ACID ...]         add or remove aircraft (toggle)
    ///   -LABEL                        delete the group
    /// Aircraft may also be separated by slashes ("AAL1/DAL2").
    /// </summary>
    public static class CrrCommands
    {
        public const int MaxGroups = 20;

        public static bool IsLabel(string s) => s.Length >= 1 && s.Length <= 5 && s.All(char.IsLetterOrDigit);

        public static string Apply(List<CrrGroup> groups, string text, Func<string, (LatLon Pos, Locations.Kind Kind, string Name)?> resolve,
            Func<string, bool> isOnline)
        {
            var toks = (text ?? "").ToUpperInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (toks.Count == 0) return "";

            if (toks.Count == 1 && toks[0].StartsWith("-", StringComparison.Ordinal))
            {
                var lbl = toks[0].Substring(1);
                return groups.RemoveAll(g => g.Label == lbl) > 0 ? "DEL GROUP " + lbl : "NO GROUP " + lbl;
            }

            var existing = groups.FirstOrDefault(g => g.Label == toks[0]);
            if (existing != null)
            {
                if (toks.Count == 1) return "GROUP EXISTS " + existing.Label;
                return Toggle(existing, toks.Skip(1), isOnline);
            }

            var loc = resolve(toks[0]);
            if (loc == null) return "LOCATION NOT FOUND " + toks[0];
            string label;
            int next = 1;
            if (toks.Count > 1 && IsLabel(toks[1]) && !isOnline(toks[1]) && !toks[1].Contains('/'))
            {
                label = toks[1];
                next = 2;
            }
            else if (loc.Value.Kind == Locations.Kind.Fix && IsLabel(loc.Value.Name)) label = loc.Value.Name;
            else return "LABEL REQUIRED";

            if (groups.Any(g => g.Label == label)) return "GROUP EXISTS " + label;
            if (groups.Count >= MaxGroups) return "TOO MANY GROUPS";
            var g = new CrrGroup { Label = label, Location = toks[0].TrimStart('/'), Lat = loc.Value.Pos.Lat, Lon = loc.Value.Pos.Lon };
            groups.Add(g);
            var msg = "ADD GROUP " + label;
            if (toks.Count > next) msg += "  " + Toggle(g, toks.Skip(next), isOnline);
            return msg;
        }

        private static string Toggle(CrrGroup g, IEnumerable<string> tokens, Func<string, bool> isOnline)
        {
            var add = new List<string>(); var del = new List<string>(); var nf = new List<string>();
            foreach (var t in tokens.SelectMany(t => t.Split('/', StringSplitOptions.RemoveEmptyEntries)))
            {
                var cs = EntryList.Callsign(t);
                if (cs == null) { nf.Add(t); continue; }
                if (g.Aircraft.Remove(cs)) del.Add(cs);
                else if (isOnline(cs)) { g.Aircraft.Add(cs); add.Add(cs); }
                else nf.Add(cs);
            }
            var parts = new List<string>();
            if (add.Count > 0) parts.Add("ADD " + string.Join(" ", add));
            if (del.Count > 0) parts.Add("DEL " + string.Join(" ", del));
            if (nf.Count > 0) parts.Add("NOT FOUND " + string.Join(" ", nf));
            return string.Join("  ", parts);
        }
    }

    // ---------------- MRP (meter reference point) ----------------

    public sealed class NamedPoint
    {
        public string Name { get; set; } = "";
        public double Lat { get; set; }
        public double Lon { get; set; }
        /// <summary>Airport: flights destined there. Fix: flights whose route names it.</summary>
        public bool IsAirport { get; set; }
        public LatLon Pos => new LatLon(Lat, Lon);
    }

    public sealed class MeterItem
    {
        public string Callsign { get; init; } = "";
        public string Type { get; init; } = "";
        public double DistanceNm { get; init; }
        public DateTime EtaUtc { get; init; }
        public int AltitudeHundreds { get; init; }
    }

    public static class Metering
    {
        /// <summary>
        /// Flights heading for a meter point: airborne, within rangeNm, track within 90° of the point,
        /// and either destined for it (airport) or with it in the route (fix). ETA = distance / groundspeed.
        /// </summary>
        public static List<MeterItem> For(NamedPoint mp, FeedSnapshot s, double rangeNm, Func<string, bool> sameAirport)
        {
            var list = new List<MeterItem>();
            foreach (var p in s.Pilots)
            {
                if (p.Groundspeed < Traffic.AirborneKt || p.Plan == null) continue;
                bool match = mp.IsAirport ? sameAirport(p.Plan.Arrival)
                    : (" " + p.Plan.Route + " ").Contains(" " + mp.Name + " ") || p.Plan.Route.Contains("." + mp.Name, StringComparison.Ordinal)
                      || p.Plan.Route.Contains(mp.Name + ".", StringComparison.Ordinal);
                if (!match) continue;
                double d = Geo.DistanceNm(p.Pos, mp.Pos);
                if (d > rangeNm) continue;
                if (d > 3 && Math.Abs(FlightTracker.AngleDiff(p.Heading, Geo.BearingDeg(p.Pos, mp.Pos))) > 90) continue;
                list.Add(new MeterItem
                {
                    Callsign = p.Callsign,
                    Type = p.Plan.Type,
                    DistanceNm = d,
                    EtaUtc = s.UpdatedUtc.AddMinutes(d / p.Groundspeed * 60),
                    AltitudeHundreds = (int)Math.Round(p.Altitude / 100.0),
                });
            }
            list.Sort((a, b) => a.EtaUtc.CompareTo(b.EtaUtc));
            return list;
        }
    }

    // ---------------- UA (unassociated targets) ----------------

    public static class Unassociated
    {
        /// <summary>Airborne targets inside the facility with no flight plan, sorted by callsign.</summary>
        public static List<Pilot> Find(FeedSnapshot s, FacilityContext ctx) =>
            s.Pilots.Where(p => p.Plan == null && p.Groundspeed >= Traffic.AirborneKt && ctx.Boundary.Contains(p.Pos))
                    .OrderBy(p => p.Callsign, StringComparer.Ordinal).ToList();

        /// <summary>"12 NW DCA": distance and direction from the nearest facility airport.</summary>
        public static string Where(LatLon pos, FacilityContext ctx)
        {
            MapAirport? best = null;
            double bd = double.MaxValue;
            foreach (var a in ctx.Airports.Values)
            {
                double d = Geo.DistanceNm(a.Pos, pos);
                if (d < bd) { bd = d; best = a; }
            }
            if (best == null) return "";
            if (bd < 1) return "OVR " + best.Display;
            return ((int)Math.Round(bd)).ToString(CultureInfo.InvariantCulture) + " " +
                   Locations.Compass(Geo.BearingDeg(best.Pos, pos)) + " " + best.Display;
        }
    }
}
