using System;
using System.Collections.Generic;
using CrcCompanion.Core;

/// <summary>Tests for the Continuous Flight Plan Readout (CFR) text.</summary>
internal static class CfrTests
{
    public static void Run(Action<string, bool, string> check, Action<string, object?, object?> eq)
    {
        static string Apt(string id) => id.Length == 4 && id[0] == 'K' ? id.Substring(1) : id;

        // --- route in ERAM format ---
        eq("manual example", "SFO..SLC.J154.OCS.J94.BFF..DBQ..JVL.JVL4.ORD",
            Cfr.Route("KSFO", "SLC J154 OCS J94 BFF DCT DBQ DCT JVL JVL4", "KORD", Apt));
        eq("SID and STAR", "IAD.JCOBY4.JCOBY..BRV.CAMRN4.JFK",
            Cfr.Route("KIAD", "JCOBY4 JCOBY DCT BRV CAMRN4", "KJFK", Apt));
        eq("drops DCT, speed/level, airports", "DCA..SWANN..ATL",
            Cfr.Route("KDCA", "KDCA N0450F350 DCT SWANN/N0450F350 DCT KATL", "KATL", Apt));
        eq("already dotted", "SFO..SLC.J154.OCS..ORD", Cfr.Route("KSFO", "SLC.J154.OCS", "KORD", Apt));
        eq("two connectors in a row", "IAD.JCOBY4.J80.BRV..JFK", Cfr.Route("KIAD", "JCOBY4 J80 BRV", "KJFK", Apt));
        eq("empty route", "DCA..BOS", Cfr.Route("KDCA", "", "KBOS", Apt));
        eq("lat/long is a point", "BOS..4030N07000W..LHR", Cfr.Route("KBOS", "4030N07000W", "LHR", Apt));

        bool more;
        eq("cut", "SFO..SLC", Cfr.Cut("SFO..SLC.J154", 8, out more));
        check("cut flags more", more, "");
        Cfr.Cut("SFO..SLC", 8, out more);
        check("exact fit has no more", !more, "");

        // --- type, ADS-B, proposed time ---
        eq("heavy FAA", "B744/L", Cfr.TypeSuffix("H/B744/L", "B744"));
        eq("FAA", "B738/L", Cfr.TypeSuffix("B738/L", "B738"));
        eq("no suffix", "C172", Cfr.TypeSuffix("", "C172"));
        check("ADS-B B1", !Cfr.NonAdsb("B738/M-SDE2FGHIRWY/LB1"), "");
        check("ADS-B U1", !Cfr.NonAdsb("C172/L-SDG/CU1"), "");
        check("Mode C only is non-ADS-B", Cfr.NonAdsb("C172/L-SDG/C"), "");
        check("no ICAO surveillance: not flagged", !Cfr.NonAdsb("B738/L"), "");
        eq("proposed", "P1530", Cfr.Proposed("1530"));
        eq("proposed padded", "P0905", Cfr.Proposed("905"));
        eq("no time", "", Cfr.Proposed("0"));

        // --- coordination fix / time ---
        var box = new Boundary("KTST", new[] { (IReadOnlyList<LatLon>)new[] { new LatLon(37, -80), new LatLon(37, -75), new LatLon(40, -75), new LatLon(40, -80), new LatLon(37, -80) } });
        var pts = new Dictionary<string, LatLon>
        {
            ["BEHND"] = new LatLon(38.5, -83), ["OUTSD"] = new LatLon(38.5, -81), ["INSDE"] = new LatLon(38.5, -78), ["KDST"] = new LatLon(38.5, -76),
        };
        LatLon? Look(string id) => pts.TryGetValue(id, out var v) ? v : null;
        var plan = new FlightPlan { Departure = "KORG", Arrival = "KDST", Route = "BEHND J1 OUTSD INSDE", DepTime = "1530" };
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var east = new Pilot { Callsign = "T1", Pos = new LatLon(38.5, -82), Heading = 90, Groundspeed = 480 };
        var (fix, tim) = Cfr.Coordination(east, plan, box, Look, Apt, now);
        eq("outside: first fix ahead inside", "INSDE", fix);
        check("estimate prefixed E", tim.StartsWith("E12"), tim);
        var inside = new Pilot { Callsign = "T1", Pos = new LatLon(38.5, -77.5), Heading = 90, Groundspeed = 480 };
        eq("inside: next fix ahead", "DST", Cfr.Coordination(inside, plan, box, Look, Apt, now).Fix);
        var ground = new Pilot { Callsign = "T1", Pos = new LatLon(38.5, -85), Heading = 90, Groundspeed = 0 };
        eq("ground: departure", ("ORG", "P1530"), Cfr.Coordination(ground, plan, box, Look, Apt, now));
        eq("prefile: departure", ("ORG", "P1530"), Cfr.Coordination(null, plan, box, Look, Apt, now));

        // --- QF entry ---
        var items = new List<string>();
        bool Has(string cs) => cs != "NOPE1";
        eq("add", "ADD UAL1 DAL2", Cfr.Apply(items, "QF R UAL1 DAL2", 3, Has));
        eq("no FP", "NO FP NOPE1", Cfr.Apply(items, "NOPE1", 3, Has));
        Cfr.Apply(items, "AAL3 SWA4", 3, Has);
        eq("oldest dropped past max", "DAL2,AAL3,SWA4", string.Join(",", items));
        Cfr.Apply(items, "DAL2", 3, Has);
        eq("re-entry moves last", "AAL3,SWA4,DAL2", string.Join(",", items));
        eq("remove", "DEL AAL3", Cfr.Apply(items, "-AAL3", 3, Has));
        eq("after remove", "SWA4,DAL2", string.Join(",", items));
    }
}
