using System;
using System.Collections.Generic;
using System.Linq;
using CrcCompanion.Core;

/// <summary>Tests for the ERAM INBND / DEPT sublist format.</summary>
internal static class StripListTests
{
    public static void Run(Action<string, bool, string> check, Action<string, object?, object?> eq)
    {
        // --- filed altitude ---
        eq("6000 -> 60", "60", StripLists.FiledAltitude("I", "6000"));
        eq("15000 -> 150", "150", StripLists.FiledAltitude("I", "15000"));
        eq("FL350 -> 350", "350", StripLists.FiledAltitude("I", "FL350"));
        eq("block", "170B210", StripLists.FiledAltitude("I", "FL170BFL210"));
        eq("block plain", "170B210", StripLists.FiledAltitude("I", "170B210"));
        eq("OTP", "OTP", StripLists.FiledAltitude("V", "OTP/115"));
        eq("VFR no altitude", "VFR", StripLists.FiledAltitude("V", "VFR"));
        eq("VFR with altitude", "VFR/45", StripLists.FiledAltitude("V", "4500"));
        eq("nothing filed", "", StripLists.FiledAltitude("I", ""));

        // --- non-RVSM ---
        check("ICAO with W is RVSM", !StripLists.IsNonRvsm("B738/M-SDE2FGHIRWY/LB1", "B738/L"), "");
        check("ICAO without W is non-RVSM", StripLists.IsNonRvsm("C172/L-SDGY/C", ""), "");
        check("FAA /L is RVSM", !StripLists.IsNonRvsm("", "B738/L"), "");
        check("FAA /G is non-RVSM", StripLists.IsNonRvsm("", "C172/G"), "");
        check("FAA heavy prefix /L is RVSM", !StripLists.IsNonRvsm("", "H/B744/L"), "");
        check("unknown is not flagged", !StripLists.IsNonRvsm("", "B738"), "");

        // --- inbound fix ---
        var box = new Boundary("KTST", new[] { (IReadOnlyList<LatLon>)new[] { new LatLon(37, -80), new LatLon(37, -75), new LatLon(40, -75), new LatLon(40, -80), new LatLon(37, -80) } });
        var pts = new Dictionary<string, LatLon>
        {
            ["MXE"] = new LatLon(39.9, -75.7), ["OUTSD"] = new LatLon(42, -70), ["PENSY"] = new LatLon(39.9, -76.2),
        };
        LatLon? Look(string id) => pts.TryGetValue(id, out var v) ? v : null;
        eq("first fix inside", "MXE", StripLists.InboundFix("OUTSD J6 MXE PENSY CAPSS3", box, Look));
        eq("dotted route", "PENSY", StripLists.InboundFix("OUTSD..PENSY.BRDJE5", box, Look));
        eq("none", null, StripLists.InboundFix("OUTSD J6", box, Look));
        check("in route", StripLists.InRoute("CAMRN", "J6 CAMRN CAMRN4"), "");
        check("airport match JFK=KJFK", StripLists.SameAirport("JFK", "KJFK", i => i.Substring(1)), "");

        // --- grouping ---
        var t0 = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
        StripItem I(string cs, string dep, string fix, int min) =>
            new StripItem { Callsign = cs, Departure = dep, Route = fix, EntryUtc = t0.AddMinutes(min) };
        var items = new[] { I("AAL1", "KJFK", "DEF", 9), I("UAL2", "KLGA", "ABC", 5), I("DAL3", "KJFK", "ABC", 7), I("N4", "KPHL", "XYZ", 1) };
        var auto = StripLists.Group(items, new List<string>(), i => i.Route, (n, i) => false, "");
        eq("auto groups alphabetical", "ABC:UAL2,DAL3|DEF:AAL1|XYZ:N4",
            string.Join("|", auto.Select(g => g.Title + ":" + string.Join(",", g.Items.Select(i => i.Callsign)))));
        var byTime = StripLists.Group(items, new List<string>(), i => i.Route, (n, i) => false, StripLists.SortTime);
        eq("boundary time sort", "XYZ,ABC,DEF", string.Join(",", byTime.Select(g => g.Title)));
        var named = StripLists.Group(items, new List<string> { "LGA", "JFK", "BOS" }, i => i.Departure,
            (n, i) => StripLists.SameAirport(n, i.Departure, a => a.Substring(1)), "");
        eq("named sublists keep order, empty kept, unmatched left out", "LGA:UAL2|JFK:AAL1,DAL3|BOS:",
            string.Join("|", named.Select(g => g.Title + ":" + string.Join(",", g.Items.Select(i => i.Callsign)))));
        var namedSorted = StripLists.Group(items, new List<string> { "LGA", "JFK", "BOS" }, i => i.Departure,
            (n, i) => StripLists.SameAirport(n, i.Departure, a => a.Substring(1)), StripLists.SortName);
        eq("fix name sort", "BOS,JFK,LGA", string.Join(",", namedSorted.Select(g => g.Title)));
    }
}
