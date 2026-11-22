using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CrcCompanion.Core;

/// <summary>Tests for the vUSAlink hub monitor client behind the CPDLC views.</summary>
internal static class CpdlcTests
{
    private const double T0 = 1795313000;   // 2026-11-19 21:23:20Z

    private static string Msg(long id, double ts, string kind, string frm, string to, string msg) =>
        "{\"id\":" + id + ",\"ts\":" + ts.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        ",\"kind\":\"" + kind + "\",\"frm\":\"" + frm + "\",\"to\":\"" + to + "\",\"msg\":\"" + msg + "\"}";

    private static string Reply(string airspace, long evt, string[] connected, params string[] msgs) =>
        "{\"ok\":true,\"airspace\":\"" + airspace + "\",\"flights\":[],\"connected\":[" +
        string.Join(",", connected.Select(c => "\"" + c + "\"")) + "],\"messages\":[" + string.Join(",", msgs) + "],\"evtId\":" + evt + "}";

    public static void Run(Action<string, bool, string> check, Action<string, object?, object?> eq)
    {
        // --- airspace names ---
        eq("airspace ZDC -> KZDC", "KZDC", CpdlcHub.Airspace("zdc"));
        eq("airspace KZDC kept", "KZDC", CpdlcHub.Airspace("KZDC"));
        eq("clean @fields@", "MONITOR UNICOM 122.800", CpdlcHub.CleanText("monitor @UNICOM@ @122.800@"));

        // --- parsing ---
        var json = Reply("KZJX", 2186, new[] { "AAL423" },
            Msg(2178, T0, "dn", "AAL423", "", "WILCO"),
            Msg(2182, T0 + 60, "up", "KZJX", "AAL423", "MONITOR @UNICOM@ @122.800@"),
            Msg(2183, T0 + 80, "dn", "AAL423", "", "WILCO"),
            Msg(2185, T0 + 90, "up", "KZJX", "AAL423", "END SERVICE"),
            Msg(2186, T0 + 100, "rq", "DAL5", "", "REQUEST LOGON"),
            Msg(2187, T0 + 100, "xx", "DAL5", "", "junk"));
        var r = CpdlcHub.ParseMonitor(json)!;
        eq("parse airspace", "KZJX", r.Airspace);
        eq("parse evtId", 2186L, r.EventId);
        eq("parse skips unknown kind", 5, r.Events.Count);
        eq("uplink callsign is addressee", "AAL423", r.Events[1].Callsign);
        eq("request callsign is sender", "DAL5", r.Events[4].Callsign);
        eq("request kind", CpdlcKind.Request, r.Events[4].Kind);
        eq("event time", DateTime.UnixEpoch.AddSeconds(T0 + 60), r.Events[1].Utc);
        check("connected parsed", r.Connected.Contains("aal423"), "");
        eq("not ok -> null", null, CpdlcHub.ParseMonitor("{\"ok\":false,\"error\":\"x\"}"));

        // --- reply codes ---
        eq("WILCO", "WILCO", CpdlcLog.ReplyCode("WILCO"));
        eq("STANDBY", "STBY", CpdlcLog.ReplyCode("STANDBY"));
        eq("UNABLE DUE TO WEATHER", "UNABLE", CpdlcLog.ReplyCode("UNABLE DUE TO WEATHER"));
        eq("request isn't a reply", null, CpdlcLog.ReplyCode("REQUEST CLIMB TO FL380"));
        check("hub note expects no reply", CpdlcLog.ExpectsNoReply("ATC COMMS TERMINATED."), "");
        check("END SERVICE ROGER/ACCEPT expects reply", !CpdlcLog.ExpectsNoReply("END SERVICE. CPDLC SERVICES TERMINATED. ROGER/ACCEPT THIS MESSAGE."), "");

        // --- log: merge, uplinks, outstanding ---
        var log = new CpdlcLog();
        log.Reset("KZJX");
        log.Merge(r, DateTime.UnixEpoch.AddSeconds(T0 + 120));
        eq("merged count", 5, log.Count);
        eq("last id", 2186L, log.LastId);
        var other = CpdlcHub.ParseMonitor(Reply("KZDC", 9999, new string[0], Msg(9999, T0, "up", "KZDC", "X1", "HI")))!;
        log.Merge(other, DateTime.UtcNow);
        eq("other airspace ignored", 5, log.Count);
        log.Merge(r, DateTime.UnixEpoch.AddSeconds(T0 + 120));
        eq("re-merge doesn't duplicate", 5, log.Count);
        eq("history newest first", 2186L, log.History()[0].Id);
        eq("history filter", "2185,2183,2182,2178", string.Join(",", log.History(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aal423" }).Select(e => e.Id)));

        var ups = log.Uplinks();
        eq("uplinks", "2185,2182", string.Join(",", ups.Select(u => u.Uplink.Id)));
        eq("MONITOR answered WILCO", "WILCO", ups[1].Code);
        check("END SERVICE needs no reply", ups[0].NoReplyExpected && !ups[0].Open, "");

        // Two uplinks, answered in order; STANDBY keeps the first open; an old open one drops off.
        var now = DateTime.UnixEpoch.AddSeconds(T0 + 400);
        var log2 = new CpdlcLog();
        log2.Reset("KZDC");
        log2.Merge(CpdlcHub.ParseMonitor(Reply("KZDC", 8, new[] { "UAL1" },
            Msg(1, T0 - 3000, "up", "KZDC", "OLD1", "CLIMB TO @FL350@"),
            Msg(2, T0, "up", "KZDC", "UAL1", "CLIMB TO @FL380@"),
            Msg(3, T0 + 10, "up", "KZDC", "UAL1", "PROCEED DIRECT TO @LUNNI@"),
            Msg(4, T0 + 20, "dn", "UAL1", "", "STANDBY"),
            Msg(5, T0 + 30, "dn", "UAL1", "", "WILCO"),
            Msg(6, T0 + 300, "up", "KZDC", "DAL2", "CONTACT @JAX CENTER@ @132.600@"),
            Msg(7, T0 + 310, "dn", "DAL2", "", "UNABLE"),
            Msg(8, T0 + 320, "dn", "AAL3", "", "REQUEST CLIMB TO FL400")))!, now);
        var u2 = log2.Uplinks().ToDictionary(u => u.Uplink.Id);
        eq("first uplink gets WILCO after STANDBY", "WILCO", u2[2].Code);
        check("second uplink still open", u2[3].Open, "");
        eq("DAL2 UNABLE", "UNABLE", u2[6].Code);
        eq("outstanding (old one abandoned)", "3", string.Join(",", log2.Outstanding(now).Select(u => u.Uplink.Id)));

        var adv = log2.Advisories(now);
        eq("advisories", "REQ AAL3,UNABLE DAL2,LATE UAL1", string.Join(",", adv.Select(a => a.Type + " " + a.Event.Callsign)));
        eq("acknowledged advisory hidden", 2, log2.Advisories(now, new HashSet<long> { 8 }).Count);
        var logon = new CpdlcLog();
        logon.Reset("KZJX");
        logon.Merge(CpdlcHub.ParseMonitor(Reply("KZJX", 1, new string[0], Msg(1, T0, "rq", "N1", "", "REQUEST LOGON")))!, DateTime.UnixEpoch.AddSeconds(T0 + 30));
        eq("logon advisory", "LOGON", logon.Advisories(DateTime.UnixEpoch.AddSeconds(T0 + 30)).Single().Type);
        check("logon advisory gone once connected", log.Advisories(DateTime.UnixEpoch.AddSeconds(T0 + 120)).All(a => a.Type != "LOGON" || a.Event.Callsign != "AAL423"), "");

        // --- transfer of communication ---
        eq("TOC freq", "132.600", CpdlcLog.TransferFrequency("CONTACT JAX CENTER 132.600"));
        eq("TOC unit", "JAX CENTER", CpdlcLog.TransferUnit("CONTACT JAX CENTER 132.600"));
        eq("TOC freq none", "", CpdlcLog.TransferFrequency("CONTACT UNICOM"));
        var toc = log2.LastTransfers();
        eq("last transfer DAL2", 6L, toc["DAL2"].Uplink.Id);
        check("no transfer for UAL1", !toc.ContainsKey("UAL1"), "");

        // --- ERAM HISTORY: closed messages and their STAT ---
        eq("history of log2 (open ones left out)", "6:UNA,2:WIL",
            string.Join(",", log2.ClosedHistory().Select(h => h.Message.Id + ":" + h.StatText)));
        var log3 = new CpdlcLog();
        log3.Reset("KZDC");
        log3.Merge(CpdlcHub.ParseMonitor(Reply("KZDC", 14, new[] { "N1", "N2" },
            Msg(1, T0, "up", "KZDC", "N1", "CLIMB TO @FL300@"),
            Msg(2, T0 + 5, "dn", "N1", "", "UNABLE DUE TO WEATHER"),
            Msg(3, T0 + 10, "dn", "N1", "", "REQUEST DIRECT TO HAR"),
            Msg(4, T0 + 20, "up", "KZDC", "N1", "PROCEED DIRECT TO @HAR@"),
            Msg(5, T0 + 25, "dn", "N1", "", "WILCO"),
            Msg(6, T0 + 30, "dn", "N1", "", "REQUEST FL400"),
            Msg(7, T0 + 40, "up", "KZDC", "N1", "UNABLE. REVERT TO VOICE."),
            Msg(8, T0 + 50, "up", "KZDC", "N1", "ARE YOU ABLE FL360"),
            Msg(9, T0 + 55, "dn", "N1", "", "AFFIRM"),
            Msg(10, T0 + 60, "up", "KZDC", "N1", "CROSS HAR AT FL300"),
            Msg(11, T0 + 65, "dn", "N1", "", "REQUEST LOWER"),
            Msg(12, T0 + 70, "up", "KZDC", "N1", "ATC COMMS TERMINATED."),
            Msg(13, T0 + 80, "rq", "N2", "", "REQUEST LOGON"),
            Msg(14, T0 + 81, "up", "KZDC", "N2", "LOGON ACCEPTED"),
            Msg(15, T0 + 90, "up", "KZDC", "N2", "END SERVICE")))!, DateTime.UnixEpoch.AddSeconds(T0 + 100));
        var hist = log3.ClosedHistory();
        eq("history stats newest first",
            "14:NRR,13:APRV,12:NRR,11:CLSD*,10:CLSD*,8:RCVD,7:NRR,6:UNA*,4:WIL,3:APRV,1:UNA*",
            string.Join(",", hist.Select(h => h.Message.Id + ":" + h.StatText)));
        eq("PIDs marked", "13,11,6,3", string.Join(",", hist.Where(h => h.Pid).Select(h => h.Message.Id)));
        eq("UNA reason from pilot", "DUE TO WEATHER", hist.Single(h => h.Message.Id == 1).Reason);
        eq("UNA reason from our uplink", "REVERT TO VOICE", hist.Single(h => h.Message.Id == 6).Reason);
        eq("CLSD reason", "SESSION TERMINATED", hist.Single(h => h.Message.Id == 10).Reason);
        eq("plain UNABLE has no asterisk", "UNA", CpdlcLog.UnableReason("UNABLE.") == "" ? "UNA" : "UNA*");

        // --- ERAM MESSAGE OUT ---
        var mo = log3.MessageOut(DateTime.UnixEpoch.AddSeconds(T0 + 100));
        eq("message out stats", "14:NRR,12:NRR,10:CLSD*,8:RCVD,7:NRR,4:WIL,1:UNA*",
            string.Join(",", mo.Select(r => r.Uplink.Id + ":" + r.StatText)));
        eq("special area = UNA", "1", string.Join(",", mo.Where(r => r.Special).Select(r => r.Uplink.Id)));
        var mo2 = log2.MessageOut(now);
        eq("TIM after 120 s, old uplink out of window", "6:UNA,3:TIM,2:WIL",
            string.Join(",", mo2.Select(r => r.Uplink.Id + ":" + r.Stat)));
        check("TIM is special and open", mo2.Single(r => r.Uplink.Id == 3).Special && !mo2.Single(r => r.Uplink.Id == 3).Closed, "");
        eq("SNT before 120 s", "SNT", log2.MessageOut(DateTime.UnixEpoch.AddSeconds(T0 + 60)).Single(r => r.Uplink.Id == 3).Stat);
        eq("SNT at 119 s", "SNT", log2.MessageOut(DateTime.UnixEpoch.AddSeconds(T0 + 129)).Single(r => r.Uplink.Id == 3).Stat);
        eq("TIM at 120 s", "TIM", log2.MessageOut(DateTime.UnixEpoch.AddSeconds(T0 + 130)).Single(r => r.Uplink.Id == 3).Stat);

        // --- MSG CONTENT short forms and truncation ---
        eq("climb", "↑380", CpdlcText.Abbreviate("CLIMB TO AND MAINTAIN FL380"));
        eq("descend feet", "↓090", CpdlcText.Abbreviate("DESCEND AND MAINTAIN 9000"));
        eq("maintain", "350", CpdlcText.Abbreviate("MAINTAIN FL350"));
        eq("direct", "DRCT LUNNI", CpdlcText.Abbreviate("PROCEED DIRECT TO LUNNI"));
        eq("block unchanged", "CLIMB TO AND MAINTAIN BLOCK FL350 TO FL370", CpdlcText.Abbreviate("CLIMB TO AND MAINTAIN BLOCK FL350 TO FL370"));
        eq("other unchanged", "CONTACT JAX CENTER 132.600", CpdlcText.Abbreviate("CONTACT JAX CENTER 132.600"));
        eq("truncate with *", "CLRD DEV 10NM L WA*", CpdlcText.Truncate("CLRD DEV 10NM L WAYPOINT", 19));
        eq("short padded", "DRCT HAR  ", CpdlcText.Truncate("DRCT HAR", 10));

        // Hub restart: ids start over, log restarts.
        log2.Merge(CpdlcHub.ParseMonitor(Reply("KZDC", 2, new string[0], Msg(2, T0 + 500, "up", "KZDC", "N9", "HI")))!, now);
        eq("hub restart resets log", 1, log2.Count);
    }
}
