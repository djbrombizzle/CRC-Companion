using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace CrcCompanion.Core
{
    public enum CpdlcKind { Up, Down, Request }

    /// <summary>One CPDLC message from the vUSAlink hub's log (an uplink, a pilot downlink or a logon request).</summary>
    public sealed class CpdlcEvent
    {
        public long Id { get; init; }
        public DateTime Utc { get; init; }
        public CpdlcKind Kind { get; init; }
        /// <summary>The aircraft (the uplink's addressee, or the downlink's sender).</summary>
        public string Callsign { get; init; } = "";
        /// <summary>The message as the pilot sees it, with the hub's @field@ markers removed.</summary>
        public string Text { get; init; } = "";

        public string KindLabel => Kind == CpdlcKind.Up ? "UP" : Kind == CpdlcKind.Down ? "DN" : "RQ";
    }

    /// <summary>
    /// Client side of vUSAlink's KUSA hub (github.com/djbrombizzle/vUSAlink-hub). The companion reads the hub's
    /// view-only <c>/hub/monitor</c> feed: no sign-in, so it never competes with the vUSAlink EDST for the
    /// controller's one hub session, and it never sends anything to an aircraft.
    /// </summary>
    public static class CpdlcHub
    {
        public const string DefaultUrl = "https://web-production-3d9fe.up.railway.app";

        /// <summary>Hub airspace for a vatspy FIR: "KZDC" stays, "ZDC" → "KZDC".</summary>
        public static string Airspace(string fir)
        {
            fir = (fir ?? "").Trim().ToUpperInvariant();
            return fir.Length == 3 && fir[0] == 'Z' ? "K" + fir : fir;
        }

        public sealed class MonitorReply
        {
            public string Airspace { get; init; } = "";
            public List<CpdlcEvent> Events { get; } = new List<CpdlcEvent>();
            public HashSet<string> Connected { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public long EventId { get; init; }
        }

        /// <summary>Parses a /hub/monitor reply. Returns null when it isn't ok.</summary>
        public static MonitorReply? ParseMonitor(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) return null;
            var reply = new MonitorReply
            {
                Airspace = Str(root, "airspace"),
                EventId = root.TryGetProperty("evtId", out var e) && e.TryGetInt64(out var eid) ? eid : 0,
            };
            if (root.TryGetProperty("connected", out var conn) && conn.ValueKind == JsonValueKind.Array)
                foreach (var c in conn.EnumerateArray())
                    if (c.ValueKind == JsonValueKind.String) reply.Connected.Add(c.GetString()!);
            if (root.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
                foreach (var m in msgs.EnumerateArray())
                {
                    var ev = ParseEvent(m);
                    if (ev != null) reply.Events.Add(ev);
                }
            return reply;
        }

        private static CpdlcEvent? ParseEvent(JsonElement m)
        {
            if (m.ValueKind != JsonValueKind.Object) return null;
            if (!m.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id)) return null;
            var kind = Str(m, "kind") switch
            {
                "up" => CpdlcKind.Up,
                "dn" => CpdlcKind.Down,
                "rq" => CpdlcKind.Request,
                _ => (CpdlcKind?)null,
            };
            if (kind == null) return null;
            double ts = m.TryGetProperty("ts", out var t) && t.TryGetDouble(out var tv) ? tv : 0;
            var cs = kind == CpdlcKind.Up ? Str(m, "to") : Str(m, "frm");
            if (cs.Length == 0) return null;
            return new CpdlcEvent
            {
                Id = id,
                Utc = DateTime.UnixEpoch.AddSeconds(ts),
                Kind = kind.Value,
                Callsign = cs.ToUpperInvariant(),
                Text = CleanText(Str(m, "msg")),
            };
        }

        /// <summary>"MONITOR @UNICOM@ @122.800@" → "MONITOR UNICOM 122.800".</summary>
        public static string CleanText(string s)
        {
            s = (s ?? "").Replace("@", " ").Replace('\n', ' ').Replace('\r', ' ').ToUpperInvariant();
            return string.Join(" ", s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static string Str(JsonElement o, string name) =>
            o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }

    /// <summary>What became of an uplink.</summary>
    public sealed class UplinkStatus
    {
        public CpdlcEvent Uplink { get; init; } = null!;
        /// <summary>The pilot's reply, or null while it's open.</summary>
        public CpdlcEvent? Reply { get; init; }
        /// <summary>Short reply code: WILCO, UNABLE, ROGER, STBY, AFFIRM, NEG, or "" while open.</summary>
        public string Code { get; init; } = "";
        /// <summary>No reply is expected (hub status notes such as LOGON ACCEPTED).</summary>
        public bool NoReplyExpected { get; init; }
        public bool Open => Reply == null && !NoReplyExpected;
    }

    /// <summary>Something the controller should look at (CPDLC ADV).</summary>
    public sealed class CpdlcAdvisory
    {
        public CpdlcEvent Event { get; init; } = null!;
        /// <summary>LOGON, REQ (a pilot request or free text), UNABLE or LATE (an uplink with no reply).</summary>
        public string Type { get; init; } = "";
    }

    /// <summary>One uplink in the ERAM MESSAGE OUT view.</summary>
    public sealed class MsgOutRow
    {
        public CpdlcEvent Uplink { get; init; } = null!;
        /// <summary>SNT, STBY, WIL, ROG, RCVD, NRR, CLSD (normal area); UNA, TIM (special area).</summary>
        public string Stat { get; init; } = "";
        public string Reason { get; init; } = "";
        /// <summary>An error for the special area, which stays until acknowledged.</summary>
        public bool Special { get; init; }
        /// <summary>Answered or otherwise finished (drawn dimmed in the normal area, removed by CLEANUP).</summary>
        public bool Closed { get; init; }

        public string StatText => Reason.Length > 0 && (Stat == "UNA" || Stat == "CLSD") ? Stat + "*" : Stat;
    }

    /// <summary>ERAM-style short forms for the MSG CONTENT column.</summary>
    public static class CpdlcText
    {
        private static readonly System.Text.RegularExpressions.Regex AltRe = new System.Text.RegularExpressions.Regex(
            @"^(CLIMB|DESCEND)?( TO)?( AND)?( ?MAINTAIN)?( BLOCK)? (FL(\d{3})|(\d{3,5})( FT)?)\.?$");
        private static readonly System.Text.RegularExpressions.Regex DirectRe = new System.Text.RegularExpressions.Regex(
            @"^PROCEED DIRECT TO ([A-Z0-9]{2,5})\.?$");

        /// <summary>
        /// "CLIMB TO AND MAINTAIN FL380" → "↑380", "DESCEND AND MAINTAIN 9000" → "↓090", "MAINTAIN FL350" → "350",
        /// "PROCEED DIRECT TO LUNNI" → "DRCT LUNNI". Anything else is returned unchanged.
        /// </summary>
        public static string Abbreviate(string text)
        {
            var t = (text ?? "").Trim();
            var m = AltRe.Match(t);
            if (m.Success && (m.Groups[1].Success || m.Groups[4].Success) && !m.Groups[5].Success)
            {
                int hundreds = m.Groups[7].Success
                    ? int.Parse(m.Groups[7].Value, CultureInfo.InvariantCulture)
                    : int.Parse(m.Groups[8].Value, CultureInfo.InvariantCulture) / 100;
                var arrow = m.Groups[1].Value == "CLIMB" ? "↑" : m.Groups[1].Value == "DESCEND" ? "↓" : "";
                return arrow + hundreds.ToString("000", CultureInfo.InvariantCulture);
            }
            var d = DirectRe.Match(t);
            if (d.Success) return "DRCT " + d.Groups[1].Value;
            return t;
        }

        /// <summary>Fits a column: text longer than <paramref name="width"/> is cut and ends with "*", as in ERAM.</summary>
        public static string Truncate(string text, int width)
        {
            text ??= "";
            if (width <= 0) return "";
            return text.Length <= width ? text.PadRight(width) : text.Substring(0, width - 1) + "*";
        }

        public static bool IsTruncated(string text, int width) => (text ?? "").Length > width;
    }

    /// <summary>One closed message in the ERAM HISTORY view.</summary>
    public sealed class HistoryRow
    {
        public CpdlcEvent Message { get; init; } = null!;
        /// <summary>Pilot-initiated downlink (drawn with a down arrow between TIME and ORG).</summary>
        public bool Pid { get; init; }
        /// <summary>WIL, ROG, UNA, RCVD, NRR, CLSD (uplinks); APRV, UNA, ROG, CLSD (PIDs).</summary>
        public string Stat { get; init; } = "";
        /// <summary>Due To Reason (shown as "*" after the status), or "".</summary>
        public string Reason { get; init; } = "";
        public DateTime ClosedUtc { get; init; }

        /// <summary>STAT as displayed: UNA and CLSD with a reason get the Due To Reason indicator.</summary>
        public string StatText => Reason.Length > 0 && (Stat == "UNA" || Stat == "CLSD") ? Stat + "*" : Stat;
    }

    /// <summary>
    /// The CPDLC log for one airspace, merged across polls (the hub keeps only its last 60 messages per
    /// airspace, so the companion keeps its own longer history while it runs).
    /// </summary>
    public sealed class CpdlcLog
    {
        public const int MaxEvents = 500;
        /// <summary>An open uplink older than this is LATE.</summary>
        public static readonly TimeSpan LateAfter = TimeSpan.FromMinutes(2);
        /// <summary>Advisories older than this drop off CPDLC ADV.</summary>
        public static readonly TimeSpan AdvisoryWindow = TimeSpan.FromMinutes(30);

        private readonly SortedDictionary<long, CpdlcEvent> _events = new SortedDictionary<long, CpdlcEvent>();

        public string Airspace { get; private set; } = "";
        public HashSet<string> Connected { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Highest event id seen, for the next poll's "since".</summary>
        public long LastId { get; private set; }
        public DateTime? LastPollUtc { get; private set; }
        /// <summary>Last poll error, or "" when the hub answered.</summary>
        public string Error { get; private set; } = "";

        public int Count => _events.Count;

        /// <summary>Oldest first.</summary>
        public IEnumerable<CpdlcEvent> Events => _events.Values;

        /// <summary>Starts over for a new airspace.</summary>
        public void Reset(string airspace)
        {
            Airspace = airspace;
            _events.Clear();
            Connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            LastId = 0;
            LastPollUtc = null;
            Error = "";
        }

        public void Merge(CpdlcHub.MonitorReply reply, DateTime nowUtc)
        {
            if (!string.Equals(reply.Airspace, Airspace, StringComparison.OrdinalIgnoreCase)) return;
            // A hub restart starts its ids over: drop the old log so new ids can't collide with it.
            if (reply.EventId < LastId) { _events.Clear(); LastId = 0; }
            foreach (var e in reply.Events) _events[e.Id] = e;
            while (_events.Count > MaxEvents) _events.Remove(_events.Keys.First());
            Connected = reply.Connected;
            LastId = Math.Max(LastId, reply.EventId);
            LastPollUtc = nowUtc;
            Error = "";
        }

        public void Fail(string error) => Error = error;

        /// <summary>Newest first, optionally for some aircraft only.</summary>
        public List<CpdlcEvent> History(ICollection<string>? callsigns = null) =>
            _events.Values.Reverse().Where(e => callsigns == null || callsigns.Count == 0 || callsigns.Contains(e.Callsign)).ToList();

        private static readonly (string Word, string Code)[] Replies =
        {
            ("WILCO", "WILCO"), ("UNABLE", "UNABLE"), ("ROGER", "ROGER"), ("STANDBY", "STBY"),
            ("AFFIRM", "AFFIRM"), ("NEGATIVE", "NEG"), ("ACCEPT", "ROGER"),
        };

        /// <summary>WILCO / UNABLE / ... for a downlink that answers an uplink, else null (a request or free text).</summary>
        public static string? ReplyCode(string text)
        {
            var t = (text ?? "").Trim().TrimEnd('.');
            foreach (var (w, c) in Replies)
                if (t == w || t.StartsWith(w + " ", StringComparison.Ordinal) || t.StartsWith(w + ".", StringComparison.Ordinal)) return c;
            return null;
        }

        /// <summary>Hub notes and messages that close a dialogue: no pilot reply is expected.</summary>
        public static bool ExpectsNoReply(string text)
        {
            var t = (text ?? "").Trim();
            if (t.Contains("ROGER/ACCEPT", StringComparison.Ordinal)) return false;
            return t == "LOGON ACCEPTED" || t == "FORCE LOGOFF" || t == "END SERVICE" ||
                   t.StartsWith("ATC COMMS", StringComparison.Ordinal) ||
                   t.StartsWith("CPDLC NOT IN USE", StringComparison.Ordinal) ||
                   t.StartsWith("UNABLE", StringComparison.Ordinal) ||
                   t.StartsWith("REQUEST RECEIVED", StringComparison.Ordinal);
        }

        /// <summary>
        /// Every uplink with its reply, newest first. The monitor feed has no message reference numbers, so a
        /// pilot's answer is matched to the oldest unanswered uplink to that aircraft (pilots answer in order).
        /// STANDBY marks it but leaves it open for the real answer.
        /// </summary>
        public List<UplinkStatus> Uplinks()
        {
            var queue = new Dictionary<string, List<CpdlcEvent>>(StringComparer.OrdinalIgnoreCase);
            var replies = new Dictionary<long, (CpdlcEvent Reply, string Code)>();
            foreach (var e in _events.Values)
            {
                if (e.Kind == CpdlcKind.Up)
                {
                    if (ExpectsNoReply(e.Text)) continue;
                    if (!queue.TryGetValue(e.Callsign, out var q)) queue[e.Callsign] = q = new List<CpdlcEvent>();
                    q.Add(e);
                }
                else if (e.Kind == CpdlcKind.Down && queue.TryGetValue(e.Callsign, out var q) && q.Count > 0)
                {
                    var code = ReplyCode(e.Text);
                    if (code == null) continue;
                    // STANDBY goes to the oldest uplink not already on standby, if any.
                    var target = code == "STBY" ? q.FirstOrDefault(u => !replies.ContainsKey(u.Id)) ?? q[0] : q[0];
                    replies[target.Id] = (e, code);
                    if (code != "STBY") q.Remove(target);
                }
            }
            var result = new List<UplinkStatus>();
            foreach (var e in _events.Values.Reverse())
            {
                if (e.Kind != CpdlcKind.Up) continue;
                if (!replies.TryGetValue(e.Id, out var r))
                    result.Add(new UplinkStatus { Uplink = e, NoReplyExpected = ExpectsNoReply(e.Text) });
                else if (r.Code == "STBY")
                    result.Add(new UplinkStatus { Uplink = e, Code = "STBY" });
                else
                    result.Add(new UplinkStatus { Uplink = e, Reply = r.Reply, Code = r.Code });
            }
            return result;
        }

        /// <summary>
        /// Uplinks still waiting for the pilot's answer (STANDBY counts as waiting), newest first. After
        /// <see cref="AdvisoryWindow"/> an unanswered uplink is treated as abandoned.
        /// </summary>
        public List<UplinkStatus> Outstanding(DateTime nowUtc) =>
            Uplinks().Where(u => u.Open && nowUtc - u.Uplink.Utc <= AdvisoryWindow).ToList();

        /// <summary>
        /// CPDLC ADV: logon requests and pilot requests not yet answered by an uplink, UNABLE replies, and uplinks
        /// with no answer after <see cref="LateAfter"/>. Newest first, within <see cref="AdvisoryWindow"/>.
        /// </summary>
        public List<CpdlcAdvisory> Advisories(DateTime nowUtc, ICollection<long>? acknowledged = null)
        {
            var list = new List<CpdlcAdvisory>();
            var lastUp = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _events.Values)
                if (e.Kind == CpdlcKind.Up) lastUp[e.Callsign] = e.Utc;
            bool AnsweredAfter(CpdlcEvent e) => lastUp.TryGetValue(e.Callsign, out var t) && t >= e.Utc;

            foreach (var e in _events.Values)
            {
                if (nowUtc - e.Utc > AdvisoryWindow) continue;
                if (e.Kind == CpdlcKind.Request && !AnsweredAfter(e) && !Connected.Contains(e.Callsign))
                    list.Add(new CpdlcAdvisory { Event = e, Type = "LOGON" });
                else if (e.Kind == CpdlcKind.Down)
                {
                    var code = ReplyCode(e.Text);
                    if (code == "UNABLE") list.Add(new CpdlcAdvisory { Event = e, Type = "UNABLE" });
                    else if (code == null && !AnsweredAfter(e)) list.Add(new CpdlcAdvisory { Event = e, Type = "REQ" });
                }
            }
            foreach (var u in Outstanding(nowUtc))
                if (nowUtc - u.Uplink.Utc >= LateAfter)
                    list.Add(new CpdlcAdvisory { Event = u.Uplink, Type = "LATE" });
            return list.Where(a => acknowledged == null || !acknowledged.Contains(a.Event.Id))
                       .OrderByDescending(a => a.Event.Utc).ThenByDescending(a => a.Event.Id).ToList();
        }

        /// <summary>Hub notes that mark the end of an aircraft's CPDLC session: anything still open is system closed.</summary>
        public static bool EndsSession(string text)
        {
            var t = (text ?? "").Trim();
            return t.StartsWith("END SERVICE", StringComparison.Ordinal) || t == "FORCE LOGOFF" ||
                   t.StartsWith("ATC COMMS TERMINATED", StringComparison.Ordinal);
        }

        /// <summary>Hub action notes that duplicate the real uplink (END SERVICE is also sent as its full text).</summary>
        private static bool IsHubNote(string text) => text == "END SERVICE" || text == "FORCE LOGOFF";

        /// <summary>ERAM STAT for an uplink closed by the pilot's reply.</summary>
        private static string UplinkStat(string code) => code switch
        {
            "WILCO" => "WIL",
            "ROGER" => "ROG",
            "UNABLE" => "UNA",
            _ => "RCVD",   // AFFIRM / NEGATIVE: a response was received
        };

        /// <summary>"UNABLE DUE TO WEATHER" → "DUE TO WEATHER"; "UNABLE." → "".</summary>
        public static string UnableReason(string text)
        {
            var t = (text ?? "").Trim();
            if (!t.StartsWith("UNABLE", StringComparison.Ordinal)) return "";
            return t.Substring(6).Trim().Trim('.', ',').Trim();
        }

        /// <summary>
        /// ERAM Message History: closed messages only, newest first. Uplinks show the pilot's answer
        /// (WIL, ROG, UNA, RCVD), NRR when no answer is expected, or CLSD when the session ended first.
        /// Pilot-initiated downlinks (PIDs, including logon requests) show how they were closed: APRV (answered
        /// with an uplink), UNA (answered UNABLE), ROG (answered ROGER) or CLSD. UNA with a reason is "UNA*".
        /// Open messages (no answer yet) are left out; they're in MSGOUT and ADV.
        /// </summary>
        public List<HistoryRow> ClosedHistory()
        {
            var events = _events.Values.ToList();
            var rows = new List<HistoryRow>();
            var replies = Uplinks().ToDictionary(u => u.Uplink.Id);

            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                // The first later uplink to this aircraft, and the first later session end.
                CpdlcEvent? nextUp = null, end = null;
                for (int j = i + 1; j < events.Count && (nextUp == null || end == null); j++)
                {
                    var n = events[j];
                    if (n.Kind != CpdlcKind.Up || !string.Equals(n.Callsign, e.Callsign, StringComparison.OrdinalIgnoreCase)) continue;
                    if (nextUp == null && !IsHubNote(n.Text)) nextUp = n;
                    if (end == null && EndsSession(n.Text)) end = n;
                }

                if (e.Kind == CpdlcKind.Up)
                {
                    if (IsHubNote(e.Text)) continue;
                    var u = replies[e.Id];
                    if (u.Reply != null)
                    {
                        var reason = u.Code == "UNABLE" ? UnableReason(u.Reply.Text) : "";
                        rows.Add(new HistoryRow { Message = e, Stat = UplinkStat(u.Code), Reason = reason, ClosedUtc = u.Reply.Utc });
                    }
                    else if (u.NoReplyExpected)
                        rows.Add(new HistoryRow { Message = e, Stat = "NRR", ClosedUtc = e.Utc });
                    else if (end != null)
                        rows.Add(new HistoryRow { Message = e, Stat = "CLSD", Reason = "SESSION TERMINATED", ClosedUtc = end.Utc });
                }
                else if (e.Kind == CpdlcKind.Request || (e.Kind == CpdlcKind.Down && ReplyCode(e.Text) == null))
                {
                    if (nextUp == null) continue;   // still open
                    var t = nextUp.Text;
                    string stat; string reason = "";
                    if (EndsSession(t)) { stat = "CLSD"; reason = "SESSION TERMINATED"; }
                    else if (t.StartsWith("UNABLE", StringComparison.Ordinal)) { stat = "UNA"; reason = UnableReason(t); }
                    else if (t == "ROGER" || t.StartsWith("ROGER ", StringComparison.Ordinal) || t.StartsWith("ROGER.", StringComparison.Ordinal)) stat = "ROG";
                    else stat = "APRV";
                    rows.Add(new HistoryRow { Message = e, Pid = true, Stat = stat, Reason = reason, ClosedUtc = nextUp.Utc });
                }
            }
            rows.Reverse();
            return rows;
        }

        /// <summary>An uplink with no pilot reply after this long is timed out (TIM), as in the vUSAlink EDST.</summary>
        public static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(120);

        /// <summary>
        /// ERAM Message Out: this session's uplinks (last <see cref="AdvisoryWindow"/>), newest first.
        /// Special area: UNA (pilot answered UNABLE; UNA* with a reason) and TIM (no reply after 120 s).
        /// Normal area: SNT (sent, waiting), STBY (pilot said STANDBY), and closed ones: WIL, ROG, RCVD, NRR
        /// (no reply expected) and CLSD (session ended first).
        /// </summary>
        public List<MsgOutRow> MessageOut(DateTime nowUtc)
        {
            var events = _events.Values.ToList();
            var replies = Uplinks().ToDictionary(u => u.Uplink.Id);
            var rows = new List<MsgOutRow>();
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e.Kind != CpdlcKind.Up || IsHubNote(e.Text) || nowUtc - e.Utc > AdvisoryWindow) continue;
                var u = replies[e.Id];
                bool ended = false;
                for (int j = i + 1; j < events.Count && !ended; j++)
                    ended = events[j].Kind == CpdlcKind.Up && EndsSession(events[j].Text) &&
                            string.Equals(events[j].Callsign, e.Callsign, StringComparison.OrdinalIgnoreCase);

                MsgOutRow row;
                if (u.Reply != null && u.Code == "UNABLE")
                    row = new MsgOutRow { Uplink = e, Stat = "UNA", Reason = UnableReason(u.Reply.Text), Special = true, Closed = true };
                else if (u.Reply != null)
                    row = new MsgOutRow { Uplink = e, Stat = UplinkStat(u.Code), Closed = true };
                else if (u.NoReplyExpected)
                    row = new MsgOutRow { Uplink = e, Stat = "NRR", Closed = true };
                else if (ended)
                    row = new MsgOutRow { Uplink = e, Stat = "CLSD", Reason = "SESSION TERMINATED", Closed = true };
                else if (u.Code == "STBY")
                    row = new MsgOutRow { Uplink = e, Stat = "STBY" };
                else if (nowUtc - e.Utc >= ReplyTimeout)
                    row = new MsgOutRow { Uplink = e, Stat = "TIM", Special = true };
                else
                    row = new MsgOutRow { Uplink = e, Stat = "SNT" };
                rows.Add(row);
            }
            rows.Reverse();
            return rows;
        }

        /// <summary>Last CONTACT / MONITOR uplink per aircraft (transfer of communication), with its reply.</summary>
        public Dictionary<string, UplinkStatus> LastTransfers()
        {
            var d = new Dictionary<string, UplinkStatus>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in Uplinks())   // newest first: keep the first seen
                if (IsTransfer(u.Uplink.Text) && !d.ContainsKey(u.Uplink.Callsign)) d[u.Uplink.Callsign] = u;
            return d;
        }

        public static bool IsTransfer(string text)
        {
            var t = (text ?? "").Trim();
            return t.StartsWith("CONTACT ", StringComparison.Ordinal) || t.StartsWith("MONITOR ", StringComparison.Ordinal);
        }

        /// <summary>The frequency in a CONTACT / MONITOR message ("CONTACT JAX CENTER 132.600" → "132.600").</summary>
        public static string TransferFrequency(string text)
        {
            foreach (var w in (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Reverse())
                if (w.Length >= 5 && w.Contains('.') && double.TryParse(w.TrimEnd('.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f >= 100 && f < 140)
                    return f.ToString("0.000", CultureInfo.InvariantCulture);
            return "";
        }

        /// <summary>The unit in a CONTACT / MONITOR message ("CONTACT JAX CENTER 132.600" → "JAX CENTER").</summary>
        public static string TransferUnit(string text)
        {
            var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (words.Count < 2) return "";
            words.RemoveAt(0);
            var freq = TransferFrequency(text ?? "");
            words.RemoveAll(w => freq.Length > 0 &&
                                 double.TryParse(w.TrimEnd('.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) &&
                                 f.ToString("0.000", CultureInfo.InvariantCulture) == freq);
            return string.Join(" ", words);
        }
    }
}
