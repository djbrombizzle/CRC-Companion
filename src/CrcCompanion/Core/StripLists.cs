using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CrcCompanion.Core
{
    /// <summary>One titled group in the ERAM INBND / DEPT list views.</summary>
    public sealed class Sublist
    {
        public string Title { get; init; } = "";
        public List<StripItem> Items { get; } = new List<StripItem>();
        /// <summary>Earliest boundary time in the group (inbound), for BOUNDARY TIME sort.</summary>
        public DateTime? FirstEntryUtc => Items.Where(i => i.EntryUtc != null).Select(i => i.EntryUtc).Min();
    }

    /// <summary>
    /// ERAM Inbound and Departure List views (R-Position User Manual 13.3, 13.4): sublists with a title, each
    /// line an AID and its filed altitude (non-RVSM aircraft boxed), plus the boundary time on inbound lines.
    /// </summary>
    public static class StripLists
    {
        public const string SortTime = "TIME", SortName = "NAME";

        private static readonly Regex Block = new Regex(@"^(?:FL)?(\d{2,3})B(?:FL)?(\d{2,3})$");

        /// <summary>
        /// Filed altitude as ERAM shows it: hundreds without leading zeros ("60", "150", "350"), a block
        /// ("170B210"), "OTP" (VFR on top), "VFR" or "VFR/65" for VFR plans. "" when nothing was filed.
        /// </summary>
        public static string FiledAltitude(string rules, string rawAltitude)
        {
            var raw = (rawAltitude ?? "").Trim().ToUpperInvariant().Replace(" ", "");
            if (raw.StartsWith("OTP", StringComparison.Ordinal)) return "OTP";
            var b = Block.Match(raw);
            if (b.Success) return int.Parse(b.Groups[1].Value, CultureInfo.InvariantCulture) + "B" + int.Parse(b.Groups[2].Value, CultureInfo.InvariantCulture);
            if (raw.StartsWith("VFR", StringComparison.Ordinal)) raw = raw.Substring(3).TrimStart('/');
            int h = VatsimFeed.ParseAltitudeHundreds(raw);
            bool vfr = string.Equals((rules ?? "").Trim(), "V", StringComparison.OrdinalIgnoreCase) ||
                       (rawAltitude ?? "").Trim().StartsWith("VFR", StringComparison.OrdinalIgnoreCase);
            if (vfr) return h > 0 ? "VFR/" + h.ToString(CultureInfo.InvariantCulture) : "VFR";
            return h > 0 ? h.ToString(CultureInfo.InvariantCulture) : "";
        }

        /// <summary>
        /// Non-RVSM from the flight plan's equipment: the ICAO form ("B738/M-SDE2FGHIRWY/LB1") is RVSM when its
        /// equipment codes include W; the FAA form ("B738/L") is RVSM with suffix H, W, Z or L. Unknown → false.
        /// </summary>
        public static bool IsNonRvsm(string aircraftIcao, string aircraftFaa)
        {
            var icao = (aircraftIcao ?? "").Trim().ToUpperInvariant();
            int dash = icao.IndexOf('-');
            if (dash >= 0)
            {
                var equip = icao.Substring(dash + 1);
                int slash = equip.IndexOf('/');
                if (slash >= 0) equip = equip.Substring(0, slash);
                if (equip.Length > 0) return !equip.Contains('W');
            }
            var parts = (aircraftFaa ?? "").Trim().ToUpperInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var suffix = parts[^1];
                if (suffix.Length == 1 && char.IsLetter(suffix[0]) && !(parts.Length == 2 && parts[0].Length == 1))
                    return "HWZL".IndexOf(suffix[0]) < 0;
            }
            return false;
        }

        /// <summary>
        /// The inbound fix: the first fix in the filed route that lies inside the facility (procedures, airways
        /// and DCT are skipped). Null when none resolves.
        /// </summary>
        public static string? InboundFix(string route, Boundary boundary, Func<string, LatLon?> lookup)
        {
            foreach (var raw in (route ?? "").ToUpperInvariant().Split(new[] { ' ', '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = raw.Split('/')[0];
                if (t.Length < 2 || t.Length > 5 || t == "DCT" || !t.All(c => c >= 'A' && c <= 'Z')) continue;
                var pos = lookup(t);
                if (pos != null && boundary.Contains(pos.Value)) return t;
            }
            return null;
        }

        /// <summary>
        /// Groups items into sublists. With <paramref name="names"/> set, those sublists are shown (even empty) and a
        /// flight goes in the first whose name it matches; flights matching none are left out, as with ERAM's adapted
        /// lists. With no names, one sublist per automatic key. Items keep their order within a sublist.
        /// Sort: <see cref="SortName"/> alphabetical, <see cref="SortTime"/> by earliest boundary time, "" = list
        /// order (automatic sublists: alphabetical).
        /// </summary>
        public static List<Sublist> Group(IEnumerable<StripItem> items, IReadOnlyList<string> names,
            Func<StripItem, string> autoKey, Func<string, StripItem, bool> matches, string sort)
        {
            var result = new List<Sublist>();
            if (names.Count > 0)
            {
                foreach (var n in names) result.Add(new Sublist { Title = n });
                foreach (var i in items)
                {
                    var s = result.FirstOrDefault(g => matches(g.Title, i));
                    s?.Items.Add(i);
                }
            }
            else
            {
                var byKey = new Dictionary<string, Sublist>(StringComparer.OrdinalIgnoreCase);
                foreach (var i in items)
                {
                    var k = autoKey(i);
                    if (!byKey.TryGetValue(k, out var s)) { byKey[k] = s = new Sublist { Title = k }; result.Add(s); }
                    s.Items.Add(i);
                }
                if (sort.Length == 0) sort = SortName;
            }
            if (sort == SortName) result = result.OrderBy(s => s.Title, StringComparer.Ordinal).ToList();
            else if (sort == SortTime) result = result.OrderBy(s => s.FirstEntryUtc ?? DateTime.MaxValue).ThenBy(s => s.Title, StringComparer.Ordinal).ToList();
            return result;
        }

        /// <summary>Airport name match: "JFK" matches KJFK and JFK.</summary>
        public static bool SameAirport(string name, string icao, Func<string, string> display) =>
            string.Equals(name, icao, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, display(icao), StringComparison.OrdinalIgnoreCase);

        /// <summary>A name in the filed route (fix, airway or procedure), e.g. "CAMRN" in "... CAMRN4".</summary>
        public static bool InRoute(string name, string route) =>
            (route ?? "").ToUpperInvariant().Split(new[] { ' ', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(t => string.Equals(t.Split('/')[0], name, StringComparison.OrdinalIgnoreCase));
    }
}
