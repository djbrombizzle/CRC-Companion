using System;
using System.Collections.Generic;
using System.Linq;
using CrcCompanion.Core;

/// <summary>Tests for the Update Area: GI notes, VATFLOW Airport TMU, FCA changes, the queue and the EDST CID book.</summary>
internal static class UaTests
{
    public static void Run(Action<string, bool, string> check, Action<string, object?, object?> eq)
    {
        var now = new DateTime(2026, 11, 22, 18, 0, 0, DateTimeKind.Utc);

        // --- GI through the hub monitor ---
        var monitor = @"{""ok"":true,""airspace"":""KZDC"",""flights"":[],""connected"":[],""messages"":[],""evtId"":5,
            ""gi"":[{""id"":7,""ts"":1795370400,""from"":""KZNY"",""fromShort"":""ZNY"",""cid"":""1"",""callsign"":""ny_ctr"",""text"":""jfk arrivals 20 mit""},
                    {""id"":8,""ts"":1795370460,""from"":""KZDC"",""fromShort"":""ZDC"",""cid"":""2"",""callsign"":""DC_CTR"",""text"":""  ""}],""giId"":8}";
        var reply = CpdlcHub.ParseMonitor(monitor)!;
        check("gi field seen", reply.HasGi, "");
        eq("blank GI dropped", 1, reply.Gi.Count);
        eq("GI text", "ZNY NY_CTR JFK ARRIVALS 20 MIT", reply.Gi[0].ToUa().Text);
        eq("GI key", "GI:7", reply.Gi[0].ToUa().Key);
        check("no gi field on old hubs", !CpdlcHub.ParseMonitor(@"{""ok"":true,""airspace"":""KZDC"",""messages"":[]}")!.HasGi, "");

        var log = new CpdlcLog();
        log.Reset("KZDC");
        log.Merge(reply, now);
        var first = log.TakeNewGi(out bool backlog);
        check("first GI batch is backlog", backlog && first.Count == 1, "");
        eq("GI cursor", 7L, log.LastGiId);
        var reply2 = CpdlcHub.ParseMonitor(@"{""ok"":true,""airspace"":""KZDC"",""messages"":[],""evtId"":5,""gi"":[{""id"":9,""ts"":1795370500,""fromShort"":""ZDC"",""callsign"":""DC_CTR"",""text"":""hi""}],""giId"":9}")!;
        log.Merge(reply2, now);
        var second = log.TakeNewGi(out backlog);
        check("later GI is not backlog", !backlog && second.Count == 1 && second[0].Id == 9, "");

