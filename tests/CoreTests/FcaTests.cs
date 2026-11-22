using System;
using System.Collections.Generic;
using System.Linq;
using CrcCompanion.Core;

/// <summary>Tests for the VATFLOW FCA client and sequencing port behind the MRP view.</summary>
internal static class FcaTests
{
    private sealed class World : IFcaWorld
    {
        public Dictionary<string, LatLon> Airports = new Dictionary<string, LatLon>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, LatLon> Fixes = new Dictionary<string, LatLon>(StringComparer.OrdinalIgnoreCase);
        public Func<string, LatLon, bool?> Artcc = (_, _) => null;
        public VatflowNav? Nav { get; set; }
        public LatLon? Airport(string icao) => Airports.TryGetValue(icao ?? "", out var p) ? p : null;
        public LatLon? Fix(string name, LatLon near) => Fixes.TryGetValue(name ?? "", out var p) ? p : null;
        public bool? InArtcc(string artcc, LatLon p) => Artcc(artcc, p);
    }

    private const string Row =
        "[{\"id\":\"fca_1\",\"updated_at\":\"2026-11-22T14:49:37.958+00:00\",\"data\":{\"id\":\"fca_1\",\"name\":\"XYZ [LINEE#]\"," +
        "\"artcc\":\"ZDC\",\"mode\":\"mit\",\"mit\":20,\"rate\":30,\"minFL\":100,\"maxFL\":600,\"dir\":\"any\",\"dests\":[\"KXYZ\"],\"origins\":[]," +
        "\"fixes\":[],\"scope\":[],\"points\":[[38,-77],[40,-77]],\"enabled\":true,\"excluded\":[\"EXCL1\"],\"order\":[],\"manualSeq\":false," +
        "\"releases\":{\"REL1\":{\"ctaMs\":1795400000000,\"edctMs\":1795399000000}},\"color\":\"#a3e635\",\"trackCrossings\":true}}]";

    private static FcaPilot Air(string cs, double lon, int gs = 420) => new FcaPilot
    {
        Callsign = cs, Pos = new LatLon(39, lon), Alt = 35000, Gs = gs, Hdg = 90,
        Dep = "KABC", Arr = "KXYZ", FpAlt = 35000, Tas = 450,
    };

