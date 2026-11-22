using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace CrcCompanion.Core
{
    public sealed class FlightPlan
    {
        public string Rules { get; init; } = "";
        public string Type { get; init; } = "";
        public string Departure { get; init; } = "";
        public string Arrival { get; init; } = "";
        /// <summary>Filed altitude in hundreds of feet (35000 / FL350 → 350). 0 if unknown.</summary>
        public int AltitudeHundreds { get; init; }
        public string Route { get; init; } = "";
        public string AssignedSquawk { get; init; } = "";
        /// <summary>Filed altitude as ERAM shows it ("150", "170B210", "VFR", "OTP"); see StripLists.FiledAltitude.</summary>
        public string FiledAltitude { get; init; } = "";
        /// <summary>The equipment says the aircraft isn't RVSM approved.</summary>
        public bool NonRvsm { get; init; }
        /// <summary>Filed cruise TAS, knots (0 if none).</summary>
        public int CruiseTas { get; init; }
        /// <summary>Filed departure time "HHMM".</summary>
        public string DepTime { get; init; } = "";
        /// <summary>Type with equipment suffix as ERAM shows it ("B744/L").</summary>
        public string TypeSuffix { get; init; } = "";
        /// <summary>Filed remarks.</summary>
        public string Remarks { get; init; } = "";
        /// <summary>The ICAO surveillance codes list no ADS-B (CFR's coral "A").</summary>
        public bool NonAdsb { get; init; }
    }

    public sealed class Pilot
    {
        public int Cid { get; init; }
        public string Callsign { get; init; } = "";
        public LatLon Pos { get; init; }
        public int Altitude { get; init; }
        public int Groundspeed { get; init; }
        public int Heading { get; init; }
        public string Squawk { get; init; } = "";
        public FlightPlan? Plan { get; init; }
    }

    public sealed class Prefile
    {
        public int Cid { get; init; }
        public string Callsign { get; init; } = "";
        public FlightPlan? Plan { get; init; }
    }

    public sealed class Controller
    {
        public int Cid { get; init; }
        public string Callsign { get; init; } = "";
        /// <summary>Primary frequency in Hz (133.700 → 133700000). 0 if none (199.998 = observer).</summary>
        public long FrequencyHz { get; init; }
        /// <summary>VATSIM facility code: 0 OBS, 1 FSS, 2 DEL, 3 GND, 4 TWR, 5 APP, 6 CTR.</summary>
        public int Facility { get; init; }
    }

    public sealed class Transceiver
    {
        public long FrequencyHz { get; init; }
        public LatLon Pos { get; init; }
    }

    /// <summary>One poll of VATSIM's live data (main feed + voice transceivers).</summary>
    public sealed class FeedSnapshot
    {
        public DateTime UpdatedUtc { get; init; }
        public List<Pilot> Pilots { get; } = new List<Pilot>();
        public List<Prefile> Prefiles { get; } = new List<Prefile>();
        public List<Controller> Controllers { get; } = new List<Controller>();
        public Dictionary<string, List<Transceiver>> Transceivers { get; } =
            new Dictionary<string, List<Transceiver>>(StringComparer.OrdinalIgnoreCase);
    }

    public static class VatsimFeed
    {
        public const int FacObs = 0, FacFss = 1, FacDel = 2, FacGnd = 3, FacTwr = 4, FacApp = 5, FacCtr = 6;

        /// <summary>Parses https://data.vatsim.net/v3/vatsim-data.json into a snapshot.</summary>
        public static FeedSnapshot ParseMain(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            DateTime updated = DateTime.UtcNow;
            if (root.TryGetProperty("general", out var gen) && gen.TryGetProperty("update_timestamp", out var ts) &&
                DateTime.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t))
                updated = t;

            var snap = new FeedSnapshot { UpdatedUtc = updated };

            if (root.TryGetProperty("pilots", out var pilots))
                foreach (var p in pilots.EnumerateArray())
                    snap.Pilots.Add(new Pilot
                    {
                        Cid = Int(p, "cid"),
                        Callsign = Str(p, "callsign"),
                        Pos = new LatLon(Dbl(p, "latitude"), Dbl(p, "longitude")),
                        Altitude = Int(p, "altitude"),
                        Groundspeed = Int(p, "groundspeed"),
                        Heading = Int(p, "heading"),
                        Squawk = Str(p, "transponder"),
                        Plan = Plan(p),
                    });

            if (root.TryGetProperty("prefiles", out var pre))
                foreach (var p in pre.EnumerateArray())
                    snap.Prefiles.Add(new Prefile { Cid = Int(p, "cid"), Callsign = Str(p, "callsign"), Plan = Plan(p) });

            if (root.TryGetProperty("controllers", out var ctrls))
                foreach (var c in ctrls.EnumerateArray())
                    snap.Controllers.Add(new Controller
                    {
                        Cid = Int(c, "cid"),
                        Callsign = Str(c, "callsign"),
                        FrequencyHz = ParseMhz(Str(c, "frequency")),
                        Facility = Int(c, "facility"),
                    });

            return snap;
        }

        /// <summary>Adds https://data.vatsim.net/v3/transceivers-data.json to a snapshot.</summary>
        public static void AddTransceivers(FeedSnapshot snap, string json)
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var st in doc.RootElement.EnumerateArray())
            {
                var cs = Str(st, "callsign");
                if (cs.Length == 0 || !st.TryGetProperty("transceivers", out var arr)) continue;
                var list = new List<Transceiver>();
                foreach (var t in arr.EnumerateArray())
                {
                    long f = t.TryGetProperty("frequency", out var fe) && fe.TryGetInt64(out var fv) ? fv : 0;
                    if (f <= 0) continue;
                    list.Add(new Transceiver { FrequencyHz = f, Pos = new LatLon(Dbl(t, "latDeg"), Dbl(t, "lonDeg")) });
                }
                snap.Transceivers[cs] = list;
            }
        }

        /// <summary>"133.700" → 133700000. Observer placeholder 199.998 → 0.</summary>
        public static long ParseMhz(string s)
        {
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var mhz)) return 0;
            if (mhz >= 199.99 || mhz < 100) return 0;
            return (long)Math.Round(mhz * 1_000_000);
        }

        /// <summary>Frequencies match within 3 kHz (absorbs rounding noise like 122.800004).</summary>
        public static bool SameFrequency(long a, long b) => a > 0 && b > 0 && Math.Abs(a - b) < 3000;

        /// <summary>"35000", "FL350", "350" → 350; "VFR" → 0.</summary>
        public static int ParseAltitudeHundreds(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            var t = s.Trim().ToUpperInvariant();
            if (t.StartsWith("FL", StringComparison.Ordinal)) t = t.Substring(2);
            if (!int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return 0;
            return v >= 1000 ? (int)Math.Round(v / 100.0) : v;
        }

        /// <summary>"H/B744/L" or "B744/L" → "B744"; prefers aircraft_short when present.</summary>
        public static string ShortType(string aircraftShort, string aircraftFaa)
        {
            if (!string.IsNullOrWhiteSpace(aircraftShort)) return aircraftShort.Trim().ToUpperInvariant();
            var parts = (aircraftFaa ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "";
            if (parts.Length >= 2 && parts[0].Length == 1) return parts[1].ToUpperInvariant();
            return parts[0].ToUpperInvariant();
        }

        private static FlightPlan? Plan(JsonElement e)
        {
            if (!e.TryGetProperty("flight_plan", out var fp) || fp.ValueKind != JsonValueKind.Object) return null;
            return new FlightPlan
            {
                Rules = Str(fp, "flight_rules"),
                Type = ShortType(Str(fp, "aircraft_short"), Str(fp, "aircraft_faa")),
                Departure = Str(fp, "departure").ToUpperInvariant(),
                Arrival = Str(fp, "arrival").ToUpperInvariant(),
                AltitudeHundreds = ParseAltitudeHundreds(Str(fp, "altitude")),
                Route = Str(fp, "route").ToUpperInvariant(),
                AssignedSquawk = Str(fp, "assigned_transponder"),
                FiledAltitude = StripLists.FiledAltitude(Str(fp, "flight_rules"), Str(fp, "altitude")),
                NonRvsm = StripLists.IsNonRvsm(Str(fp, "aircraft"), Str(fp, "aircraft_faa")),
                CruiseTas = int.TryParse(Str(fp, "cruise_tas"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tas) ? tas : 0,
                DepTime = Str(fp, "deptime"),
                TypeSuffix = Cfr.TypeSuffix(Str(fp, "aircraft_faa"), ShortType(Str(fp, "aircraft_short"), Str(fp, "aircraft_faa"))),
                Remarks = Str(fp, "remarks").Trim().ToUpperInvariant(),
                NonAdsb = Cfr.NonAdsb(Str(fp, "aircraft")),
            };
        }

        private static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? "",
                JsonValueKind.Number => v.GetRawText(),
                _ => "",
            } : "";

        private static int Int(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i
            : int.TryParse(Str(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var j) ? j : 0;

        private static double Dbl(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    }
}
