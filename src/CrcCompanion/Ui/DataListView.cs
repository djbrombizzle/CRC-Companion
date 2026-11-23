using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    /// <summary>Live data for the VIEWS grid views beyond the strip lists.</summary>
    public interface ILiveSource : IStripSource
    {
        FeedSnapshot? Snapshot { get; }
        FlightTracker Tracker { get; }
        IReadOnlyList<ConflictPair> Conflicts { get; }
        /// <summary>Re-run the conflict probe now (after the NM setting changes).</summary>
        void RecomputeConflicts();
        /// <summary>Fix, airport, FRD or lat/long, nearest to your facility.</summary>
        (LatLon Pos, Locations.Kind Kind, string Name)? ResolveLocation(string text);
        string? NearestFix(LatLon p, double maxNm);
        bool IsAirportId(string id);
        Pilot? FindPilot(string callsign);
        /// <summary>GROUP SUP entries: codes hidden from the CODE view.</summary>
        IReadOnlyList<string> SuppressedGroups { get; }
        /// <summary>vUSAlink hub CPDLC log for your ARTCC (CPDLC views).</summary>
        CpdlcLog Cpdlc { get; }
        /// <summary>VATFLOW FCA programs for your ARTCC (MRP view).</summary>
        IReadOnlyList<FcaProgram> FcaPrograms { get; }
        /// <summary>The ARTCC those programs are for ("ZDC").</summary>
        string FcaArtcc { get; }
        /// <summary>"" when the programs are loaded, else why not ("LOADING VATFLOW", "VATFLOW DOWN").</summary>
        string FcaStatus { get; }
        /// <summary>Changes whenever traffic or programs change (MRP recomputes then).</summary>
        int MrpVersion { get; }
        /// <summary>A program's sequence with current traffic; <paramref name="localOrder"/> overrides its order.</summary>
        FcaSequence Sequence(FcaProgram fca, IReadOnlyList<string>? localOrder);
        /// <summary>UA messages: GI (vUSAlink EDST), TMU (VATFLOW Airport TMU) and FLOW (VATFLOW FCAs) for your ARTCC.</summary>
        UaQueue UpdateArea { get; }
        /// <summary>Sources that aren't answering ("HUB DOWN", "VATFLOW TMU DOWN", "NO GI FROM HUB"), or "".</summary>
        string UaStatus { get; }
        /// <summary>CRC CID and owning sector the vUSALink EDST app learned from a QF, or null.</summary>
        EdstCidBook.Entry? EdstCid(string callsign);
        /// <summary>The aircraft the vUSALink EDST app learned this CRC CID for, or null.</summary>
        EdstCidBook.Entry? EdstCidByCid(string cid);
    }

    /// <summary>One body line: a heading (spaced like a WX report break) or an entry.</summary>
    public sealed class DataLine
    {
        public string Text { get; init; } = "";
        public bool Heading { get; init; }
        /// <summary>Identity for click-to-highlight; empty = not highlightable.</summary>
        public string Key { get; init; } = "";
        /// <summary>Click action; when null, a click toggles the highlight.</summary>
        public Action<MouseButton>? Click { get; init; }
    }

    /// <summary>
    /// ERAM list view whose body is a list of text lines, drawn like the strip views
    /// (17 px pitch at FONT 2, headings with an extra gap, '&gt;' when more lines are below,
    /// wheel to scroll). Optionally lays entries out in columns like ALTIM SET.
    /// </summary>
    public abstract class DataListView : ListViewWindow
    {
        protected readonly ILiveSource Live;
        private int _scroll;
        private readonly HashSet<string> _highlighted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        protected DataListView(IViewHost host, ViewSettings settings, string title, ILiveSource live)
            : base(host, settings, title)
        {
            Live = live;
        }

        /// <summary>Minimum body width in characters.</summary>
        protected virtual int MinChars => 24;
        /// <summary>Lay entries out in LINES x COL columns (no headings).</summary>
        protected virtual bool UseColumns => false;

        protected abstract List<DataLine> BuildLines();

        /// <summary>View-specific rows after the common ones.</summary>
        protected virtual IEnumerable<MenuRow> ExtraMenuRows() => Enumerable.Empty<MenuRow>();

        protected override IReadOnlyList<MenuRow> BuildMenuRows()
        {
            var rows = new List<MenuRow>
            {
                new ModeRow(() => Settings.Opaque, () => { Settings.Opaque = !Settings.Opaque; Changed(); }),
                new ToggleRow("BORDER", () => Settings.Border, () => { Settings.Border = !Settings.Border; Changed(); }),
                LinesRow(),
            };
            if (UseColumns) rows.Add(new StepRow("COL", () => Settings.Cols, v => { Settings.Cols = v; Changed(); }, Limits.ColsMin, Limits.ColsMax));
            rows.Add(FontRow());
            rows.Add(BrightRow());
            rows.AddRange(ExtraMenuRows());
            return rows;
        }

        /// <summary>Status line when there's no facility or data yet, else null.</summary>
        protected DataLine? NoDataLine()
        {
            if (Live.Facility == null || Live.Snapshot == null)
                return new DataLine { Text = Live.StatusText.Length > 0 ? Live.StatusText : "NO DATA" };
            return null;
        }

        protected static DataLine None() => new DataLine { Text = "NONE" };

        protected void ToggleHighlight(string key)
        {
            if (!_highlighted.Remove(key)) _highlighted.Add(key);
            Surface.Refresh();
        }

        protected override void OnBodyWheel(int delta)
        {
            _scroll += delta > 0 ? -3 : 3;
            Surface.Refresh();
        }

        protected override Size PaintBody(Painter p, double x0, double y0)
        {
            var lines = BuildLines();
            if (lines.Count == 0) lines.Add(None());
            return UseColumns ? PaintColumns(p, x0, y0, lines) : PaintList(p, x0, y0, lines);
        }

        private Size PaintList(Painter p, double x0, double y0, List<DataLine> lines)
        {
            var m = M;
            int pitch = m.TitleH;
            int groupGap = m.TitleH - 1;
            int chars = Math.Max(MinChars, lines.Max(l => l.Text.Length) + 1);
            double width = LeftPad + m.TextWidth(chars) + EntryGap;

            int max = Math.Max(1, Settings.Lines);
            int maxScroll = Math.Max(0, lines.Count - max);
            _scroll = Math.Clamp(_scroll, 0, maxScroll);
            int count = Math.Min(max, lines.Count - _scroll);

            double y = 0;
            var tops = new double[count];
            for (int v = 0; v < count; v++)
            {
                if (v > 0) y += pitch + (lines[_scroll + v].Heading ? groupGap : 0);
                tops[v] = y;
            }
            double contentH = tops[count - 1] + m.GlyphTopInRow + m.Cap;

            if (p.Drawing)
            {
                var tb = TextBrush;
                for (int v = 0; v < count; v++)
                {
                    var line = lines[_scroll + v];
                    var text = line.Text;
                    double boxY = y0 + tops[v] + m.GlyphTopInRow - 3;
                    if (line.Key.Length > 0 && _highlighted.Contains(line.Key))
                        p.Fill(Eram.Highlight, x0 + 1, boxY, width - 2, pitch);
                    var click = line.Click ?? (line.Key.Length > 0 ? (Action<MouseButton>)(_ => ToggleHighlight(line.Key)) : null);
                    if (click != null) p.Hit(x0, boxY, width, pitch, click);
                    if (v == count - 1 && _scroll + count < lines.Count) text = text.PadRight(chars - 1).Substring(0, chars - 1) + ">";
                    p.Text(text, x0 + LeftPad, y0 + tops[v] + m.GlyphTopInRow, m, tb);
                }
            }
            return new Size(width, contentH);
        }

        private Size PaintColumns(Painter p, double x0, double y0, List<DataLine> lines)
        {
            var m = M;
            int chars = Math.Max(1, lines.Max(l => l.Text.Length));
            int entryW = m.TextWidth(chars) + EntryGap;
            int rowsPer = Math.Max(1, Settings.Lines);
            int capacity = rowsPer * Math.Max(1, Settings.Cols);
            int shown = Math.Min(lines.Count, capacity);
            int cols = Math.Max(1, (int)Math.Ceiling(shown / (double)rowsPer));
            int rows = Math.Min(rowsPer, shown);

            if (p.Drawing)
            {
                var tb = TextBrush;
                for (int i = 0; i < shown; i++)
                {
                    var line = lines[i];
                    int col = i / rowsPer, row = i % rowsPer;
                    double ex = x0 + LeftPad + col * entryW, ey = y0 + row * m.Row;
                    var text = line.Text;
                    if (i == shown - 1 && shown < lines.Count) text = (text + ">").Substring(0, Math.Min(text.Length + 1, chars));
                    if (line.Key.Length > 0 && _highlighted.Contains(line.Key))
                        p.Fill(Eram.Highlight, ex - 3, ey + m.GlyphTopInRow - 3, entryW - EntryGap + 6, m.TitleH);
                    var click = line.Click ?? (line.Key.Length > 0 ? (Action<MouseButton>)(_ => ToggleHighlight(line.Key)) : null);
                    if (click != null) p.Hit(ex, ey, entryW, m.Row, click);
                    p.Text(text, ex, ey + m.GlyphTopInRow, m, tb);
                }
            }
            double width = Math.Max(LeftPad + cols * entryW + RightPad, LeftPad + m.TextWidth(Math.Min(MinChars, 12)) + EntryGap);
            double height = (rows - 1) * m.Row + m.GlyphTopInRow + m.Cap + 3;
            return new Size(width, height);
        }

        // ---------- helpers for subclasses ----------

        protected static string Fit(string s, int n) => (s ?? "").Length > n ? s!.Substring(0, n) : (s ?? "").PadRight(n);

        protected static string Alt(int hundreds) => Math.Clamp(hundreds, 0, 999).ToString("000", System.Globalization.CultureInfo.InvariantCulture);

        protected static string Hhmm(DateTime t) => t.ToString("HHmm", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Menu row that opens a typed entry for this view.</summary>
        protected CommandRow EntryRow(string label, string title, Func<IReadOnlyList<string>> current, Func<string, string> apply) =>
            new CommandRow(label, _ => OpenEntry(title, current, text =>
            {
                var feedback = apply(text);
                Changed();
                return feedback;
            }));

        /// <summary>Popup with one DELETE row.</summary>
        protected void DeletePopup(string title, Action delete) =>
            OpenPopup(title, new List<MenuRow>
            {
                new CommandRow("DELETE", _ => { delete(); ClosePopup(); Changed(); }),
            });
    }
}
