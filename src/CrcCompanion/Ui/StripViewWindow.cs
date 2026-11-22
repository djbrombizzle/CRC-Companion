using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    public enum StripKind { Inbound, InSector, Departures }

    /// <summary>What the strip views need from the app.</summary>
    public interface IStripSource
    {
        TrafficResult? Traffic { get; }
        FacilityContext? Facility { get; }
        /// <summary>True when you're logged on as a controller (frequency marks are meaningful).</summary>
        bool Connected { get; }
        /// <summary>Short status shown when there is nothing to list (e.g. "NO FACILITY").</summary>
        string StatusText { get; }
        /// <summary>Recompute the lists now (e.g. after the Inbound look-ahead changes).</summary>
        void Recompute();
        /// <summary>A fix or airport's position, nearest your facility (for the INBND inbound fix), or null.</summary>
        LatLon? FixPosition(string id);
    }

    /// <summary>
    /// INBND and DEPT are drawn like ERAM's Inbound and Departure List views (R-Position User Manual 13.3-13.4):
    /// sublists, each a tear-off marker and title, then one line per flight: AID and filed altitude (a coral box
    /// marks non-RVSM), with the boundary time on INBND. Sublists are the names entered in the M menu (fixes or
    /// airports), or automatic (INBND: inbound fix; DEPT: departure airport) when none are entered.
    /// IN SECTOR stays a one-line strip list: mark, callsign, type, destination, altitude, beacon, route.
    /// </summary>
    public sealed class StripViewWindow : ListViewWindow
    {
        private readonly StripKind _kind;
        private readonly IStripSource _source;
        private int _scroll;
        /// <summary>Callsigns highlighted by clicking; kept by callsign so the highlight follows the flight as lists re-sort.</summary>
        private readonly HashSet<string> _highlighted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _deleteMode;

        // ERAM sublist columns: AID(8) filed altitude(7) gap(3) boundary time(4).
        private const int AltCol = 8, AltChars = 7, TimeCol = 18;

        public StripViewWindow(IViewHost host, ViewSettings settings, StripKind kind, IStripSource source)
            : base(host, settings, kind switch { StripKind.Inbound => "INBND", StripKind.InSector => "IN SECTOR", _ => "DEPT" })
        {
            _kind = kind;
            _source = source;
        }

        private bool Sublists => _kind != StripKind.InSector;

        protected override string MenuTitle => _kind switch { StripKind.Inbound => "INBND", StripKind.InSector => "IS", _ => "DEPT" };

        protected override IReadOnlyList<MenuRow> BuildMenuRows()
        {
            if (!Sublists)
                return new List<MenuRow>
                {
                    new ModeRow(() => Settings.Opaque, () => { Settings.Opaque = !Settings.Opaque; Changed(); }),
                    new ToggleRow("BORDER", () => Settings.Border, () => { Settings.Border = !Settings.Border; Changed(); }),
                    LinesRow(),
                    new StepRow("ROUTE", () => Settings.RouteChars, v => { Settings.RouteChars = v; Changed(); }, Limits.RouteMin, Limits.RouteMax, Limits.RouteStep),
                    FontRow(),
                    BrightRow(),
                    new ToggleRow("OTHERS", () => Settings.ShowOthers, () => { Settings.ShowOthers = !Settings.ShowOthers; Changed(); }),
                };

            // As in ERAM: O, BORDER, TEAROFF, LINES, FONT, BRIGHT, SORT, then the sublist entry row.
            var rows = new List<MenuRow>
            {
                new ModeRow(() => Settings.Opaque, () => { Settings.Opaque = !Settings.Opaque; Changed(); }),
                new ToggleRow("BORDER", () => Settings.Border, () => { Settings.Border = !Settings.Border; Changed(); }),
                new ToggleRow("TEAROFF", () => Settings.Tearoff, () => { Settings.Tearoff = !Settings.Tearoff; Changed(); }),
                LinesRow(),
                FontRow(),
                BrightRow(),
                new CommandRow("SORT", _ => OpenSortMenu()),
                new InputRow(AddSublist),
            };
            // Companion extras after ERAM's rows.
            if (_kind == StripKind.Inbound)
                rows.Add(new StepRow("MIN", () => Settings.Minutes, v => { Settings.Minutes = v; _source.Recompute(); Changed(); },
                    Limits.MinutesMin, Limits.MinutesMax, Limits.MinutesStep));
            else
                rows.Add(new ToggleRow("PREFILE", () => Settings.ShowPrefiles, () => { Settings.ShowPrefiles = !Settings.ShowPrefiles; Changed(); }));
            return rows;
        }

        private void AddSublist(string name)
        {
            name = name.Trim().ToUpperInvariant();
            if (name.Length == 0 || Settings.Items.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
            Settings.Items.Add(name);
            Changed();
        }

        /// <summary>
        /// SORT menu: BOUNDARY TIME (INBND) and FIX NAME choose the sublist order; with DELETE on, clicking a
        /// sublist name below deletes it.
        /// </summary>
        private void OpenSortMenu()
        {
            var rows = new List<MenuRow>();
            void SetSort(string s) { Settings.Sort = Settings.Sort == s ? "" : s; Changed(); }
            if (_kind == StripKind.Inbound)
                rows.Add(new ToggleRow("BOUNDARY TIME", () => Settings.Sort == StripLists.SortTime, () => SetSort(StripLists.SortTime)));
            rows.Add(new ToggleRow("FIX NAME", () => Settings.Sort == StripLists.SortName, () => SetSort(StripLists.SortName)));
            rows.Add(new ToggleRow("DELETE", () => _deleteMode, () => { _deleteMode = !_deleteMode; }, Eram.DeleteTeal));
            foreach (var n in Settings.Items.ToList())
            {
                var name = n;
                rows.Add(new CommandRow(name, _ =>
                {
                    if (!_deleteMode) return;
                    Settings.Items.Remove(name);
                    Changed();
                    OpenSortMenu();   // rebuild without it
                }));
            }
            OpenPopup("SORT", rows, () => _deleteMode = false);
        }

        protected override void OnBodyWheel(int delta)
        {
            _scroll += delta > 0 ? -3 : 3;
            Surface.Refresh();
        }

        private void ToggleHighlight(string callsign)
        {
            if (!_highlighted.Remove(callsign)) _highlighted.Add(callsign);
            Surface.Refresh();
        }

        protected override Size PaintBody(Painter p, double x0, double y0) =>
            Sublists ? PaintSublists(p, x0, y0) : PaintStrips(p, x0, y0);

        // ---------------- INBND / DEPT ----------------

        private sealed class Line
        {
            public string Title = "";
            public StripItem? Item;
            public bool First;
        }

        private List<Line>? BuildSublistLines(out string status)
        {
            status = "";
            var t = _source.Traffic;
            var f = _source.Facility;
            if (t == null || f == null) { status = _source.StatusText; return null; }

            List<Sublist> groups;
            if (_kind == StripKind.Inbound)
            {
                var fixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string Fix(StripItem i)
                {
                    if (!fixes.TryGetValue(i.Callsign, out var k))
                        fixes[i.Callsign] = k = StripLists.InboundFix(i.Route, f.Boundary, _source.FixPosition) ?? f.DisplayAirport(i.Destination);
                    return k;
                }
                groups = StripLists.Group(t.Inbound, Settings.Items, Fix,
                    (name, i) => StripLists.SameAirport(name, i.Destination, f.DisplayAirport) || StripLists.InRoute(name, i.Route) ||
                                 string.Equals(Fix(i), name, StringComparison.OrdinalIgnoreCase),
                    Settings.Sort);
            }
            else
            {
                var deps = t.Departures.Where(i => Settings.ShowPrefiles || !i.IsPrefile);
                groups = StripLists.Group(deps, Settings.Items, i => f.DisplayAirport(i.Departure),
                    (name, i) => StripLists.SameAirport(name, i.Departure, f.DisplayAirport),
                    Settings.Sort == StripLists.SortTime ? StripLists.SortName : Settings.Sort);
            }

            var lines = new List<Line>();
            foreach (var g in groups)
            {
                lines.Add(new Line { Title = g.Title, First = true });
                foreach (var i in g.Items) lines.Add(new Line { Item = i });
            }
            return lines;
        }

        private Size PaintSublists(Painter p, double x0, double y0)
        {
            var m = M;
            int pitch = m.Row;              // ERAM list lines
            int gap = m.Row / 2;            // extra space above each sublist title
            int chars = _kind == StripKind.Inbound ? TimeCol + 4 : AltCol + AltChars + 1;
            double tx = x0 + LeftPad;
            double width = LeftPad + m.TextWidth(chars) + EntryGap;
            var tb = TextBrush;

            var lines = BuildSublistLines(out var status);
            if (lines == null || lines.Count == 0)
            {
                if (p.Drawing) p.Text(lines == null ? status : "NONE", tx, y0 + m.GlyphTopInRow, m, tb);
                return new Size(Math.Max(width, LeftPad + m.TextWidth(Math.Max(chars, status.Length)) + EntryGap), m.GlyphTopInRow + m.Cap);
            }

            int max = Math.Max(1, Settings.Lines);
            int maxScroll = Math.Max(0, lines.Count - max);
            _scroll = Math.Clamp(_scroll, 0, maxScroll);
            int count = Math.Min(max, lines.Count - _scroll);

            var tops = new double[count];
            double y = 0;
            for (int v = 0; v < count; v++)
            {
                if (v > 0) y += pitch + (lines[_scroll + v].First ? gap : 0);
                tops[v] = y;
            }
            double contentH = tops[count - 1] + m.GlyphTopInRow + m.Cap + 3;

            if (p.Drawing)
            {
                for (int v = 0; v < count; v++)
                {
                    var line = lines[_scroll + v];
                    double top = y0 + tops[v] + m.GlyphTopInRow;
                    if (line.Item == null)
                    {
                        if (Settings.Tearoff) DrawMarker(p, tx, top - (m.MarkerH - m.Cap) / 2.0);
                        p.Text(line.Title, tx + m.TextWidth(AltCol), top, m, tb);
                        continue;
                    }
                    var i = line.Item;
                    double boxY = top - 4, boxH = pitch;
                    if (_highlighted.Contains(i.Callsign)) p.Fill(Eram.Highlight, x0 + 1, boxY, width - 2, boxH);
                    var key = i.Callsign;
                    p.Hit(x0, boxY, width, boxH, _ => ToggleHighlight(key));

                    p.Text(i.Callsign, tx, top, m, tb);
                    var alt = i.Filed.Length > AltChars ? i.Filed.Substring(0, AltChars) : i.Filed;
                    double ax = tx + m.TextWidth(AltCol);
                    p.Text(alt, ax, top, m, tb);
                    // Non-RVSM indicator: a box around the altitude.
                    if (i.NonRvsm && alt.Length > 0)
                        p.Frame(Eram.NonRvsm, ax - 3, top - 3, m.TextWidth(alt.Length) + 5, m.Cap + 6, 1);
                    if (_kind == StripKind.Inbound && i.EntryUtc != null)
                        p.Text(i.EntryUtc.Value.ToString("HHmm", System.Globalization.CultureInfo.InvariantCulture), tx + m.TextWidth(TimeCol), top, m, tb);
                }
                // More lines below than fit: ERAM's '>' at the end of the last line.
                if (_scroll + count < lines.Count) p.Text(">", tx + m.TextWidth(chars - 1), y0 + tops[count - 1] + m.GlyphTopInRow, m, tb);
            }
            return new Size(width, contentH);
        }

        // ---------------- IN SECTOR ----------------

        private List<(string Text, string Key)> BuildStripLines()
        {
            var lines = new List<(string, string)>();
            var t = _source.Traffic;
            var f = _source.Facility;
            if (t == null || f == null)
            {
                lines.Add((_source.StatusText, ""));
                return lines;
            }
            foreach (var i in Core.Traffic.VisibleInSector(t.InSector, _source.Connected, Settings.ShowOthers))
                lines.Add((StripFormat.InSector(i, Settings.RouteChars, _source.Connected, f.DisplayAirport, f.Airports.ContainsKey(i.Destination)), i.Callsign));
            if (lines.Count == 0) lines.Add(("NONE", ""));
            return lines;
        }

        private Size PaintStrips(Painter p, double x0, double y0)
        {
            var m = M;
            int pitch = m.TitleH;           // 17 at FONT 2
            int chars = StripFormat.Width(Settings.RouteChars);
            double width = LeftPad + m.TextWidth(chars) + EntryGap;

            var lines = BuildStripLines();
            int max = Math.Max(1, Settings.Lines);
            int maxScroll = Math.Max(0, lines.Count - max);
            _scroll = Math.Clamp(_scroll, 0, maxScroll);
            int count = Math.Min(max, lines.Count - _scroll);
            double contentH = (count - 1) * pitch + m.GlyphTopInRow + m.Cap;

            if (p.Drawing)
            {
                var tb = TextBrush;
                for (int v = 0; v < count; v++)
                {
                    var (text, key) = lines[_scroll + v];
                    double top = v * pitch;
                    // Line box: the glyphs plus 3 px above and below (17 px at FONT 2, so boxes touch).
                    double boxY = y0 + top + m.GlyphTopInRow - 3, boxH = pitch;
                    if (key.Length > 0)
                    {
                        if (_highlighted.Contains(key)) p.Fill(Eram.Highlight, x0 + 1, boxY, width - 2, boxH);
                        p.Hit(x0, boxY, width, boxH, _ => ToggleHighlight(key));
                    }
                    if (v == count - 1 && _scroll + count < lines.Count) text = text.PadRight(chars - 1).Substring(0, chars - 1) + ">";
                    p.Text(text, x0 + LeftPad, y0 + top + m.GlyphTopInRow, m, tb);
                }
            }
            return new Size(width, contentH);
        }
    }
}
