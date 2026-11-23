using System;
using System.Collections.Generic;
using System.Windows;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// ERAM Altimeter Settings View, laid out to CRC's measured geometry:
    /// [marker 13x17] 11 px [STN___HHMM_ALT] 11 px, columns 203 px apart at FONT 2.
    /// Time is underlined when stale (&gt;65 min), the setting when below 29.92;
    /// -M- when the report is missing or older than 120 min.
    /// Entries fill LINES rows per column, across COL columns.
    /// </summary>
    public sealed class AltimViewWindow : ListViewWindow
    {
        // "HGR   1753 030": 6 (station) + 4 (time) + 1 + 3 (altimeter), as in CRC
        private const int StationChars = 6;
        private const int TextChars = StationChars + 4 + 1 + 3;

        public AltimViewWindow(IViewHost host, ViewSettings settings) : base(host, settings, "ALTIM SET") { }

        protected override string MenuTitle => "AS";

        protected override IReadOnlyList<MenuRow> BuildMenuRows()
        {
            var rows = CommonRowsHead();
            rows.Add(LinesRow());
            rows.Add(new StepRow("COL", () => Settings.Cols, v => { Settings.Cols = v; Changed(); }, Limits.ColsMin, Limits.ColsMax));
            rows.Add(FontRow());
            rows.Add(BrightRow());
            rows.Add(new CommandRow("TEMPLATE", null)); // not simulated in CRC either
            rows.Add(StationsRow());
            return rows;
        }

        protected override Size PaintBody(Painter p, double x0, double y0)
        {
            var m = M;
            int markerBlock = Settings.Tearoff ? m.MarkerW + EntryGap : 0;
            int entryW = markerBlock + m.TextWidth(TextChars) + EntryGap;

            // Shown in alphabetical order, whatever order they were entered in.
            var stations = System.Linq.Enumerable.ToList(
                System.Linq.Enumerable.OrderBy(Settings.Stations, s => s, StringComparer.OrdinalIgnoreCase));
            int lines = Math.Max(1, Settings.Lines);
            int capacity = lines * Math.Max(1, Settings.Cols);
            int shown = Math.Min(stations.Count, capacity);
            int cols = Math.Max(1, (int)Math.Ceiling(shown / (double)lines));
            int rows = shown == 0 ? 1 : Math.Min(lines, shown);

            var now = DateTime.UtcNow;
            var tb = TextBrush;

            for (int i = 0; i < shown; i++)
            {
                int col = i / lines, row = i % lines;
                double ex = x0 + LeftPad + col * entryW;
                double ey = y0 + row * m.Row;
                var id = stations[i];
                var st = StationState(id, now);

                string alt;
                bool altUnderline = false;
                if (!st.Fetched) alt = "   ";
                else if (st.Report == null || st.Expired || st.Report.AltimeterHundredths == null) alt = "-M-";
                else
                {
                    var hundredths = st.Report.AltimeterHundredths.Value;
                    alt = Metar.FormatAltimeter(hundredths);
                    altUnderline = hundredths < Metar.StandardHundredths;
                }

                if (Settings.Tearoff) DrawMarker(p, ex, ey);

                double tx = ex + markerBlock, top = ey + m.GlyphTopInRow;
                p.Text(id.PadRight(StationChars) + st.Time + " " + alt, tx, top, m, tb);
                if (st.Stale) p.Underline(tx, top, m, StationChars, 4, tb);
                if (altUnderline) p.Underline(tx, top, m, StationChars + 5, 3, tb);

                p.Hit(ex, ey, entryW, m.Row, _ => OpenDeletePopup(id));
            }

            double width = LeftPad + cols * entryW + RightPad;
            double height = (rows - 1) * m.Row + m.MarkerH;
            return new Size(width, height);
        }
    }
}