        // --- VATFLOW Airport TMU (vatflow-hub "state" message, shaped like the live hub's) ---
        var state = @"{""type"":""state"",""edcts"":{},
            ""rates"":{""KDCA"":{""aar"":32,""trail"":0,""mit"":0,""gates"":[{""name"":""CAPPS"",""trail"":0,""mit"":20},{""name"":""CLIPR"",""trail"":0,""mit"":35},{""name"":""X"",""mit"":0}],""jetsOnly"":true},
                       ""KLAX"":{""aar"":68,""mit"":0,""gates"":[]},
                       ""KBWI"":{""aar"":0}},
            ""restrictions"":{""rmvvjjv4o3tkz"":{""id"":""rmvvjjv4o3tkz"",""requesting"":""DO1"",""providing"":""ZDV"",""restriction"":""SSKII, TBARR, "",""start"":"""",""stop"":"""",""flashUntil"":0},
                              ""r2"":{""id"":""r2"",""requesting"":""ZNY"",""providing"":""ZDC"",""restriction"":""jfk via all 20mit"",""start"":""1700"",""stop"":""2100Z""},
                              ""r3"":{""id"":""r3"",""requesting"":""ZOB"",""providing"":""ZNY"",""restriction"":""IAD VIA ALL 15MIT""}},
            ""groundStops"":{""gs1"":{""id"":""gs1"",""airport"":""KIAD"",""scope"":""zny zbw"",""until"":""1930"",""updated"":"""",""flashUntil"":0}}}";
        var tmis = VatflowTmu.Parse(state)!;
        eq("tmi count (no-AAR rate dropped)", 6, tmis.Count);
        eq("ground stop text", "GROUND STOP KIAD UNTIL 1930 SCOPE ZNY ZBW", tmis.Single(t => t.Kind == "GS").Text);
        eq("restriction text, trailing comma trimmed", "SSKII, TBARR DO1:ZDV", tmis.Single(t => t.Id == "rmvvjjv4o3tkz").Text);
        eq("restriction with times", "JFK VIA ALL 20MIT ZNY:ZDC 1700-2100", tmis.Single(t => t.Id == "r2").Text);
        eq("rate with gate MITs", "ARR RATE KDCA AAR 32 CAPPS 20MIT CLIPR 35MIT", tmis.Single(t => t.Id == "KDCA").Text);
        bool Local(string a) => a == "KIAD" || a == "KDCA" || a == "KBWI";
        var zdc = tmis.Where(t => VatflowTmu.Concerns(t, "ZDC", Local)).Select(t => t.Kind + ":" + t.Id).OrderBy(x => x).ToList();
        eq("ZDC concerns", "GS:gs1,RATE:KDCA,RESTR:r2,RESTR:r3", string.Join(",", zdc));
        check("FAA id matches ICAO airport", VatflowTmu.Concerns(new TmiItem { Kind = "GS", Id = "x", Airports = { "IAD" } }, "ZDC", Local), "");
        eq("hhmm iso", "1700", VatflowTmu.Hhmm("2026-11-22T17:00:00.123456789Z"));
        eq("hhmm colon", "1830", VatflowTmu.Hhmm("18:30"));
        check("other hub messages ignored", VatflowTmu.Parse(@"{""type"":""authok"",""ok"":true}") == null, "");

        // --- change tracker ---
        var tr = new UaTracker("TMU");
        var a1 = tr.Update(new[] { ("GS:1", "k1", "GROUND STOP IAD") }, now);
        eq("first snapshot is new", "GROUND STOP IAD", a1.Single().Text);
        eq("unchanged: nothing", 0, tr.Update(new[] { ("GS:1", "k1", "GROUND STOP IAD") }, now).Count);
        eq("changed: amend", "AMEND GROUND STOP IAD UNTIL 1900", tr.Update(new[] { ("GS:1", "k2", "GROUND STOP IAD UNTIL 1900") }, now).Single().Text);
        eq("gone: cancel", "CANCEL GROUND STOP IAD UNTIL 1900", tr.Update(Array.Empty<(string, string, string)>(), now).Single().Text);

        var fcas = new[]
        {
            new FcaProgram { Id = "f1", Name = "", Artcc = "ZDC", Mode = "mit", Mit = 35, Fixes = { "DCA" }, Enabled = true },
            new FcaProgram { Id = "f2", Name = "", Artcc = "ZDC", Mode = "mit", Mit = 20, Fixes = { "BWI" }, Enabled = false },
        };
        var fi = UaTracker.FcaItems(fcas).ToList();
        eq("only enabled FCAs", 1, fi.Count);
        check("FCA text", fi[0].Text.StartsWith("FCA "), fi[0].Text);

        // --- queue ---
        var q = new UaQueue(new[] { "old" });
        int changes = 0;
        q.Changed += () => changes++;
        q.Add(new[] { M("a", now), M("old", now), M("b", now.AddMinutes(1)) });
        eq("acked key not queued", 2, q.Waiting.Count);
        q.Add(new[] { M("a", now) });
        eq("same key not queued twice", 2, q.Waiting.Count);
        eq("acknowledge oldest", "a", q.AcknowledgeNext()!.Key);
        eq("shown newest first", "a", q.Shown[0].Key);
        q.AcknowledgeNext();
        eq("text area newest first", "b,a", string.Join(",", q.Shown.Select(m => m.Key)));
        check("acked keys saved", q.AckedKeys.Contains("a") && q.AckedKeys.Contains("b") && q.AckedKeys.Contains("old"), "");
        q.Add(new[] { M("c", now) }, silent: true);
        check("silent goes straight to text", q.Waiting.Count == 0 && q.Shown[0].Key == "c", "");
        q.Delete("a");
        eq("delete", "c,b", string.Join(",", q.Shown.Select(m => m.Key)));
        check("changed fired", changes > 0, "");
        eq("display", "1800 GI X", new UaMessage { Utc = now, Source = "GI", Text = "X" }.Display);

        // --- EDST CID book (vUSALink App cids.json) ---
        var book = EdstCidBook.Parse(@"[{""cid"":""151"",""callsign"":""skw5646"",""sector"":""32"",""logon"":""x"",""verified"":true,""at"":""2026-11-22T17:00:00Z"",""lastSeen"":""2026-11-22T17:05:00Z""},
                                         {""cid"":"""",""callsign"":""DAL1""}]");
        eq("cid entries", 1, book.Count);
        var e = book["SKW5646"];
        eq("cid", "151", e.Cid);
        eq("sector", "32", e.Sector);
        check("verified", e.Verified, "");
        eq("not json array", 0, EdstCidBook.Parse("{}").Count);

        // --- QF readouts (the app renews "at" on each QF) -> CFR ---
        eq("QF time read as UTC", new DateTime(2026, 11, 22, 17, 0, 0, DateTimeKind.Utc), e.At);
        var qfBook = new EdstCidBook();
        qfBook.Replace(EdstCidBook.Parse(@"[
            {""cid"":""101"",""callsign"":""AAL1"",""at"":""2026-11-22T17:00:00.0000000Z"",""lastSeen"":""2026-11-22T17:20:00Z""},
            {""cid"":""102"",""callsign"":""DAL2"",""at"":""2026-11-22T17:05:00.0000000Z""},
            {""cid"":""103"",""callsign"":""UAL3"",""at"":""2026-11-22T17:03:00.0000000Z""}]"));
        var since = new DateTime(2026, 11, 22, 17, 1, 0, DateTimeKind.Utc);
        eq("QFs after a time, oldest first", "UAL3,DAL2", string.Join(",", qfBook.QfSince(since).Select(x => x.Callsign)));
        eq("lastSeen updates don't count as a QF", 0, qfBook.QfSince(new DateTime(2026, 11, 22, 17, 5, 0, DateTimeKind.Utc)).Count);
        eq("latest QF", new DateTime(2026, 11, 22, 17, 5, 0, DateTimeKind.Utc), qfBook.LatestQf);
        eq("missing time never counts", 0, EdstCidBook.Parse(@"[{""cid"":""1"",""callsign"":""X1""}]").Values.Count(x => x.At > DateTime.MinValue));
    }

    private static UaMessage M(string key, DateTime t) => new UaMessage { Key = key, Utc = t, Source = "GI", Text = key };
}
