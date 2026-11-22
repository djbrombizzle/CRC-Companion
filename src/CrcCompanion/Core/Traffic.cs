using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CrcCompanion.Core
{
    /// <summary>Who an aircraft's radios are tuned to.</summary>
    public enum FreqStatus { None, Mine, Center, Approach, Tower, Ground, Other }

    public sealed class StripItem
    {
        public string Callsign { get; init; } = "";
        public string Type { get; init; } = "";
        public string Departure { get; init; } = "";
        public string Destination { get; init; } = "";
        /// <summary>Current altitude (airborne) or filed altitude (departures), hundreds of feet.</summary>
        public int AltitudeHundreds { get; init; }
        public string Squawk { get; init; } = "";
        public string Route { get; init; } = "";
        public double? MinutesToEntry { get; init; }
        public DateTime? EntryUtc { get; init; }
        public FreqStatus Freq { get; init; }
        /// <summary>Callsign of the other local controller the aircraft is tuned to, if any.</summary>
        public string With { get; init; } = "";
        public bool IsPrefile { get; init; }
        /// <summary>Filed altitude as ERAM shows it ("150", "170B210", "VFR", "OTP").</summary>
        public string Filed { get; init; } = "";
        public bool NonRvsm { get; init; }
    }

    public sealed class TrafficResult
    {
        public List<StripItem> Inbound { get; } = new List<StripItem>();
        public List<StripItem> InSector { get; } = new List<StripItem>();
        public List<StripItem> Departures { get; } = new List<StripItem>();
        /// <summary>Other online controllers counted as local (inside the facility).</summary>
        public List<Controller> LocalControllers { get; } = new List<Controller>();
    }

    /// <summary>The facility the user is working (or chose), resolved against map data.</summary>
    public sealed class FacilityContext
    {
        public string Fir { get; init; } = "";
        public Boundary Boundary { get; init; } = null!;
        public Dictionary<string, MapAirport> Airports { get; init; } = new Dictionary<string, MapAirport>(StringComparer.OrdinalIgnoreCase);
        public string MyCallsign { get; init; } = "";
        public long MyFrequencyHz { get; init; }
        public Func<string, string?> FirForCallsign { get; init; } = _ => null;

        public string DisplayAirport(string icao) =>
            Airports.TryGetValue(icao, out var a) ? a.Display
            : icao.Length == 4 && icao[0] == 'K' ? icao.Substring(1) : icao;
    }

    public static class Traffic
    {
        public const int AirborneKt = 50;
        public const int OnGroundKt = 40;
        public const double DepartureRadiusNm = 8;

        public static TrafficResult Compute(FeedSnapshot s, FacilityContext ctx, int inboundMinutes, DateTime nowUtc)
        {
            var r = new TrafficResult();

            // Local controllers: any position (not observers) with a radio inside the facility,
            // or, if no radio data, whose callsign prefix maps to this facility.
            var localFreqs = new List<(long Hz, Controller C)>();
            foreach (var c in s.Controllers)
            {
                if (c.Facility < VatsimFeed.FacDel || c.FrequencyHz == 0) continue;
                if (string.Equals(c.Callsign, ctx.MyCallsign, StringComparison.OrdinalIgnoreCase)) continue;
                if (c.Callsign.EndsWith("_ATIS", StringComparison.OrdinalIgnoreCase)) continue;
                s.Transceivers.TryGetValue(c.Callsign, out var radios);
                bool local = radios != null && radios.Count > 0
                    ? radios.Any(t => ctx.Boundary.Contains(t.Pos))
                    : string.Equals(ctx.FirForCallsign(c.Callsign), ctx.Fir, StringComparison.OrdinalIgnoreCase);
                if (!local) continue;
                r.LocalControllers.Add(c);
                localFreqs.Add((c.FrequencyHz, c));
                if (radios != null)
                    foreach (var t in radios)
                        if (!VatsimFeed.SameFrequency(t.FrequencyHz, c.FrequencyHz)) localFreqs.Add((t.FrequencyHz, c));
            }

            (FreqStatus, string) Classify(string callsign)
            {
                if (!s.Transceivers.TryGetValue(callsign, out var radios) || radios.Count == 0) return (FreqStatus.None, "");
                if (ctx.MyFrequencyHz > 0 && radios.Any(t => VatsimFeed.SameFrequency(t.FrequencyHz, ctx.MyFrequencyHz)))
                    return (FreqStatus.Mine, ctx.MyCallsign);
                foreach (var t in radios)
                    foreach (var (hz, c) in localFreqs)
                        if (VatsimFeed.SameFrequency(t.FrequencyHz, hz)) return (StatusFor(c.Facility), c.Callsign);
                return (FreqStatus.None, "");
            }

            var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in s.Pilots)
            {
                connected.Add(p.Callsign);
                var plan = p.Plan;
                bool airborne = p.Groundspeed >= AirborneKt;

                if (airborne)
                {
                    if (ctx.Boundary.Contains(p.Pos))
                    {
                        var (st, with) = Classify(p.Callsign);
                        r.InSector.Add(Item(p, plan, p.Altitude, st, with, null, nowUtc, ctx));
                    }
                    else
                    {
                        double? mte = Geo.MinutesToEnter(ctx.Boundary, p.Pos, p.Heading, p.Groundspeed, inboundMinutes);
                        if (plan != null && ctx.Airports.TryGetValue(plan.Arrival, out var dest))
                        {
                            var direct = Geo.MinutesToEnter(ctx.Boundary, p.Pos, Geo.BearingDeg(p.Pos, dest.Pos), p.Groundspeed, inboundMinutes);
                            if (direct != null && (mte == null || direct < mte)) mte = direct;
                        }
                        if (mte != null)
                        {
                            var (st, with) = Classify(p.Callsign);
                            r.Inbound.Add(Item(p, plan, p.Altitude, st, with, mte, nowUtc, ctx));
                        }
                    }
                }
                else if (p.Groundspeed < OnGroundKt && plan != null &&
                         ctx.Airports.TryGetValue(plan.Departure, out var dep) &&
                         Geo.DistanceNm(p.Pos, dep.Pos) <= DepartureRadiusNm)
                {
                    var (st, with) = Classify(p.Callsign);
                    r.Departures.Add(Item(p, plan, plan.AltitudeHundreds, st, with, null, nowUtc, ctx));
                }
            }

            foreach (var pf in s.Prefiles)
            {
                if (pf.Plan == null || connected.Contains(pf.Callsign)) continue;
                if (!ctx.Airports.ContainsKey(pf.Plan.Departure)) continue;
                r.Departures.Add(new StripItem
                {
                    Callsign = pf.Callsign,
                    Type = pf.Plan.Type,
                    Departure = pf.Plan.Departure,
                    Destination = pf.Plan.Arrival,
                    AltitudeHundreds = pf.Plan.AltitudeHundreds,
                    Squawk = pf.Plan.AssignedSquawk,
                    Route = pf.Plan.Route,
                    IsPrefile = true,
                    Filed = pf.Plan.FiledAltitude,
                    NonRvsm = pf.Plan.NonRvsm,
                });
            }

            r.Inbound.Sort((a, b) => Nullable.Compare(a.MinutesToEntry, b.MinutesToEntry));
            r.InSector.Sort((a, b) => string.CompareOrdinal(a.Callsign, b.Callsign));
            r.Departures.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(ctx.DisplayAirport(a.Departure), ctx.DisplayAirport(b.Departure));
                return c != 0 ? c : string.CompareOrdinal(a.Callsign, b.Callsign);
            });
            return r;
        }

        private static StripItem Item(Pilot p, FlightPlan? plan, int altFeetOrHundreds, FreqStatus st, string with,
            double? mte, DateTime now, FacilityContext ctx) => new StripItem
            {
                Callsign = p.Callsign,
                Type = plan?.Type ?? "",
                Departure = plan?.Departure ?? "",
                Destination = plan?.Arrival ?? "",
                AltitudeHundreds = mte == null && p.Groundspeed < OnGroundKt ? altFeetOrHundreds : (int)Math.Round(altFeetOrHundreds / 100.0),
                Squawk = p.Squawk,
                Route = plan?.Route ?? "",
                MinutesToEntry = mte,
                EntryUtc = mte == null ? null : now.AddMinutes(mte.Value),
                Freq = st,
                With = with,
                Filed = plan?.FiledAltitude ?? "",
                NonRvsm = plan?.NonRvsm ?? false,
            };

        /// <summary>
        /// IN SECTOR filter. Aircraft working another local controller are hidden only when you're
        /// working a position yourself (connected with a frequency) and OTHERS is off. Observing or
        /// offline, everyone inside is shown, with their frequency marks.
        /// </summary>
        public static IEnumerable<StripItem> VisibleInSector(IEnumerable<StripItem> items, bool connected, bool showOthers) =>
            items.Where(i => showOthers || !connected || i.Freq == FreqStatus.Mine || i.Freq == FreqStatus.None);

        public static FreqStatus StatusFor(int facility) => facility switch
        {
            VatsimFeed.FacCtr => FreqStatus.Center,
            VatsimFeed.FacApp => FreqStatus.Approach,
            VatsimFeed.FacTwr => FreqStatus.Tower,
            VatsimFeed.FacGnd or VatsimFeed.FacDel => FreqStatus.Ground,
            _ => FreqStatus.Other,
        };
    }

    /// <summary>One-line strip text, eATS-style columns.</summary>
    public static class StripFormat
    {
        public const int CallsignChars = 8;

        /// <summary>
        /// Mark column: * on your frequency, A approach/departure, T tower, G ground/delivery,
        /// C another Center sector, ? in your airspace on nobody's frequency (only when you're connected),
        /// P prefiled (not connected yet), blank otherwise.
        /// </summary>
        public static char Mark(StripItem i, bool flagUntuned)
        {
            if (i.IsPrefile) return 'P';
            return i.Freq switch
            {
                FreqStatus.Mine => '*',
                FreqStatus.Approach => 'A',
                FreqStatus.Tower => 'T',
                FreqStatus.Ground => 'G',
                FreqStatus.Center => 'C',
                FreqStatus.Other => 'O',
                _ => flagUntuned ? '?' : ' ',
            };
        }

        private static string Fit(string s, int n) => s.Length > n ? s.Substring(0, n) : s.PadRight(n);

        private static string Alt(int hundreds) => Math.Clamp(hundreds, 0, 999).ToString("000", CultureInfo.InvariantCulture);

        private static string Sq(string s) => Fit(string.IsNullOrWhiteSpace(s) ? "" : s.Trim(), 4);

        /// <summary>"* AAL1272 A319 E1552 350 2476 ROUTE..." (E = estimated entry time, UTC).</summary>
        public static string Inbound(StripItem i, int routeChars, bool flagUntuned, bool routeTail = false)
        {
            var e = i.EntryUtc.HasValue ? "E" + i.EntryUtc.Value.ToString("HHmm", CultureInfo.InvariantCulture) : "     ";
            return Line(Mark(i, false), i, e, Alt(i.AltitudeHundreds), routeChars, routeTail);
        }

        /// <summary>"* AAL1272 A319 DCA  350 2476 ROUTE..." (destination, current altitude).</summary>
        public static string InSector(StripItem i, int routeChars, bool flagUntuned, Func<string, string> airport, bool routeTail = false)
            => Line(Mark(i, flagUntuned), i, Fit(airport(i.Destination), 5), Alt(i.AltitudeHundreds), routeChars, routeTail);

        /// <summary>"P AAL1272 A319 DFW  350 2476 ROUTE..." (destination, filed altitude).</summary>
        public static string Departure(StripItem i, int routeChars, Func<string, string> airport)
            => Line(Mark(i, false), i, Fit(airport(i.Destination), 5), Alt(i.AltitudeHundreds), routeChars);

        private static string Line(char mark, StripItem i, string col3, string alt, int routeChars, bool routeTail = false) =>
            (mark + " " + Fit(i.Callsign, CallsignChars) + " " + Fit(i.Type, 4) + " " + col3 + " " + alt + " " +
             Sq(i.Squawk) + " " + FitRoute(i.Route, Math.Max(0, routeChars), routeTail)).TrimEnd();

        /// <summary>Marks a route shown from its end ("..BAMMY FRDMM6").</summary>
        public const string TailMark = "..";

        /// <summary>
        /// Fits a route into n characters. From the start: cut at n. From the end (arrivals into your
        /// facility): the last whole words that fit, after "..", so the STAR and arrival fixes show.
        /// </summary>
        public static string FitRoute(string route, int n, bool fromEnd)
        {
            var r = string.Join(" ", (route ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (r.Length <= n || n <= 0) return n <= 0 ? "" : r;
            if (!fromEnd) return r.Substring(0, n);

            var words = r.Split(' ');
            int budget = n - TailMark.Length;
            var tail = "";
            for (int k = words.Length - 1; k >= 0; k--)
            {
                var next = tail.Length == 0 ? words[k] : words[k] + " " + tail;
                if (next.Length > budget) break;
                tail = next;
            }
            // A single last word longer than the space: show its end.
            if (tail.Length == 0) tail = words[^1].Substring(Math.Max(0, words[^1].Length - budget));
            return TailMark + tail;
        }

        public static int Width(int routeChars) => 1 + 1 + CallsignChars + 1 + 4 + 1 + 5 + 1 + 3 + 1 + 4 + 1 + routeChars;
    }
}
