using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CrcCompanion.Core
{
    /// <summary>
    /// Continuous Flight Plan Readout (ERAM CFR view) text: the route in ERAM's dotted format, the coordination
    /// fix and time, and the non-ADS-B indicator.
    /// </summary>
    public static class Cfr
    {
        /// <summary>Filter pop-up buttons, in the menu's order (Table 8-1).</summary>
        public static readonly string[] Fields = { "ALT", "BCN", "CID", "FIX", "LBL", "RTE", "SPD", "TIM", "TYP" };

        public const int EntriesMin = 1, EntriesMax = 10, EntriesDefault = 10;
        public const int RteMin = 30, RteMax = 100, RteDefault = 35;

        private static readonly Regex SpeedLevel = new Regex(@"^([NKM]\d{3,4})([FASM]\d{3,4})$", RegexOptions.CultureInvariant);
        private static readonly Regex LatLon = new Regex(@"^\d{2,4}[NS]\d{3,5}[EW]$", RegexOptions.CultureInvariant);

        /// <summary>
        /// The filed route as ERAM prints it: "SFO..SLC.J154.OCS..DBQ.JVL4.ORD". Direct legs are "..", an airway or
        /// procedure is joined to the fixes either side with single dots. DCT and speed/level groups are dropped.
        /// </summary>
        public static string Route(string departure, string route, string arrival, Func<string, string> airport)
        {
            var dep = airport(departure ?? "");
            var arr = airport(arrival ?? "");
            var tokens = new List<string>();
            foreach (var raw in (route ?? "").ToUpperInvariant().Split(new[] { ' ', '.', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = raw.Split('/')[0];
                if (t.Length == 0 || t == "DCT" || t == "IFR" || t == "VFR" || SpeedLevel.IsMatch(t)) continue;
                tokens.Add(t);
            }
            if (tokens.Count > 0 && Same(tokens[0], departure, dep)) tokens.RemoveAt(0);
            if (tokens.Count > 0 && Same(tokens[^1], arrival, arr)) tokens.RemoveAt(tokens.Count - 1);

            var sb = new StringBuilder(dep);
            bool afterConnector = false;
            foreach (var t in tokens)
            {
                if (IsConnector(t))
                {
                    if (!afterConnector) sb.Append('.');
                    sb.Append(t).Append('.');
                    afterConnector = true;
                }
                else
                {
                    if (!afterConnector) sb.Append("..");
                    sb.Append(t);
                    afterConnector = false;
                }
            }
            if (arr.Length > 0)
            {
                if (!afterConnector) sb.Append("..");
                sb.Append(arr);
            }
            else if (afterConnector) sb.Length--;
            return sb.ToString();
        }

        private static bool Same(string token, string? icao, string display) =>
            token.Length > 0 && (string.Equals(token, icao, StringComparison.OrdinalIgnoreCase) || string.Equals(token, display, StringComparison.OrdinalIgnoreCase));

        /// <summary>An airway or procedure (J154, Q100, JVL4, CAMRN4): it has a digit and isn't a lat/long.</summary>
        public static bool IsConnector(string t) => t.Any(char.IsDigit) && !LatLon.IsMatch(t);

        /// <summary>The fixes (not airways or procedures) in a filed route, in order.</summary>
        public static List<string> Points(string route) =>
            (route ?? "").ToUpperInvariant().Split(new[] { ' ', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Split('/')[0])
                .Where(t => t.Length >= 2 && t.Length <= 5 && t != "DCT" && t.All(c => c >= 'A' && c <= 'Z'))
                .ToList();

        /// <summary>
        /// Route text cut to <paramref name="chars"/> (the RTE setting). <paramref name="more"/> is set when text was
        /// left off (the boxed asterisk then shows the rest).
        /// </summary>
        public static string Cut(string route, int chars, out bool more)
        {
            more = route.Length > chars;
            return more ? route.Substring(0, chars) : route;
        }

        /// <summary>
        /// Coordination fix and time. Airborne: the next fix ahead on the filed route (the first one inside the
        /// facility while still outside it), with an estimate "E1615" from groundspeed. On the ground or prefiled:
        /// the departure airport and the proposed time "P1530".
        /// </summary>
        public static (string Fix, string Time) Coordination(Pilot? pilot, FlightPlan plan, Boundary? boundary,
            Func<string, LatLon?> lookup, Func<string, string> airport, DateTime nowUtc, int airborneKt = 50)
        {
            if (pilot == null || pilot.Groundspeed < airborneKt)
                return (airport(plan.Departure), Proposed(plan.DepTime));

            var ahead = new List<(string Name, LatLon Pos)>();
            foreach (var name in Points(plan.Route).Append(plan.Arrival))
            {
                var pos = lookup(name);
                if (pos == null) continue;
                double d = Geo.DistanceNm(pilot.Pos, pos.Value);
                if (d < 2) continue;
                double off = Math.Abs(((Geo.BearingDeg(pilot.Pos, pos.Value) - pilot.Heading) % 360 + 540) % 360 - 180);
                if (off < 90) ahead.Add((name, pos.Value));
            }
            if (ahead.Count == 0) return ("", "");
            var pick = ahead[0];
            if (boundary != null && !boundary.Contains(pilot.Pos))
            {
                var inside = ahead.FirstOrDefault(a => boundary.Contains(a.Pos));
                if (inside.Name != null) pick = inside;
            }
            double minutes = Geo.DistanceNm(pilot.Pos, pick.Pos) / Math.Max(pilot.Groundspeed, 1) * 60;
            var name2 = string.Equals(pick.Name, plan.Arrival, StringComparison.OrdinalIgnoreCase) ? airport(pick.Name) : pick.Name;
            return (name2, "E" + nowUtc.AddMinutes(minutes).ToString("HHmm", CultureInfo.InvariantCulture));
        }

        /// <summary>Filed departure time as "P1530" ("" when none).</summary>
        public static string Proposed(string depTime)
        {
            var t = (depTime ?? "").Trim();
            if (t.Length == 0 || !t.All(char.IsDigit) || t.Length > 4) return "";
            t = t.PadLeft(4, '0');
            return t == "0000" ? "" : "P" + t;
        }

        /// <summary>
        /// Type with equipment suffix as ERAM shows it ("B744/L"), from the FAA-format aircraft field ("H/B744/L").
        /// </summary>
        public static string TypeSuffix(string aircraftFaa, string shortType)
        {
            var parts = (aircraftFaa ?? "").Trim().ToUpperInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length == 1 && !char.IsDigit(parts[0][0])) parts = parts.Skip(1).ToArray();
            if (parts.Length >= 2 && parts[1].Length == 1) return parts[0] + "/" + parts[1];
            if (parts.Length >= 1 && parts[0].Length > 1) return parts[0];
            return shortType ?? "";
        }

        /// <summary>
        /// Non-ADS-B: the ICAO equipment ("B738/M-SDE2FGHIRWY/LB1") lists surveillance codes and none of them is
        /// ADS-B (B1 B2 U1 U2 V1 V2). False when the plan has no ICAO surveillance field to tell.
        /// </summary>
        public static bool NonAdsb(string aircraftIcao)
        {
            var a = (aircraftIcao ?? "").Trim().ToUpperInvariant();
            int dash = a.IndexOf('-');
            if (dash < 0) return false;
            int slash = a.IndexOf('/', dash);
            if (slash < 0 || slash == a.Length - 1) return false;
            var surv = a.Substring(slash + 1);
            return !new[] { "B1", "B2", "U1", "U2", "V1", "V2" }.Any(surv.Contains);
        }

        /// <summary>
        /// Applies a QF entry to the readout list (oldest first): "QF R UAL123", "UAL123", "UAL123 DAL45", "-UAL123".
        /// New flights go last; the oldest drop off past <paramref name="max"/>. A flight already listed moves last.
        /// <paramref name="exists"/> says whether a flight plan can be found. Returns feedback.
        /// </summary>
        public static string Apply(List<string> items, string text, int max, Func<string, bool> exists)
        {
            var toks = (text ?? "").ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (toks.Count > 0 && toks[0] == "QF") toks.RemoveAt(0);
            if (toks.Count > 0 && toks[0] == "R") toks.RemoveAt(0);
            var added = new List<string>(); var removed = new List<string>(); var none = new List<string>();
            foreach (var tok in toks)
            {
                if (tok.StartsWith("-", StringComparison.Ordinal))
                {
                    var cs = tok.Substring(1);
                    if (items.RemoveAll(i => string.Equals(i, cs, StringComparison.OrdinalIgnoreCase)) > 0) removed.Add(cs);
                    continue;
                }
                if (!exists(tok)) { none.Add(tok); continue; }
                items.RemoveAll(i => string.Equals(i, tok, StringComparison.OrdinalIgnoreCase));
                items.Add(tok);
                added.Add(tok);
            }
            while (items.Count > max) items.RemoveAt(0);
            return string.Join("  ", new[]
            {
                added.Count > 0 ? "ADD " + string.Join(" ", added) : "",
                removed.Count > 0 ? "DEL " + string.Join(" ", removed) : "",
                none.Count > 0 ? "NO FP " + string.Join(" ", none) : "",
            }.Where(s => s.Length > 0));
        }
    }
}
