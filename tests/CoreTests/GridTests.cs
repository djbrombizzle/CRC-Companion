using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CrcCompanion.Core;

/// <summary>Tests for the logic behind the VIEWS grid views (CRR, CODE, CONFLCT ALERT, HOLD LIST, ...).</summary>
internal static class GridTests
{
    private static FacilityContext Ctx()
    {
        var box = new Boundary("KTST", new[] { (IReadOnlyList<LatLon>)new[] { new LatLon(37, -80), new LatLon(37, -75), new LatLon(40, -75), new LatLon(40, -80), new LatLon(37, -80) } });
        return new FacilityContext
        {
            Fir = "KTST",
            Boundary = box,
            Airports = new Dictionary<string, MapAirport>(StringComparer.OrdinalIgnoreCase)
            {
                ["KIAD"] = new MapAirport { Icao = "KIAD", Display = "IAD", Pos = new LatLon(38.9445, -77.4558), Fir = "KTST" },
                ["KDCA"] = new MapAirport { Icao = "KDCA", Display = "DCA", Pos = new LatLon(38.8521, -77.0377), Fir = "KTST" },
            },
        };
    }

    private static Pilot P(string cs, double lat, double lon, int alt, int gs, int hdg, string sq = "1200", FlightPlan? plan = null) =>
        new Pilot { Callsign = cs, Pos = new LatLon(lat, lon), Altitude = alt, Groundspeed = gs, Heading = hdg, Squawk = sq, Plan = plan };

    private static FeedSnapshot Snap(DateTime t, params Pilot[] pilots)
    {
        var s = new FeedSnapshot { UpdatedUtc = t };
        s.Pilots.AddRange(pilots);
        return s;
    }