    public static void Run(Action<string, bool, string> check, Action<string, object?, object?> eq)
    {
        // --- parsing ---
        var list = FcaProgram.ParseRows(Row);
        eq("parse one program", 1, list.Count);
        var f = list[0];
        eq("artcc", "ZDC", f.Artcc);
        eq("mode", "mit", f.Mode);
        eq("points", 2, f.Points.Count);
        eq("dests", "KXYZ", string.Join(",", f.Dests));
        eq("release cta", 1795400000000L, f.Releases["REL1"].CtaMs);
        eq("excluded", "EXCL1", string.Join(",", f.Excluded));
        eq("fix title from brackets", "LINEE", f.FixTitle);
        eq("header", "XYZ MIT 20", f.Header);
        eq("via title", "BONNT", new FcaProgram { Name = "ORD [via BONNT]" }.FixTitle);
        eq("paren title", "SKILS", new FcaProgram { Name = "DCA (SKILS)" }.FixTitle);
        eq("name title", "MCO ALL", new FcaProgram { Name = "MCO ALL" }.FixTitle);
        eq("nested paren title", "RAVNN", new FcaProgram { Name = "BWI [RAVNN (WALKN)]" }.FixTitle);
        eq("rate header", "MCO RATE 30", new FcaProgram { Dests = new List<string> { "KMCO" }, Mode = "rate", Rate = 30 }.Header);

        // --- filters ---
        check("airport codes MIA=KMIA", FcaEngine.AirportCodesMatch("MIA", "KMIA"), "");
        check("fix token match", FcaEngine.MatchesFix(new FcaProgram { Fixes = new List<string> { "LAIRI" } }, "J6 LAIRI4"), "");
        check("fix token miss", !FcaEngine.MatchesFix(new FcaProgram { Fixes = new List<string> { "LAIRI" } }, "J6 LAIRX"), "");
        check("alt band", FcaEngine.MatchesAlt(f, 24000) && !FcaEngine.MatchesAlt(f, 9000), "");

        // --- world: line at 77W from 38N to 40N; flights eastbound along 39N to KXYZ at 76W ---
        var w = new World();
        w.Airports["KABC"] = new LatLon(39, -82);
        w.Airports["KXYZ"] = new LatLon(39, -76);
        long now = 1795399500000;

        var a = Air("AAL1", -79);
        var b = Air("BBB2", -79.1);
        var wrongDest = new FcaPilot { Callsign = "OTH4", Pos = new LatLon(39, -79), Alt = 35000, Gs = 420, Hdg = 90, Dep = "KABC", Arr = "KABC", FpAlt = 35000 };
        var excl = Air("EXCL1", -79.5);
        var seq = FcaEngine.Compute(f, new[] { a, b, wrongDest, excl }, w, now);
        eq("airborne members (dest filter, exclusions)", "AAL1,BBB2", string.Join(",", seq.Items.Select(i => i.Pilot.Callsign)));
        var ia = seq.Items[0];
        double expectDist = 2 * 60 * Math.Cos(39 * Math.PI / 180);
        check("distance to line", Math.Abs(ia.DistNm - expectDist) < 1.5, ia.DistNm.ToString());
        check("ETA = dist / gs", Math.Abs(ia.EtaSec - ia.DistNm / 420 * 3600) < 1, ia.EtaSec.ToString());
        eq("airborne scheduled at ETA", ia.EtaSec, ia.SchedSec);
        var ib = seq.Items[1];
        check("too close behind: conflict", ib.Conflict, "");
        double sep = 20.0 / 420 * 3600;
        check("conflict short = sep - gap", Math.Abs(ib.ConflictShortSec - (sep - (ib.EtaSec - ia.EtaSec))) < 1, ib.ConflictShortSec.ToString());
        check("MRP delay = shortfall", Math.Abs(FcaEngine.DelayToAbsorb(ib) - ib.ConflictShortSec) < 1e-6, "");

        // Rate mode: 30/hr = 120 s.
        var rate = FcaProgram.Parse(System.Text.Json.JsonDocument.Parse(Row.Replace("\"mode\":\"mit\"", "\"mode\":\"rate\"")).RootElement[0].GetProperty("data"))!;
        eq("rate separation", 120.0, FcaEngine.SepSeconds(rate, ia));

        // Manual order: BBB2 first, AAL1 chained behind it.
        var man = FcaEngine.Compute(f, new[] { a, b }, w, now, new List<string> { "BBB2", "AAL1" });
        check("manual flag", man.Manual, "");
        eq("manual order", "BBB2,AAL1", string.Join(",", man.Items.Select(i => i.Pilot.Callsign)));
        var m0 = man.Items[0];
        var m1 = man.Items[1];
        check("manual chain: AAL1 at BBB2 + sep", Math.Abs(m1.SchedSec - Math.Max(m1.EtaSec, m0.SchedSec + sep)) < 1, m1.SchedSec.ToString());
        check("manual delay positive", m1.DelaySec > 0, "");

        // Ground departure at KABC: advisory slot after the airborne flights' times.
        var gnd = new FcaPilot { Callsign = "GND5", Pos = new LatLon(39.01, -82), Gs = 0, Dep = "KABC", Arr = "KXYZ", FpAlt = 35000, Tas = 450 };
        var withGnd = FcaEngine.Compute(f, new[] { a, gnd }, w, now);
        var g = withGnd.Items.Single(i => i.Pilot.Callsign == "GND5");
        check("ground candidate", g.Ground, "");
        check("ground ETA = 180 s buffer + climb profile", Math.Abs(g.EtaSec - (180 + FcaEngine.ProfileTransitSec(g.DistNm, 0, 35000, 450))) < 1, g.EtaSec.ToString());
        check("ground slotted clear of airborne", withGnd.Items.Where(i => !i.Ground).All(x => Math.Abs(x.SchedSec - g.SchedSec) >= FcaEngine.SepSeconds(f, g) - 1e-6), "");

        // Frozen release: pinned at its CTA.
        var rel = new FcaPilot { Callsign = "REL1", Pos = new LatLon(39.0, -82.01), Gs = 0, Dep = "KABC", Arr = "KXYZ", FpAlt = 35000, Tas = 450 };
        var withRel = FcaEngine.Compute(f, new[] { rel }, w, now);
        var r = withRel.Items.Single();
        eq("release pinned at CTA", (1795400000000 - now) / 1000.0, r.SchedSec);
        check("release frozen", r.Frozen, "");

        // Scope: only aircraft inside a scoped ARTCC.
        w.Artcc = (id, p) => id == "ZDC" ? p.Lon > -79.05 : false;
        var scoped = FcaProgram.Parse(System.Text.Json.JsonDocument.Parse(Row.Replace("\"scope\":[]", "\"scope\":[\"ZDC\"]")).RootElement[0].GetProperty("data"))!;
        eq("scope filter", "AAL1", string.Join(",", FcaEngine.Compute(scoped, new[] { a, b }, w, now).Items.Select(i => i.Pilot.Callsign)));
        w.Artcc = (_, _) => null;
        eq("scope fails open without boundaries", 2, FcaEngine.Compute(scoped, new[] { a, b }, w, now).Items.Count);

        // Past the line: no crossing ahead.
        eq("already past the line", 0, FcaEngine.Compute(f, new[] { Air("PAST6", -76.5) }, w, now).Items.Count);

        // Route through a fix north of the line still crosses it.
        w.Fixes["NORTH"] = new LatLon(39.8, -78);
        var vf = new FcaPilot { Callsign = "VIA7", Pos = new LatLon(39, -79), Alt = 35000, Gs = 420, Hdg = 60, Dep = "KABC", Arr = "KXYZ", FpAlt = 35000, Route = "DCT NORTH DCT" };
        var vs = FcaEngine.Compute(f, new[] { vf }, w, now).Items.Single();
        check("route via fix is longer than direct", vs.DistNm > expectDist + 3, vs.DistNm.ToString());

        // --- VATFLOW route expansion (airway, STAR transition, preferred route, trim ahead) ---
        var nav = VatflowNav.Parse(
            "{\"bbox\":[23.5,-130,51.5,-63]}",
            "{\"AAAAA\":[[39,-81]],\"BBBBB\":[[39,-80]],\"CCCCC\":[[39,-79]],\"TRANS\":[[39.5,-77.5]],\"STARA\":[[39.2,-76.6]]}",
            "{\"VOR\":[[39,-81.5],[10,10]]}",
            "{\"J99\":{\"t\":\"J\",\"w\":[[\"AAAAA\",39,-81],[\"BBBBB\",39,-80],[\"CCCCC\",39,-79]]}}",
            "{\"STARA1\":{\"type\":\"STAR\",\"apt\":[\"KXYZ\"],\"common\":[[\"STARA\",39.2,-76.6],[\"FINAL\",39.1,-76.2]],\"transitions\":{\"TRANS\":[[\"TRANS\",39.5,-77.5],[\"STARA\",39.2,-76.6]]}}}",
            "{\"KABC|KXYZ\":\"VOR AAAAA\"}");
        string Names(List<RouteAnchor> l) => string.Join(" ", l.Select(x => x.Name));
        eq("airway + STAR transition", "KABC VOR AAAAA BBBBB CCCCC TRANS STARA FINAL KXYZ",
            Names(FcaRoute.BuildAnchors(nav, "KABC", "KXYZ", "VOR AAAAA J99 CCCCC TRANS STARA1", w.Airport)));
        eq("nearest same-named navaid", 39.0, FcaRoute.BuildAnchors(nav, "KABC", "KXYZ", "VOR", w.Airport)[1].Pos.Lat);
        eq("preferred route for a bare plan", "KABC VOR AAAAA KXYZ", Names(FcaRoute.BuildAnchors(nav, "KABC", "KXYZ", "", w.Airport)));
        var full = FcaRoute.BuildAnchors(nav, "KABC", "KXYZ", "VOR AAAAA J99 CCCCC TRANS STARA1", w.Airport);
        eq("trim ahead of an aircraft between BBBBB and CCCCC", "NOW CCCCC TRANS STARA FINAL KXYZ",
            Names(FcaRoute.TrimAhead(full, new LatLon(39, -79.6), 90)));
        check("international stops at the edge of coverage", FcaRoute.IsInternational("EGLL", null, nav) && !FcaRoute.IsInternational("KJFK", null, nav), "");
        var fixFca = new FcaProgram { Fixes = new List<string> { "FINAL" } };
        var navWorld = new World { Airports = w.Airports, Nav = nav };
        check("fix filter matches inside an expanded STAR", FcaEngine.MatchesFix(fixFca, new FcaPilot { Dep = "KABC", Arr = "KXYZ", Route = "TRANS STARA1" }, navWorld), "");
        check("great circle densified", FcaRoute.GreatCircle(new LatLon(40, -74), new LatLon(51, 0)).Count > 10, "");

        // --- re-sequence, DCT format, departure time ---
        eq("resequence", "A,D,B,C,E", string.Join(",", FcaEngine.Resequence(new[] { "A", "B", "C", "D", "E" }, new[] { "D", "B" })));
        eq("DCT SEC", "2:40", FcaEngine.FormatDct(158, true));
        eq("DCT MIN", "6", FcaEngine.FormatDct(370, false));
        eq("DCT negative", "-1", FcaEngine.FormatDct(-60, false));
        eq("DCT zero", "0", FcaEngine.FormatDct(0, false));
        var nowMs = new DateTimeOffset(2026, 11, 22, 23, 50, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        eq("deptime after midnight rolls forward", new DateTimeOffset(2026, 11, 23, 0, 10, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), FcaEngine.DepTimeMs("0010", nowMs));
    }
}
