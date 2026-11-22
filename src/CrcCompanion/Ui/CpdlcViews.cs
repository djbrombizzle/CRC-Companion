using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// Base for the four CPDLC views. Data is vUSAlink's KUSA hub log for your ARTCC (read-only: these views
    /// show the datalink traffic; uplinks are still sent from the vUSAlink EDST).
    /// </summary>
    public abstract class CpdlcView : DataListView
    {
        protected CpdlcView(IViewHost h, ViewSettings s, string title, ILiveSource l) : base(h, s, title, l) { }

        protected CpdlcLog Log => Live.Cpdlc;
        /// <summary>Message text width in characters (M → TEXT).</summary>
        protected int TextChars => Math.Max(TextMin, Settings.RouteChars);
        private const int TextMin = 12;

        protected StepRow TextRow() =>
            new StepRow("TEXT", () => TextChars, v => { Settings.RouteChars = v; Changed(); }, TextMin, Limits.RouteMax, Limits.RouteStep);

        /// <summary>Why there's nothing to show yet (no facility, hub not answered), else null. A hub error after
        /// data has arrived is shown as a first line above the last good data.</summary>
        protected DataLine? HubStatus(out bool stop)
        {
            stop = true;
            if (Live.Facility == null) return new DataLine { Text = Live.StatusText.Length > 0 ? Live.StatusText : "NO FACILITY" };
            if (Log.Airspace.Length == 0) return new DataLine { Text = "NO FACILITY" };
            if (Log.LastPollUtc == null) return new DataLine { Text = Log.Error.Length > 0 ? Log.Error : "CONNECTING TO HUB" };
            stop = false;
            return Log.Error.Length > 0 ? new DataLine { Text = Log.Error } : null;
        }

        /// <summary>Lines for one message: the prefix, then the text wrapped under itself.</summary>
        protected List<DataLine> Wrapped(string prefix, string text, string key, Action<MouseButton>? click = null)
        {
            var parts = Metar.Wrap(text.Length == 0 ? "-" : text, TextChars);
            var pad = new string(' ', prefix.Length);
            return parts.Select((t, i) => new DataLine { Text = (i == 0 ? prefix : pad) + t, Key = key, Click = click }).ToList();
        }

        protected static string Age(DateTime utc, DateTime now)
        {
            int min = Math.Max(0, (int)(now - utc).TotalMinutes);
            return (min > 99 ? "99" : min.ToString(CultureInfo.InvariantCulture)).PadLeft(2) + "M";
        }

        protected override List<DataLine> BuildLines()
        {
            var lines = new List<DataLine>();
            var status = HubStatus(out bool stop);
            if (status != null) lines.Add(status);
            if (stop) return lines;
            var body = Body(DateTime.UtcNow);
            if (body.Count == 0 && status == null) lines.Add(None());
            lines.AddRange(body);
            return lines;
        }

        protected abstract List<DataLine> Body(DateTime now);
    }

    /// <summary>
    /// CPDLC ADV: what needs the controller's attention: logon requests not yet accepted, pilot requests not yet
    /// answered, UNABLE replies, and uplinks with no reply after 2 minutes (LATE). Click one to clear it.
    /// </summary>
    public sealed class CpdlcAdvView : CpdlcView
    {
        private readonly HashSet<long> _cleared = new HashSet<long>();

        public CpdlcAdvView(IViewHost h, ViewSettings s, ILiveSource l) : base(h, s, "CPDLC ADV", l) { }
        protected override string MenuTitle => "AD";
        protected override int MinChars => 30;

        protected override IEnumerable<MenuRow> ExtraMenuRows()
        {
            yield return TextRow();
            yield return new CommandRow("CLEAR ALL", _ =>
            {
                foreach (var a in Log.Advisories(DateTime.UtcNow)) _cleared.Add(a.Event.Id);
                Changed();
            });
            yield return new CommandRow("RESTORE", _ => { _cleared.Clear(); Changed(); });
        }

        protected override List<DataLine> Body(DateTime now)
        {
            var lines = new List<DataLine>();
            foreach (var a in Log.Advisories(now, _cleared))
            {
                var id = a.Event.Id;
                var prefix = Hhmm(a.Event.Utc) + " " + Fit(a.Type, 6) + " " + Fit(a.Event.Callsign, StripFormat.CallsignChars) + " ";
                lines.AddRange(Wrapped(prefix, a.Event.Text, "A" + id.ToString(CultureInfo.InvariantCulture),
                    _ => OpenPopup(a.Event.Callsign + " " + a.Type, new List<MenuRow>
                    {
                        new CommandRow("CLEAR", b => { _cleared.Add(id); ClosePopup(); Changed(); }),
                    })));
            }
            return lines;
        }
    }

    /// <summary>
    /// CPDLC HIST, drawn like ERAM's Message History view: title "HISTORY [FLID]" with DEL ALL, a FLID filter
    /// panel, then a column header (CID ACID MSG CONTENT STAT TIME ORG) and one line per closed message, newest
    /// first, with a scroll bar. A down arrow between TIME and ORG marks a pilot-initiated downlink.
    /// Click the FLID box and type a callsign to filter (Enter; an empty Enter clears it). DEL ALL removes the
    /// listed messages from the view (M → RESTORE brings them back). Click a message for its full text, Due To
    /// Reason and DELETE. CID and ORG stay blank: the vUSAlink hub doesn't report ERAM CIDs or sectors.
    /// </summary>
    public sealed class CpdlcHistView : CpdlcView
    {
        private const int FlidChars = 8;
        private const int ScrollRightPad = 4;
        private readonly HashSet<long> _deleted = new HashSet<long>();
        private int _scroll;
        /// <summary>Text being typed in the FLID box, or null when not typing.</summary>
        private string? _typing;

        public CpdlcHistView(IViewHost h, ViewSettings s, ILiveSource l) : base(h, s, "CPDLC HIST", l)
        {
            Surface.KeyHandler = OnKey;
            Surface.TextHandler = OnText;
            KeyboardEnded += () => { _typing = null; Surface.Refresh(); };
        }

        protected override string MenuTitle => "CH";
        protected override int BodyPadding => 0;

        private string Filter => Settings.Items.Count > 0 ? Settings.Items[0] : "";

        protected override string TitleText => Filter.Length > 0 ? "HISTORY " + Filter : "HISTORY";

        protected override (string Label, System.Windows.Media.Brush Back, Action Run)? TitleButton =>
            ("DEL ALL", Eram.DelAll, () => { foreach (var r in Rows()) _deleted.Add(r.Message.Id); _scroll = 0; Changed(); });

        protected override IEnumerable<MenuRow> ExtraMenuRows()
        {
            yield return TextRow();
            yield return new CommandRow("RESTORE", _ => { _deleted.Clear(); Changed(); });
        }

        protected override List<DataLine> Body(DateTime now) => new List<DataLine>();

        private List<HistoryRow> Rows()
        {
            var f = Filter;
            return Log.ClosedHistory()
                .Where(r => !_deleted.Contains(r.Message.Id))
                .Where(r => f.Length == 0 || string.Equals(r.Message.Callsign, f, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>Column header, aligned with <see cref="RowText"/>.</summary>
        public static string HeaderText(int textChars) => "CID ACID    " + Fit("MSG CONTENT", textChars) + " STAT TIME   ORG";

        /// <summary>One entry's text, columns as in ERAM (CID and ORG blank; the PID arrow is drawn separately).</summary>
        public static string RowText(HistoryRow r, int textChars) =>
            "    " + Fit(r.Message.Callsign, 8) + CpdlcText.Truncate(CpdlcText.Abbreviate(r.Message.Text), textChars) + " " + Fit(r.StatText, 5) + Hhmm(r.Message.Utc);

        private int LineChars => 4 + 8 + TextChars + 1 + 5 + 4 + 6;
        /// <summary>Column of the PID arrow (between TIME and ORG).</summary>
        private int ArrowCol => 4 + 8 + TextChars + 1 + 5 + 4 + 1;

        protected override void OnBodyWheel(int delta)
        {
            _scroll += delta > 0 ? -1 : 1;
            Surface.Refresh();
        }

        protected override System.Windows.Size PaintBody(Painter p, double x0, double y0)
        {
            var m = M;
            int rowH = m.TitleH;
            int gt = m.GlyphTopInTitle;
            int sbW = m.BoxW;
            double textW = m.TextWidth(LineChars);
            double width = LeftPad + textW + EntryGap + sbW + ScrollRightPad;
            var tb = TextBrush;

            // Filter panel: FLID [box]
            double y = y0;
            double boxX = x0 + LeftPad + m.TextWidth(5);
            double boxW = m.TextWidth(FlidChars) + 6;
            if (p.Drawing)
            {
                p.Text("FLID", x0 + LeftPad, y + 2 + gt, m, tb);
                p.Frame(Eram.ViewBorder, boxX, y + 2, boxW, rowH, 1);
                var shown = _typing != null ? _typing + "_" : Filter;
                p.Text(shown, boxX + 3, y + 2 + gt, m, tb);
            }
            p.Hit(boxX, y + 2, boxW, rowH, _ => StartTyping());
            y += rowH + 4;
            Rule(p, x0, y, width);
            y += 1;

            // System message (hub status, or nothing to show) in yellow.
            var status = HubStatus(out bool stop);
            var rows = stop ? new List<HistoryRow>() : Rows();
            if (status != null || rows.Count == 0)
            {
                var msg = status?.Text ?? "NO CLOSED MESSAGES FOUND";
                if (p.Drawing) p.Text(msg, x0 + LeftPad, y + 2 + gt, m, Eram.SystemYellow);
                y += rowH + 3;
                if (rows.Count == 0) return new System.Windows.Size(width, y - y0);
                Rule(p, x0, y, width);
                y += 1;
            }

            // Column header.
            if (p.Drawing) p.Text(HeaderText(TextChars), x0 + LeftPad, y + 2 + gt, m, tb);
            y += rowH + 3;
            Rule(p, x0, y, width);
            y += 1;

            // Entries.
            int visible = Math.Max(1, Settings.Lines);
            int maxScroll = Math.Max(0, rows.Count - visible);
            _scroll = Math.Clamp(_scroll, 0, maxScroll);
            int count = Math.Min(visible, rows.Count - _scroll);
            double top = y + 2;
            if (p.Drawing)
            {
                for (int v = 0; v < count; v++)
                {
                    var r = rows[_scroll + v];
                    double ry = top + v * rowH;
                    p.Text(RowText(r, TextChars), x0 + LeftPad, ry + gt, m, tb);
                    if (r.Pid)
                    {
                        // Filled down arrow about a capital's height, centred in its column.
                        double cx = x0 + LeftPad + m.TextWidth(ArrowCol) + m.Cell / 2.0, cy = ry + gt + m.Cap / 2.0;
                        p.Triangle(tb, cx - m.Cap, cy - m.Cap, 2 * m.Cap, 2 * m.Cap, up: false);
                    }
                    var row = r;
                    p.Hit(x0, ry, LeftPad + textW, rowH, _ => OpenRowPopup(row));
                }
            }
            double entriesH = Math.Max(count, 3) * rowH + 5;
            if (p.Drawing) DrawScrollBar(p, x0 + width - ScrollRightPad - sbW, y + 1, sbW, entriesH - 2, rows.Count, visible, maxScroll);
            y += entriesH;
            return new System.Windows.Size(width, y - y0);
        }

        private static void Rule(Painter p, double x0, double y, double width)
        {
            if (p.Drawing) p.Fill(Eram.ViewBorder, x0, y, width, 1);
        }

        private void ScrollBy(int n)
        {
            _scroll += n;
            Surface.Refresh();
        }

        private void DrawScrollBar(Painter p, double sx, double sy, int sbW, double sh, int total, int visible, int maxScroll)
        {
            p.Frame(Eram.ViewBorder, sx, sy, sbW, sh, 1);
            p.Fill(Eram.ViewBorder, sx, sy + sbW, sbW, 1);
            p.Fill(Eram.ViewBorder, sx, sy + sh - sbW - 1, sbW, 1);
            p.Triangle(Eram.ScrollGray, sx, sy, sbW, sbW, up: true);
            p.Triangle(Eram.ScrollGray, sx, sy + sh - sbW, sbW, sbW, up: false);
            p.Hit(sx, sy, sbW, sbW, _ => ScrollBy(-1));
            p.Hit(sx, sy + sh - sbW, sbW, sbW, _ => ScrollBy(1));

            double trackY = sy + sbW + 1, trackH = sh - 2 * sbW - 2;
            if (trackH <= 6) return;
            double frac = Math.Min(1.0, visible / (double)Math.Max(total, 1));
            double thumbH = Math.Max(6, Math.Floor((trackH - 4) * frac));
            double thumbY = Math.Floor(trackY + 2 + (trackH - 4 - thumbH) * (maxScroll == 0 ? 0 : _scroll / (double)maxScroll));
            p.Frame(Eram.ScrollGray, sx + 3, thumbY, sbW - 6, thumbH, 1);
            p.Hit(sx, trackY, sbW, thumbY - trackY, _ => ScrollBy(-visible));
            p.Hit(sx, thumbY + thumbH, sbW, trackY + trackH - (thumbY + thumbH), _ => ScrollBy(visible));
        }

        private void OpenRowPopup(HistoryRow r)
        {
            var rows = new List<MenuRow>();
            foreach (var line in Metar.Wrap(r.Message.Text.Length == 0 ? "-" : r.Message.Text, 32))
                rows.Add(new CommandRow(line, null));
            if (r.Reason.Length > 0) rows.Add(new CommandRow(r.StatText + " " + r.Reason, null));
            var id = r.Message.Id;
            rows.Add(new CommandRow("DELETE", b => { _deleted.Add(id); ClosePopup(); Changed(); }));
            OpenPopup(r.Message.Callsign, rows);
        }

        // ---------- FLID typing ----------

        private void StartTyping()
        {
            _typing = Filter;
            BeginKeyboard();
            Surface.Refresh();
        }

        private bool OnKey(Key key, ModifierKeys mods)
        {
            if (!KeyboardActive || _typing == null) return false;
            switch (key)
            {
                case Key.Escape:
                    EndKeyboard();
                    return true;
                case Key.Enter:
                    var text = _typing.Trim().ToUpperInvariant();
                    Settings.Items.Clear();
                    if (text.Length > 0) Settings.Items.Add(text);
                    _scroll = 0;
                    EndKeyboard();
                    Changed();
                    return true;
                case Key.Back:
                    if (_typing.Length > 0) _typing = _typing.Substring(0, _typing.Length - 1);
                    Surface.Refresh();
                    return true;
                default:
                    return false;
            }
        }

        private void OnText(string text)
        {
            if (!KeyboardActive || _typing == null) return;
            foreach (var c in text.ToUpperInvariant())
                if (((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) && _typing.Length < FlidChars - 1) _typing += c;
            Surface.Refresh();
        }
    }

    /// <summary>
    /// CPDLC MSGOUT, drawn like ERAM's Message Out view: title "MESSAGE OUT" with CLEANUP, a SPECIAL AREA for
    /// errors (UNA, TIM) that stay until acknowledged, and a NORMAL AREA (SNT, STBY and dimmed closed messages)
    /// with ACID / STAT / TIME sort and MSG FILTER. Click a message to see its full text; click an error's status
    /// for ACKNOWLEDGE (moves it to the normal area) or ACKNOWLEDGE+DELETE. CLEANUP removes closed messages.
    /// CID and ORG stay blank: the vUSAlink hub doesn't report ERAM CIDs or sectors.
    /// </summary>
    public sealed class CpdlcMsgoutView : CpdlcView
    {
        private static readonly string[] FilterStats = { "SNT", "STBY", "WIL", "ROG", "RCVD", "NRR", "CLSD", "UNA", "TIM" };

        private readonly HashSet<long> _acked = new HashSet<long>();
        private readonly HashSet<long> _removed = new HashSet<long>();
        /// <summary>Message whose full text is shown, or 0.</summary>
        private long _expanded;
        /// <summary>Special-area message whose status is picked (ACKNOWLEDGE menu open), or 0.</summary>
        private long _picked;
        private string _sort = "TIME";
        private bool _sortDesc = true;
        private int _scroll;

        public CpdlcMsgoutView(IViewHost h, ViewSettings s, ILiveSource l) : base(h, s, "CPDLC MSGOUT", l) { }

        protected override string MenuTitle => "MO";
        protected override int BodyPadding => 0;
        protected override string TitleText => "MESSAGE OUT";

        protected override (string Label, System.Windows.Media.Brush Back, Action Run)? TitleButton =>
            ("CLEANUP", Eram.Black, Cleanup);

        protected override bool TitleButtonDim => !NormalRows(DateTime.UtcNow).Any(r => r.Closed);

        protected override IEnumerable<MenuRow> ExtraMenuRows()
        {
            yield return TextRow();
            yield return new CommandRow("RESTORE", _ => { _removed.Clear(); _acked.Clear(); Changed(); });
        }

        protected override List<DataLine> Body(DateTime now) => new List<DataLine>();

        private void Cleanup()
        {
            foreach (var r in NormalRows(DateTime.UtcNow).Where(r => r.Closed)) _removed.Add(r.Uplink.Id);
            _scroll = 0;
            Changed();
        }

        private List<MsgOutRow> SpecialRows(DateTime now) =>
            Log.MessageOut(now).Where(r => r.Special && !_acked.Contains(r.Uplink.Id) && !_removed.Contains(r.Uplink.Id)).ToList();

        private List<MsgOutRow> NormalRows(DateTime now)
        {
            var hidden = new HashSet<string>(Settings.Items, StringComparer.OrdinalIgnoreCase);
            var rows = Log.MessageOut(now)
                .Where(r => (!r.Special || _acked.Contains(r.Uplink.Id)) && !_removed.Contains(r.Uplink.Id) && !hidden.Contains(r.Stat))
                .Select(r => r.Special ? new MsgOutRow { Uplink = r.Uplink, Stat = r.Stat, Reason = r.Reason, Closed = true } : r);
            IOrderedEnumerable<MsgOutRow> sorted = _sort switch
            {
                "ACID" => _sortDesc ? rows.OrderByDescending(r => r.Uplink.Callsign, StringComparer.Ordinal) : rows.OrderBy(r => r.Uplink.Callsign, StringComparer.Ordinal),
                "STAT" => _sortDesc ? rows.OrderByDescending(r => r.Stat, StringComparer.Ordinal) : rows.OrderBy(r => r.Stat, StringComparer.Ordinal),
                _ => _sortDesc ? rows.OrderByDescending(r => r.Uplink.Utc) : rows.OrderBy(r => r.Uplink.Utc),
            };
            return sorted.ThenByDescending(r => r.Uplink.Id).ToList();
        }

        private void SortBy(string col)
        {
            if (_sort == col) _sortDesc = !_sortDesc;
            else { _sort = col; _sortDesc = col == "TIME"; }
            Changed();
        }

        // Columns: CID(3) sp ACID(8) MSG CONTENT(W) sp STAT(6) TIME(4) sp sp ORG(3)
        private const int AcidCol = 4, ContentCol = 12;
        private int StatCol => ContentCol + TextChars + 1;
        private int TimeCol => StatCol + 6;
        private int LineChars => TimeCol + 4 + 2 + 3;

        private string Mark(string col) => _sort != col ? "Δ" : _sortDesc ? "▼" : "▲";

        private string HeaderText(bool sortable) =>
            "CID " + Fit("ACID" + (sortable ? Mark("ACID") : ""), 8) + Fit("MSG CONTENT", TextChars) + " " +
            Fit("STAT" + (sortable ? Mark("STAT") : ""), 6) + Fit("TIME" + (sortable ? Mark("TIME") : ""), 6) + "ORG";

        protected override void OnBodyWheel(int delta)
        {
            _scroll += delta > 0 ? -1 : 1;
            Surface.Refresh();
        }

        protected override System.Windows.Size PaintBody(Painter p, double x0, double y0)
        {
            var m = M;
            int rowH = m.TitleH;
            int gt = m.GlyphTopInTitle;
            double tx = x0 + LeftPad;
            double width = LeftPad + m.TextWidth(LineChars) + EntryGap;
            var now = DateTime.UtcNow;
            var tb = TextBrush;
            double y = y0;
            double bottom = y0;   // lowest point drawn, including a full-text box

            var status = HubStatus(out bool stop);
            if (status != null)
            {
                if (p.Drawing) p.Text(status.Text, tx, y + 2 + gt, m, Eram.SystemYellow);
                y += rowH + 3;
                if (stop) return new System.Windows.Size(width, y - y0);
            }

            var special = SpecialRows(now);
            var normal = NormalRows(now);

            if (special.Count > 0)
            {
                Bar(p, x0, y, width, rowH, "SPECIAL AREA", null);
                y += rowH + 1;
                if (p.Drawing) p.Text(HeaderText(false), tx, y + 2 + gt, m, tb);
                y += rowH + rowH / 2;
                y = Rows(p, x0, y, width, special, true, ref bottom);
                y += 3;
            }

            if (normal.Count > 0 || special.Count == 0)
            {
                Bar(p, x0, y, width, rowH, "NORMAL AREA", "MSG FILTER...");
                y += rowH + 1;
                if (p.Drawing)
                {
                    p.Text(HeaderText(true), tx, y + 2 + gt, m, tb);
                    p.Hit(tx + m.TextWidth(AcidCol), y, m.TextWidth(6), rowH, _ => SortBy("ACID"));
                    p.Hit(tx + m.TextWidth(StatCol), y, m.TextWidth(6), rowH, _ => SortBy("STAT"));
                    p.Hit(tx + m.TextWidth(TimeCol), y, m.TextWidth(5), rowH, _ => SortBy("TIME"));
                }
                y += rowH + rowH / 2;
                int visible = Math.Max(1, Settings.Lines);
                _scroll = Math.Clamp(_scroll, 0, Math.Max(0, normal.Count - visible));
                var shown = normal.Skip(_scroll).Take(visible).ToList();
                y = Rows(p, x0, y, width, shown, false, ref bottom);
                y += 3;
            }

            return new System.Windows.Size(width, Math.Max(y, bottom) - y0);
        }

        /// <summary>Gray area header bar; the normal area's has a separate MSG FILTER... cell over STAT/TIME/ORG.</summary>
        private void Bar(Painter p, double x0, double y, double width, int rowH, string title, string? right)
        {
            if (!p.Drawing) return;
            var m = M;
            double split = right == null ? width : x0 + LeftPad + m.TextWidth(StatCol) - 4;
            p.Fill(Eram.AreaBar, x0, y, split - x0, rowH);
            p.TextCentered(title, x0, split - x0, y + m.GlyphTopInTitle, m, Eram.AreaBarText);
            if (right != null)
            {
                p.Fill(Eram.AreaBar, split + 2, y, x0 + width - split - 2, rowH);
                p.TextCentered(right, split + 2, x0 + width - split - 2, y + m.GlyphTopInTitle, m, Eram.AreaBarText);
                p.Hit(split + 2, y, x0 + width - split - 2, rowH, _ => OpenFilter());
            }
        }

        private double Rows(Painter p, double x0, double y, double width, List<MsgOutRow> rows, bool special, ref double bottom)
        {
            var m = M;
            int rowH = m.TitleH, gt = m.GlyphTopInTitle;
            double tx = x0 + LeftPad;
            var dim = Eram.TextGray(Math.Max(0, Settings.Bright * 55 / 100));
            double contentX = tx + m.TextWidth(ContentCol), contentW = m.TextWidth(TextChars);
            double statX = tx + m.TextWidth(StatCol);
            (MsgOutRow Row, double Y)? expanded = null;

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                double ry = y;
                if (p.Drawing)
                {
                    var color = !special && r.Closed ? dim : TextBrush;
                    var content = CpdlcText.Abbreviate(r.Uplink.Text);
                    var line = "    " + Fit(r.Uplink.Callsign, 8) + CpdlcText.Truncate(content, TextChars) + " " +
                               Fit("", 6) + Hhmm(r.Uplink.Utc);
                    p.Text(line, tx, ry + gt, m, color);

                    // STAT: orange in the special area; picked = orange cell with black text.
                    var statBrush = special ? Eram.ErrorOrange : color;
                    if (special && _picked == r.Uplink.Id)
                    {
                        p.Fill(Eram.ErrorOrange, statX - 2, ry, m.TextWidth(r.StatText.Length) + 4, rowH);
                        statBrush = Eram.Black;
                    }
                    p.Text(r.StatText, statX, ry + gt, m, statBrush);

                    var row = r;
                    p.Hit(contentX - 2, ry, contentW + 4, rowH, _ => { _expanded = _expanded == row.Uplink.Id ? 0 : row.Uplink.Id; Changed(); });
                    if (special) p.Hit(statX - 2, ry, m.TextWidth(6), rowH, _ => PickError(row));
                    if (_expanded == r.Uplink.Id) expanded = (r, ry);
                }
                y += rowH;
                if ((i + 1) % 3 == 0 && i + 1 < rows.Count)
                {
                    if (p.Drawing) p.Fill(Eram.GroupRule, tx, y + 1, m.TextWidth(LineChars), 1);
                    y += 3;
                }
            }
            bottom = Math.Max(bottom, y);

            // Full text of the picked message: the content cell lit, and the whole message in a box beside it.
            // Measured on both passes so the window grows to fit it.
            var exp = rows.FirstOrDefault(r => r.Uplink.Id == _expanded);
            if (exp != null)
            {
                int idx = rows.IndexOf(exp);
                double ry = expanded?.Y ?? FirstY(y, rows.Count, idx, rowH);
                double boxX = contentX + contentW + 2, boxW = x0 + width - 2 - boxX;
                int chars = Math.Max(8, (int)((boxW - 8) / m.Cell));
                var lines = Metar.Wrap(exp.Uplink.Text, chars);
                double boxH = lines.Count * rowH + 2;
                if (p.Drawing)
                {
                    p.Fill(Eram.AreaBar, contentX - 2, ry, contentW + 4, rowH);
                    p.Text(CpdlcText.Truncate(CpdlcText.Abbreviate(exp.Uplink.Text), TextChars), contentX, ry + gt, m, Eram.Black);
                    p.Fill(Eram.AreaBar, boxX, ry, boxW, boxH);
                    for (int k = 0; k < lines.Count; k++) p.Text(lines[k], boxX + 4, ry + 1 + k * rowH + gt, m, Eram.AreaBarText);
                    p.Hit(boxX, ry, boxW, boxH, _ => { _expanded = 0; Changed(); });
                }
                bottom = Math.Max(bottom, ry + boxH);
            }
            return y;
        }

        /// <summary>Top of row <paramref name="idx"/> during the measure pass (rows end at <paramref name="end"/>).</summary>
        private static double FirstY(double end, int count, int idx, int rowH)
        {
            double groups = Math.Max(0, (count - 1) / 3);
            double start = end - count * rowH - groups * 3;
            return start + idx * rowH + (idx / 3) * 3;
        }

        private void PickError(MsgOutRow r)
        {
            var id = r.Uplink.Id;
            _picked = id;
            Changed();
            OpenPopup(r.Uplink.Callsign + " " + r.StatText, new List<MenuRow>
            {
                new CommandRow("ACKNOWLEDGE", b => { _acked.Add(id); ClosePopup(); Changed(); }),
                new CommandRow("ACKNOWLEDGE+DELETE", b => { _acked.Add(id); _removed.Add(id); ClosePopup(); Changed(); }),
            }, () => { if (_picked == id) { _picked = 0; Changed(); } });
        }

        private void OpenFilter()
        {
            var rows = new List<MenuRow>();
            foreach (var st in FilterStats)
            {
                var s = st;
                rows.Add(new ToggleRow(s, () => !Settings.Items.Contains(s), () =>
                {
                    if (!Settings.Items.Remove(s)) Settings.Items.Add(s);
                    Changed();
                }));
            }
            OpenPopup("MSG FILTER", rows);
        }
    }

    /// <summary>
    /// CPDLC TOC SET (transfer of communication): the hub link status, then each aircraft logged on to CPDLC in
    /// your ARTCC with the frequency it's tuned to now and its last CONTACT / MONITOR uplink and reply.
    /// </summary>
    public sealed class CpdlcTocView : CpdlcView
    {
        public CpdlcTocView(IViewHost h, ViewSettings s, ILiveSource l) : base(h, s, "CPDLC TOC SET", l) { }
        protected override string MenuTitle => "TC";
        protected override int MinChars => 34;

        protected override List<DataLine> Body(DateTime now)
        {
            var lines = new List<DataLine>
            {
                new DataLine { Text = "KUSA " + Log.Airspace + " " + Log.Connected.Count.ToString(CultureInfo.InvariantCulture) + " LOGGED ON" },
            };
            var toc = Log.LastTransfers();
            var radios = Live.Snapshot?.Transceivers;
            foreach (var cs in Log.Connected.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            {
                string tuned = "";
                if (radios != null && radios.TryGetValue(cs, out var tx) && tx.Count > 0 && tx[0].FrequencyHz > 0)
                    tuned = (tx[0].FrequencyHz / 1e6).ToString("0.000", CultureInfo.InvariantCulture);
                string last = "", reply = "";
                if (toc.TryGetValue(cs, out var u))
                {
                    var f = CpdlcLog.TransferFrequency(u.Uplink.Text);
                    last = Hhmm(u.Uplink.Utc) + " " + (f.Length > 0 ? f : CpdlcLog.TransferUnit(u.Uplink.Text));
                    reply = u.Open ? (u.Code.Length > 0 ? u.Code : "OPEN") : u.Code;
                }
                var text = Fit(cs, StripFormat.CallsignChars) + " " + Fit(tuned, 7) + " " + Fit(last, 12) + " " + reply;
                lines.Add(new DataLine { Text = text.TrimEnd(), Key = cs });
            }
            if (Log.Connected.Count == 0) lines.Add(None());
            return lines;
        }
    }
}