    public static void Run(Action<string, bool, string> check, Action<string, object?, object?> eq)
    {
        var t0 = new DateTime(2026, 11, 22, 15, 0, 0, DateTimeKind.Utc);
        var ctx = Ctx();

        // --- Locations ---
        Func<string, LatLon?> look = id => id == "BOS" ? new LatLon(42.3656, -71.0096) : id == "DCA" ? new LatLon(38.8521, -77.0377) : (LatLon?)null;
        var ll = Locations.Parse("//4220N/7110W", look)!.Value;
        eq("latlon lat", 42.3333, Math.Round(ll.Pos.Lat, 4));
        eq("latlon lon", -71.1667, Math.Round(ll.Pos.Lon, 4));
        eq("latlon kind", Locations.Kind.LatLon, ll.Kind);
        eq("fix kind", Locations.Kind.Fix, Locations.Parse("BOS", look)!.Value.Kind);
        var frd = Locations.Parse("BOS090010", look)!.Value;
        eq("frd kind", Locations.Kind.Frd, frd.Kind);
        eq("frd distance 10", 10.0, Math.Round(Geo.DistanceNm(new LatLon(42.3656, -71.0096), frd.Pos), 1));
        check("unknown location", Locations.Parse("ZZZZZ", look) == null, "");
        eq("compass NW", "NW", Locations.Compass(315));
        eq("compass N wraps", "N", Locations.Compass(359));

        // --- NavDatabase (synthetic protobuf) ---
        byte[] Pos(double lat, double lon) => Msg(F64(1, lat), F64(2, lon));
        var bytes = Concat(
            Field(1, Msg(Str(1, "IAD"), Str(2, "KIAD"), Str(3, "ZDC"), Str(4, "DULLES"), Field(6, Pos(38.9445, -77.4558)))),
            Field(2, Msg(Str(1, "BAMMY"), Field(2, Pos(38.5, -77.0)))),
            Field(2, Msg(Str(1, "BAMMY"), Field(2, Pos(10.0, 10.0)))),
            Field(3, Msg(Str(1, "J75"), Str(2, "BAMMY"))));
        var nav = NavDatabase.Parse(bytes);
        eq("nav fix count", 2, nav.FixCount);
        eq("nav airport by icao", 38.9445, nav.Find("KIAD", ctx.Boundary.Center)!.Value.Lat);
        eq("nav airport by faa id", true, nav.IsAirport("IAD"));
        eq("nav duplicate fix picks nearest", 38.5, nav.Find("BAMMY", ctx.Boundary.Center)!.Value.Lat);
        eq("nav nearest fix", "BAMMY", nav.NearestFix(new LatLon(38.52, -77.02), 10)?.Id);
        check("nav nearest fix out of range", nav.NearestFix(new LatLon(30, -77), 10) == null, "");

        var real = NavDatabase.TryLoad(NavDatabase.DefaultPath());
        if (real != null)
        {
            check("CRC NavData.dat: fixes loaded", real.FixCount > 10000, real.FixCount.ToString());
            check("CRC NavData.dat: airports loaded", real.AirportCount > 1000, real.AirportCount.ToString());
            var iad = real.Find("IAD", ctx.Boundary.Center);
            check("CRC NavData.dat: IAD near Dulles", iad != null && Geo.DistanceNm(iad.Value, new LatLon(38.9445, -77.4558)) < 5, iad?.ToString() ?? "null");
        }

        // --- Beacon codes ---
        check("code valid", BeaconCodes.IsCode("1200"), "");
        check("code 8 invalid", !BeaconCodes.IsCode("1280"), "");
        check("group block", BeaconCodes.IsGroup("12"), "");
        check("group range", BeaconCodes.IsGroup("4000-4077"), "");
        check("group backwards range invalid", !BeaconCodes.IsGroup("4077-4000"), "");
        check("block matches", BeaconCodes.Matches("12", "1277"), "");
        check("block no match", !BeaconCodes.Matches("12", "1300"), "");
        check("range matches (octal)", BeaconCodes.Matches("4000-4077", "4070"), "");
        check("range no match", !BeaconCodes.Matches("4000-4077", "4100"), "");
        eq("code view numeric order", "1200,2201.,3302", string.Join(",", BeaconCodes.CodeViewEntries(new[] { "3302", "1200" }, new[] { "2201" }, false)));
        eq("code view SORT MAN", "2201.,1200,3302", string.Join(",", BeaconCodes.CodeViewEntries(new[] { "3302", "1200", "2201" }, new[] { "2201" }, true)));
        eq("code view dedups manual+tracked", 1, BeaconCodes.CodeViewEntries(new[] { "2201" }, new[] { "2201" }, false).Count);

        // --- Entry lists ---
        var items = new List<string>();
        eq("entry add", "ADD AAL1 DAL2", EntryList.Apply(items, "aal1 DAL2", EntryList.Callsign));
        eq("entry remove + invalid", "DEL AAL1  INVALID 1X", EntryList.Apply(items, "-AAL1 1X", EntryList.Callsign));
        eq("entry list", "DAL2", string.Join(",", items));
        var groups = new List<string>();
        EntryList.Apply(groups, "4000-4077 12 -12", g => BeaconCodes.IsGroup(g) ? g : null);
        eq("group entry: range kept, block added then removed", "4000-4077", string.Join(",", groups));

        // --- CRR ---
        var online = new HashSet<string> { "AAL1", "DAL2", "UAL3" };
        var crr = new List<CrrGroup>();
        Func<string, (LatLon, Locations.Kind, string)?> resolve = x => Locations.Parse(x, look);
        eq("crr create fix group", "ADD GROUP BOS", CrrCommands.Apply(crr, "BOS", resolve, online.Contains));
        eq("crr add aircraft", "ADD AAL1 DAL2", CrrCommands.Apply(crr, "BOS AAL1/DAL2", resolve, online.Contains));
        eq("crr toggle removes", "DEL AAL1  NOT FOUND ZZZ9", CrrCommands.Apply(crr, "BOS AAL1 ZZZ9", resolve, online.Contains));
        eq("crr latlon needs label", "LABEL REQUIRED", CrrCommands.Apply(crr, "//4220N/7110W", resolve, online.Contains));
        eq("crr latlon with label + aircraft", "ADD GROUP NORTH  ADD UAL3", CrrCommands.Apply(crr, "//4220N/7110W NORTH UAL3", resolve, online.Contains));
        eq("crr frd label", "ADD GROUP E10", CrrCommands.Apply(crr, "BOS090010 E10", resolve, online.Contains));
        eq("crr groups", "BOS,NORTH,E10", string.Join(",", crr.Select(g => g.Label)));
        eq("crr delete group", "DEL GROUP E10", CrrCommands.Apply(crr, "-E10", resolve, online.Contains));
        eq("crr bad location", "LOCATION NOT FOUND XYZZY", CrrCommands.Apply(crr, "XYZZY", resolve, online.Contains));
        eq("crr fix group label from fix, aircraft follows", "DAL2", string.Join(",", crr[0].Aircraft));

        // --- Conflict probe ---
        Func<string, double?> noVs = _ => null;
        // Head-on at the same altitude, 20 NM apart, 450 kt each: closes in ~1.3 min.
        var head = Snap(t0,
            P("AAL1", 38.0, -78.0, 35000, 450, 90),
            P("DAL2", 38.0, -78.0 + 20 / (60 * Math.Cos(38 * Math.PI / 180)), 35000, 450, 270),
            P("UAL3", 38.0, -78.0, 37000, 450, 90),        // 2,000 ft above AAL1: separated
            P("N12", 39.5, -76.5, 4500, 120, 0));
        var cf = Conflicts.Find(head, ctx, noVs);
        eq("conflict pair found", "AAL1 DAL2", cf.Count == 1 ? cf[0].A + " " + cf[0].B : string.Join(";", cf.Select(c => c.A + c.B)));
        check("conflict predicted, not now", cf.Count == 1 && cf[0].Minutes > 0 && cf[0].Minutes <= 2, cf.Count == 1 ? cf[0].Minutes.ToString() : "");
        check("conflict text E2", cf.Count == 1 && cf[0].Text.EndsWith("E2"), cf.Count == 1 ? cf[0].Text : "");
        var now2 = Snap(t0, P("AAL1", 38.0, -78.0, 20000, 300, 0), P("DAL2", 38.02, -78.0, 20500, 300, 180));
        var cn = Conflicts.Find(now2, ctx, noVs);
        check("conflict now", cn.Count == 1 && cn[0].Minutes == 0 && cn[0].Text.EndsWith("NOW"), cn.Count == 1 ? cn[0].Text : cn.Count.ToString());
        eq("3 NM setting ignores 4 NM pair", 0,
            Conflicts.Find(Snap(t0, P("AAL1", 38.0, -78.0, 20000, 300, 0), P("DAL2", 38.0, -78.0 + 4 / (60 * Math.Cos(38 * Math.PI / 180)), 20000, 300, 0)), ctx, noVs, 3).Count);
        eq("both near same airport skipped", 0,
            Conflicts.Find(Snap(t0, P("AAL1", 38.95, -77.46, 2000, 150, 10), P("DAL2", 38.96, -77.45, 2200, 150, 10)), ctx, noVs).Count);
        eq("both outside facility skipped", 0,
            Conflicts.Find(Snap(t0, P("AAL1", 45.0, -90.0, 20000, 300, 0), P("DAL2", 45.01, -90.0, 20000, 300, 0)), ctx, noVs).Count);
        // Climbing into another aircraft's altitude: vertical rate from history makes it a conflict.
        var climb = Snap(t0, P("AAL1", 38.0, -78.0, 18000, 300, 90), P("DAL2", 38.0, -77.9, 21000, 300, 90));
        eq("level: separated vertically", 0, Conflicts.Find(climb, ctx, noVs).Count);
        eq("climbing 3000 fpm: conflict", 1, Conflicts.Find(climb, ctx, cs => cs == "AAL1" ? 3000 : 0).Count);

        // --- Flight tracker: events ---
        var tr = new FlightTracker();
        var plan = new FlightPlan { Type = "A320", Departure = "KIAD", Arrival = "KBOS", AltitudeHundreds = 340, Route = "JCOBY4 JCOBY" };
        tr.Update(Snap(t0, P("JBU5", 38.945, -77.456, 300, 0, 10, "6605", plan), P("AAL1", 38.0, -75.1, 35000, 450, 270)), ctx);
        eq("baseline: no events", 0, tr.Events.Count);
        var plan2 = new FlightPlan { Type = "A320", Departure = "KIAD", Arrival = "KBOS", AltitudeHundreds = 360, Route = "JCOBY4 JCOBY" };
        tr.Update(Snap(t0.AddSeconds(15), P("JBU5", 38.95, -77.45, 1500, 160, 10, "6605", plan2), P("AAL1", 38.0, -74.9, 35000, 450, 90),
            P("N99", 38.5, -77.0, 3000, 100, 0)), ctx);
        var types = string.Join(",", tr.Events.Select(e => e.Type + " " + e.Callsign).Reverse());
        eq("events: DEP, AMND, EXIT, LGON", "DEP JBU5,AMND JBU5,EXIT AAL1,LGON N99", types);
        eq("DEP text", "1500 DEP  JBU5     IAD BOS", tr.Events.Last().Text);
        tr.Update(Snap(t0.AddSeconds(15), P("JBU5", 38.95, -77.45, 1500, 160, 10)), ctx);
        eq("same poll twice ignored", 4, tr.Events.Count);
        tr.Update(Snap(t0.AddSeconds(30), P("JBU5", 38.96, -77.44, 2500, 170, 10, "6605", plan2)), ctx);
        eq("logoff", "LGOF N99", tr.Events[0].Type + " " + tr.Events[0].Callsign);
        tr.Update(Snap(t0.AddSeconds(45), P("JBU5", 38.97, -77.43, 3000, 180, 10, "1234", plan2)), ctx);
        eq("code change", "CODE JBU5 6605-1234", tr.Events[0].Type + " " + tr.Events[0].Callsign + " " + tr.Events[0].Info);
        tr.ClearEvents();
        eq("clear", 0, tr.Events.Count);

        // --- Flight tracker: vertical rate and holding ---
        var ht = new FlightTracker();
        var c = new LatLon(38.5, -77.0);
        int hdg = 0;
        for (int i = 0; i <= 40; i++)   // 10 minutes of 15 s samples, turning 18°/sample around a point
        {
            var pos = Geo.Move(c, hdg - 90, 3);
            ht.Update(Snap(t0.AddSeconds(15 * i), P("HLD1", pos.Lat, pos.Lon, 12000, 220, hdg), P("CLB1", 38.0, -78.0 + i * 0.02, 10000 + i * 500, 300, 90)), ctx);
            hdg = (hdg + 18) % 360;
        }
        check("holding detected", ht.IsHolding("HLD1"), "");
        check("straight flight not holding", !ht.IsHolding("CLB1"), "");
        var vs = ht.VerticalRateFpm("CLB1");
        check("vertical rate ~2000 fpm", vs != null && Math.Abs(vs.Value - 2000) < 1, vs?.ToString() ?? "null");
        check("hold center near orbit center", ht.Holds.Any(h => Geo.DistanceNm(h.Center, c) < 2), "");
        eq("angle diff wraps", -20.0, FlightTracker.AngleDiff(10, 350));

        // --- Metering ---
        var mp = new NamedPoint { Name = "IAD", Lat = 38.9445, Lon = -77.4558, IsAirport = true };
        var toIad = new FlightPlan { Type = "B738", Arrival = "KIAD", Route = "GVE LDN" };
        var ms = Snap(t0,
            P("FAR", 38.9445, -79.4558, 30000, 480, 90, "1", toIad),     // ~93 NM west, heading east
            P("NEAR", 38.9445, -78.4558, 20000, 300, 90, "2", toIad),    // ~47 NM west
            P("AWAY", 38.9445, -78.9558, 20000, 300, 270, "3", toIad),   // heading away
            P("OTHR", 38.9445, -78.0, 20000, 300, 90, "4", new FlightPlan { Arrival = "KDCA", Route = "LDN" }));
        var mlist = Metering.For(mp, ms, 250, a => a == "KIAD");
        eq("meter airport order", "NEAR,FAR", string.Join(",", mlist.Select(m => m.Callsign)));
        var fixMp = new NamedPoint { Name = "LDN", Lat = 39.07, Lon = -77.56 };
        eq("meter fix by route", "FAR,NEAR,OTHR", string.Join(",", Metering.For(fixMp, ms, 250, _ => false).Select(m => m.Callsign).OrderBy(x => x)));

        // --- Unassociated ---
        var ua = Snap(t0, P("N123AB", 39.0, -77.2, 4500, 120, 180), P("AAL1", 38.5, -77.5, 35000, 450, 90, "2201", plan), P("N5", 45, -90, 3000, 100, 0));
        eq("UA: no-plan targets inside", "N123AB", string.Join(",", Unassociated.Find(ua, ctx).Select(p => p.Callsign)));
        eq("UA position text", "10 NE IAD", Unassociated.Where(new LatLon(39.07, -77.30), ctx));

        // --- Profile: grid view settings round-trip ---
        var dir = Path.Combine(Path.GetTempPath(), "crc-companion-grid-" + Guid.NewGuid().ToString("N"));
        var store = new ProfileStore(dir);
        var prof = new Profile { Name = "ZDC" };
        prof.ExtraFor("CRR").Groups.Add(new CrrGroup { Label = "BOS", Location = "BOS", Lat = 42.3, Lon = -71.0, Aircraft = { "AAL1" } });
        prof.ExtraFor("CODE").Items.Add("1200");
        prof.ExtraFor("CONFLCT ALERT").SepNm = 99;
        prof.ExtraFor("MRP LIST").Points.Add(new NamedPoint { Name = "IAD", Lat = 38.9, Lon = -77.4, IsAirport = true });
        store.Save(prof);
        var back = store.Load("ZDC");
        eq("grid: CRR group round-trip", "BOS:AAL1", string.Join(",", back.ExtraFor("CRR").Groups.Select(g => g.Label + ":" + string.Join("/", g.Aircraft))));
        eq("grid: key kept as button label", true, back.Extra.ContainsKey("CONFLCT ALERT"));
        eq("grid: codes kept (not normalized as stations)", "1200", string.Join(",", back.ExtraFor("CODE").Items));
        eq("grid: sep clamped", Limits.SepMax, back.ExtraFor("CONFLCT ALERT").SepNm);
        eq("grid: MRP point", true, back.ExtraFor("MRP LIST").Points.Single().IsAirport);
        File.WriteAllText(Path.Combine(store.ProfilesDir, "OLD.json"), "{ \"name\": \"OLD\" }");
        eq("grid: old profile loads with no extras", 0, store.Load("OLD").Extra.Count);
        Directory.Delete(dir, true);
    }

    // ---- tiny protobuf writer for the NavDatabase test ----
    private static byte[] Varint(ulong v)
    {
        var l = new List<byte>();
        do { byte b = (byte)(v & 0x7F); v >>= 7; if (v != 0) b |= 0x80; l.Add(b); } while (v != 0);
        return l.ToArray();
    }
    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static byte[] Field(int f, byte[] payload) => Concat(Varint((ulong)(f << 3 | 2)), Varint((ulong)payload.Length), payload);
    private static byte[] Str(int f, string s) => Field(f, System.Text.Encoding.UTF8.GetBytes(s));
    private static byte[] F64(int f, double d) => Concat(Varint((ulong)(f << 3 | 1)), BitConverter.GetBytes(d));
    private static byte[] Msg(params byte[][] fields) => Concat(fields);
}
