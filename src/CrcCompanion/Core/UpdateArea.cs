using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace CrcCompanion.Core
{
    /// <summary>One Update Area message.</summary>
    public sealed class UaMessage
    {
        /// <summary>Identity: the same key is never queued twice ("GI:41", "TMI:GS:abc:1F2E", "FCA:id:ON:1A").</summary>
        public string Key { get; init; } = "";
        public DateTime Utc { get; init; }
        /// <summary>"GI", "TMU" or "FLOW".</summary>
        public string Source { get; init; } = "";
        public string Text { get; init; } = "";

        /// <summary>The message as shown in the text area: "1815 GI ZNY N90_APP RUNWAY 4 IN USE".</summary>
        public string Display => Utc.ToString("HHmm", CultureInfo.InvariantCulture) + " " + Source + " " + Text;
    }

    /// <summary>A GI (General Information) note from the vUSAlink EDST, via the hub.</summary>
    public sealed class GiMessage
    {
        public long Id { get; init; }
        public DateTime Utc { get; init; }
        /// <summary>Sending ARTCC, short form ("ZNY").</summary>
        public string From { get; init; } = "";
        public string Callsign { get; init; } = "";
        public string Text { get; init; } = "";

        /// <summary>Parses the hub's "gi" array (absent on hubs that don't send GI to the monitor).</summary>
        public static List<GiMessage> ParseArray(JsonElement arr)
        {
            var list = new List<GiMessage>();
            if (arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var m in arr.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object || !m.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id)) continue;
                double ts = m.TryGetProperty("ts", out var t) && t.TryGetDouble(out var tv) ? tv : 0;
                var from = Str(m, "fromShort");
                if (from.Length == 0) from = Str(m, "from");
                if (from.Length == 4 && from[0] == 'K') from = from.Substring(1);
                var text = Str(m, "text").Trim();
                if (text.Length == 0) continue;
                list.Add(new GiMessage
                {
                    Id = id,
                    Utc = DateTime.UnixEpoch.AddSeconds(ts),
                    From = from.ToUpperInvariant(),
                    Callsign = Str(m, "callsign").ToUpperInvariant(),
                    Text = text.ToUpperInvariant(),
                });
            }
            return list;
        }

        public UaMessage ToUa() => new UaMessage
        {
            Key = "GI:" + Id.ToString(CultureInfo.InvariantCulture),
            Utc = Utc,
            Source = "GI",
            Text = (From + " " + Callsign).Trim() + " " + Text,
        };

        internal static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? "",
                JsonValueKind.Number => v.GetRawText(),
                _ => "",
            } : "";
    }

    /// <summary>An active traffic management initiative from VATFLOW's Airport TMU.</summary>
    public sealed class TmiItem
    {
        /// <summary>"GS" ground stop, "RESTR" restriction, "RATE" airport arrival rate program.</summary>
        public string Kind { get; init; } = "";
        public string Id { get; init; } = "";
        /// <summary>The message text without the CANCEL / AMEND prefix.</summary>
        public string Text { get; init; } = "";
        /// <summary>Airports named (for filtering to a facility).</summary>
        public List<string> Airports { get; } = new List<string>();
        /// <summary>Facilities named (requesting / providing).</summary>
        public List<string> Facilities { get; } = new List<string>();

        /// <summary>Stable key for this item's current content.</summary>
        public string StateKey => "TMI:" + Kind + ":" + Id + ":" + Hash(Text);

        internal static string Hash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (var c in s) h = (h ^ c) * 16777619;
                return h.ToString("X8", CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// VATFLOW Airport TMU state, as vatflow-hub sends it to every WebSocket client:
    /// {type:"state", edcts, rates:{ICAO:{aar, mit, trail, gates:[{name, mit, trail}]}},
    ///  restrictions:{id:{requesting, providing, restriction, start, stop}}, groundStops:{id:{airport, scope, until}}}.
    /// </summary>
    public static class VatflowTmu
    {
        /// <summary>Parses a hub message. Null when it isn't a "state" message.</summary>
        public static List<TmiItem>? Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || GiMessage.Str(root, "type") != "state") return null;
            var list = new List<TmiItem>();

            foreach (var (id, g) in Map(root, "groundStops"))
            {
                var apt = S(g, "airport").ToUpperInvariant();
                if (apt.Length == 0) continue;
                var scope = S(g, "scope").Trim().ToUpperInvariant();
                var until = Hhmm(S(g, "until"));
                var t = new TmiItem
                {
                    Kind = "GS", Id = S(g, "id").Length > 0 ? S(g, "id") : id,
                    Text = "GROUND STOP " + apt + (until.Length > 0 ? " UNTIL " + until : "") + (scope.Length > 0 ? " SCOPE " + scope : ""),
                };
                t.Airports.Add(apt);
                list.Add(t);
            }
            foreach (var (id, r) in Map(root, "restrictions"))
            {
                var req = S(r, "requesting").Trim().ToUpperInvariant();
                var prov = S(r, "providing").Trim().ToUpperInvariant();
                var text = S(r, "restriction").Trim().TrimEnd(',', ' ').ToUpperInvariant();
                if (text.Length == 0) continue;
                var t = new TmiItem
                {
                    Kind = "RESTR", Id = S(r, "id").Length > 0 ? S(r, "id") : id,
                    Text = text + (req.Length + prov.Length > 0 ? " " + req + ":" + prov : "") + Span(S(r, "start"), S(r, "stop")),
                };
                if (req.Length > 0) t.Facilities.Add(req);
                if (prov.Length > 0) t.Facilities.Add(prov);
                foreach (var tok in text.Split(new[] { ' ', '/', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    if ((tok.Length == 3 || tok.Length == 4) && tok.All(char.IsLetterOrDigit) && char.IsLetter(tok[0])) t.Airports.Add(tok);
                list.Add(t);
            }
            foreach (var (apt0, p) in Map(root, "rates"))
            {
                var apt = apt0.ToUpperInvariant();
                int aar = Int(p, "aar");
                if (aar <= 0) continue;   // VATFLOW drops programs with no AAR too
                int mit = Int(p, "mit");
                var gates = new List<string>();
                if (p.TryGetProperty("gates", out var ga) && ga.ValueKind == JsonValueKind.Array)
                    foreach (var g in ga.EnumerateArray())
                    {
                        var name = S(g, "name").Trim().ToUpperInvariant();
                        int gm = Int(g, "mit");
                        if (name.Length > 0 && gm > 0) gates.Add(name + " " + gm + "MIT");
                    }
                var t = new TmiItem
                {
                    Kind = "RATE", Id = apt,
                    Text = "ARR RATE " + apt + " AAR " + aar + (mit > 0 ? " " + mit + "MIT" : "") + (gates.Count > 0 ? " " + string.Join(" ", gates) : ""),
                };
                t.Airports.Add(apt);
                list.Add(t);
            }
            return list;
        }

        /// <summary>
        /// Whether a TMI concerns an ARTCC: it names one of the ARTCC's airports (ICAO or FAA id), or the ARTCC
        /// requests or provides it.
        /// </summary>
        public static bool Concerns(TmiItem t, string artcc, Func<string, bool> isLocalAirport)
        {
            if (artcc.Length > 0 && t.Facilities.Any(f => string.Equals(f, artcc, StringComparison.OrdinalIgnoreCase))) return true;
            return t.Airports.Any(a => isLocalAirport(a) || (a.Length == 3 && isLocalAirport("K" + a)));
        }

        private static IEnumerable<(string Key, JsonElement Value)> Map(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var o) || o.ValueKind != JsonValueKind.Object) yield break;
            foreach (var prop in o.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.Object) yield return (prop.Name, prop.Value);
        }

        private static string S(JsonElement e, string name) => GiMessage.Str(e, name);

        private static int Int(JsonElement e, string name) =>
            int.TryParse(S(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v
            : double.TryParse(S(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : 0;

        private static string Span(string start, string stop)
        {
            var a = Hhmm(start); var b = Hhmm(stop);
            if (a.Length == 0 && b.Length == 0) return "";
            return " " + a + "-" + b;
        }

        /// <summary>"1830", "18:30", "1830Z" or an ISO time → "1830" (UTC). "" when unreadable.</summary>
        public static string Hhmm(string s)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            if (s.Length == 0) return "";
            var digits = s.Replace(":", "").TrimEnd('Z');
            if (digits.Length == 4 && digits.All(char.IsDigit)) return digits;
            int dot = s.IndexOf('.');
            if (dot > 0)
            {
                int end = dot + 1;
                while (end < s.Length && char.IsDigit(s[end])) end++;
                s = s.Substring(0, Math.Min(end, dot + 8)) + s.Substring(end);
            }
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
                ? t.ToString("HHmm", CultureInfo.InvariantCulture) : "";
        }
    }

    /// <summary>
    /// Turns successive snapshots of active items (TMIs, FCA programs) into Update Area messages: a new item, a
    /// changed one (AMEND) and one that's gone (CANCEL). The first snapshot reports every item as new.
    /// </summary>
    public sealed class UaTracker
    {
        private Dictionary<string, (string StateKey, string Text)>? _last;
        private readonly string _source;

        public UaTracker(string source) { _source = source; }

        public void Reset() => _last = null;

        /// <param name="items">Current items: identity, content key, text.</param>
        public List<UaMessage> Update(IEnumerable<(string Id, string StateKey, string Text)> items, DateTime nowUtc)
        {
            var now = items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => (g.First().StateKey, g.First().Text));
            var outp = new List<UaMessage>();
            foreach (var kv in now)
            {
                if (_last != null && _last.TryGetValue(kv.Key, out var old))
                {
                    if (old.StateKey != kv.Value.StateKey)
                        outp.Add(new UaMessage { Key = kv.Value.StateKey, Utc = nowUtc, Source = _source, Text = "AMEND " + kv.Value.Text });
                }
                else outp.Add(new UaMessage { Key = kv.Value.StateKey, Utc = nowUtc, Source = _source, Text = kv.Value.Text });
            }
            if (_last != null)
                foreach (var kv in _last.Where(kv => !now.ContainsKey(kv.Key)))
                    outp.Add(new UaMessage { Key = kv.Value.StateKey + ":X", Utc = nowUtc, Source = _source, Text = "CANCEL " + kv.Value.Text });
            _last = now;
            return outp;
        }

        /// <summary>FCA programs that are on (enabled), as tracker items: "FCA DCA MIT 35 [name]".</summary>
        public static IEnumerable<(string Id, string StateKey, string Text)> FcaItems(IEnumerable<FcaProgram> programs) =>
            programs.Where(p => p.Enabled).Select(p =>
            {
                var text = "FCA " + p.Header + (p.Name.Length > 0 && !p.Header.Contains(p.Name.ToUpperInvariant()) ? " " + p.Name.ToUpperInvariant() : "");
                return (p.Id, "FCA:" + p.Id + ":" + TmiItem.Hash(text), text);
            });

        public static IEnumerable<(string Id, string StateKey, string Text)> TmiItems(IEnumerable<TmiItem> tmis) =>
            tmis.Select(t => (t.Kind + ":" + t.Id, t.StateKey, t.Text));
    }

    /// <summary>
    /// The Update Area's messages: those waiting for acknowledgement (oldest first) and those acknowledged and shown
    /// in the text area (newest first). A key already queued or acknowledged is never queued again; acknowledged
    /// keys are remembered across restarts.
    /// </summary>
    public sealed class UaQueue
    {
        public const int KeepShown = 50;
        public const int KeepAcked = 400;

        private readonly List<UaMessage> _waiting = new List<UaMessage>();
        private readonly List<UaMessage> _shown = new List<UaMessage>();
        private readonly HashSet<string> _acked;
        private readonly List<string> _ackOrder;
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

        public UaQueue(IEnumerable<string>? acked = null)
        {
            _ackOrder = (acked ?? Enumerable.Empty<string>()).Where(k => k.Length > 0).Distinct().ToList();
            _acked = new HashSet<string>(_ackOrder, StringComparer.Ordinal);
        }

        public IReadOnlyList<UaMessage> Waiting => _waiting;
        public IReadOnlyList<UaMessage> Shown => _shown;
        /// <summary>Acknowledged keys to save, oldest first.</summary>
        public IReadOnlyList<string> AckedKeys => _ackOrder;
        public event Action? Changed;

        /// <summary>Queues new messages. <paramref name="silent"/> files them straight into the text area (a backlog).</summary>
        public void Add(IEnumerable<UaMessage> messages, bool silent = false)
        {
            bool any = false;
            foreach (var m in messages.OrderBy(m => m.Utc))
            {
                if (!_seen.Add(m.Key)) continue;
                if (_acked.Contains(m.Key)) continue;
                if (silent) { Show(m); Ack(m.Key); }
                else _waiting.Add(m);
                any = true;
            }
            if (any) Changed?.Invoke();
        }

        /// <summary>Acknowledges the oldest waiting message: it moves to the top of the text area.</summary>
        public UaMessage? AcknowledgeNext()
        {
            if (_waiting.Count == 0) return null;
            var m = _waiting[0];
            _waiting.RemoveAt(0);
            Show(m);
            Ack(m.Key);
            Changed?.Invoke();
            return m;
        }

        public void Delete(string key)
        {
            if (_shown.RemoveAll(m => m.Key == key) > 0) Changed?.Invoke();
        }

        public void ClearShown()
        {
            if (_shown.Count == 0) return;
            _shown.Clear();
            Changed?.Invoke();
        }

        private void Show(UaMessage m)
        {
            _shown.Insert(0, m);
            if (_shown.Count > KeepShown) _shown.RemoveRange(KeepShown, _shown.Count - KeepShown);
        }

        private void Ack(string key)
        {
            if (!_acked.Add(key)) return;
            _ackOrder.Add(key);
            if (_ackOrder.Count > KeepAcked)
            {
                foreach (var k in _ackOrder.Take(_ackOrder.Count - KeepAcked)) _acked.Remove(k);
                _ackOrder.RemoveRange(0, _ackOrder.Count - KeepAcked);
            }
        }
    }

    /// <summary>
    /// CRC CIDs the vUSALink EDST app learned from QF readouts (%LOCALAPPDATA%\vUSALinkApp\cids.json, written by
    /// vUSALink App and vUSALink-Lite). Read-only here. The app drops a pair when the pilot reconnects or leaves.
    /// </summary>
    public sealed class EdstCidBook
    {
        public sealed class Entry
        {
            public string Cid { get; init; } = "";
            public string Callsign { get; init; } = "";
            /// <summary>Owning sector from the QF ("32"), or "".</summary>
            public string Sector { get; init; } = "";
            public bool Verified { get; init; }
            /// <summary>
            /// The companion matched it to a real flight itself: the app couldn't verify it, but the callsign is a live
            /// flight, or a misread of exactly one (see <see cref="OcrMatch"/>).
            /// </summary>
            public bool Confirmed { get; init; }
            /// <summary>Safe to act on: verified by the app or confirmed by the companion.</summary>
            public bool Usable => Verified || Confirmed;
            /// <summary>When the app read this aircraft's QF readout (UTC). Every QF, even a repeat, renews it.</summary>
            public DateTime At { get; init; }
        }

        private Dictionary<string, Entry> _byCallsign = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public int Count => _byCallsign.Count;

        public Entry? Find(string callsign) => _byCallsign.TryGetValue(callsign ?? "", out var e) ? e : null;

        /// <summary>
        /// The aircraft with this CRC CID, or null. If two callsigns carry it (a CID reused after a pilot left),
        /// the one QF'd most recently wins.
        /// </summary>
        public Entry? FindByCid(string cid)
        {
            cid = (cid ?? "").Trim();
            if (cid.Length == 0) return null;
            return _byCallsign.Values.Where(e => string.Equals(e.Cid, cid, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.At).FirstOrDefault();
        }

        /// <summary>Parses cids.json: [{cid, callsign, sector, logon, verified, at, lastSeen}].</summary>
        public static Dictionary<string, Entry> Parse(string json)
        {
            var map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;
            foreach (var o in doc.RootElement.EnumerateArray())
            {
                if (o.ValueKind != JsonValueKind.Object) continue;
                var cid = GiMessage.Str(o, "cid").Trim().ToUpperInvariant();
                var cs = GiMessage.Str(o, "callsign").Trim().ToUpperInvariant();
                if (cid.Length == 0 || cs.Length == 0) continue;
                map[cs] = new Entry
                {
                    Cid = cid,
                    Callsign = cs,
                    Sector = GiMessage.Str(o, "sector").Trim().ToUpperInvariant(),
                    Verified = o.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True,
                    At = DateTime.TryParse(GiMessage.Str(o, "at"), CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : DateTime.MinValue,
                };
            }
            return map;
        }

        public void Replace(Dictionary<string, Entry> map) => _byCallsign = map;

        /// <summary>
        /// Checks the readouts the app couldn't verify against the live flights: a live callsign is confirmed as is,
        /// and a misread of exactly one live callsign (CIALI183 for UAL1183) is filed under the real one, keeping its
        /// CID, sector and time. Anything else stays unverified.
        /// </summary>
        public void ConfirmWith(IReadOnlyCollection<string> live)
        {
            if (live.Count == 0) return;
            var liveSet = new HashSet<string>(live, StringComparer.OrdinalIgnoreCase);
            foreach (var e in _byCallsign.Values.Where(x => !x.Verified && !x.Confirmed).ToList())
            {
                string? real = liveSet.Contains(e.Callsign) ? e.Callsign : OcrMatch.Find(e.Callsign, live);
                if (real == null) continue;
                real = real.ToUpperInvariant();
                // A newer readout already filed under the real callsign wins.
                if (!string.Equals(real, e.Callsign, StringComparison.OrdinalIgnoreCase)
                    && _byCallsign.TryGetValue(real, out var existing) && existing.At >= e.At) { _byCallsign.Remove(e.Callsign); continue; }
                _byCallsign.Remove(e.Callsign);
                _byCallsign[real] = new Entry { Cid = e.Cid, Callsign = real, Sector = e.Sector, Verified = false, Confirmed = true, At = e.At };
            }
        }

        /// <summary>
        /// Aircraft QF'd in CRC after <paramref name="since"/>, oldest first: the app renews a pair's time on every
        /// QF readout it reads (typed QF, or a middle-click on a data block), and only then.
        /// </summary>
        public List<Entry> QfSince(DateTime since) =>
            _byCallsign.Values.Where(e => e.At > since).OrderBy(e => e.At).ToList();

        /// <summary>The newest QF time in the book (MinValue when empty).</summary>
        public DateTime LatestQf => _byCallsign.Count == 0 ? DateTime.MinValue : _byCallsign.Values.Max(e => e.At);
    }
}
