using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// MRP (Meter Reference Point) view, drawn like ERAM's (TI 6110.100 figures 4-220 to 4-224), fed by VATFLOW's
    /// FCA programs for your ARTCC. Each program is a header line (destination and restriction, "DCA MIT 35") and a
    /// sublist titled with its metering fix in the sublist's color; each line is a flight in the program:
    /// indicators ('H' = holding), AID, meter time (scheduled crossing, HHMM) and DCT (delay to absorb, MIN or SEC
    /// format per sublist), latest at the top. SPEED adds the (empty) GIM-S speed column.
    /// Left-click AIDs to pick a re-sequence, middle-click the last to apply it (up to five, one program at a time).
    /// Middle-click an AID with nothing picked to suppress it.
    /// </summary>
    public sealed class MrpView : ListViewWindow
    {
        private readonly ILiveSource _live;
        private int _scroll;

        // Local state (this session): picks for a re-sequence, local orders, suppressed entries.
        private string _pickFca = "";
        private readonly List<string> _picks = new List<string>();
        private readonly Dictionary<string, List<string>> _localOrder = new Dictionary<string, List<string>>();
        private readonly HashSet<string> _suppressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _deleteMode;
        private int _colorPick = -1;

        // Cached sequences, recomputed when the source's data changes or a local order changes.
        private int _version = -1;
        private List<FcaSequence> _seqs = new List<FcaSequence>();
        private DateTime _computedUtc;

        public const int MaxResequence = 5;
        private static readonly string[] ColorKeys = { "G", "C", "W", "Y" };
        private static readonly Color[] Palette =
        {
            Color.FromRgb(0x00, 0xC8, 0x5A),   // green
            Color.FromRgb(0xE0, 0x7A, 0x70),   // burnt coral
            Color.FromRgb(0xF0, 0xF0, 0xF0),   // white
            Color.FromRgb(0xF0, 0xE6, 0x00),   // yellow
        };

        // Columns (characters): indicators 0-2, AID 3-10, meter time 13-16, DCT right-aligned to 23, speed 26-34.
        private const int AidCol = 3, TimeCol = 13, DctEnd = 24, SpeedCol = 26, SpeedChars = 9;

        public MrpView(IViewHost host, ViewSettings settings, ILiveSource live) : base(host, settings, "MRP")
        {
            _live = live;
        }

        protected override string MenuTitle => "MRP";

        private bool Speed => Settings.Values.TryGetValue("speed", out var v) && v == "1";
        /// <summary>Only programs enabled in VATFLOW (default); M → ACTIVE off shows disabled ones too.</summary>
        private bool ActiveOnly => !(Settings.Values.TryGetValue("active", out var v) && v == "0");
        private int ColorBright
        {
            get => Settings.Values.TryGetValue("colorBright", out var v) && int.TryParse(v, out var b) ? Math.Clamp(b, 10, 100) : 90;
            set => Settings.Values["colorBright"] = Math.Clamp(value, 10, 100).ToString(CultureInfo.InvariantCulture);
        }

        private Brush PaletteBrush(int i)
        {
            var c = Palette[Math.Clamp(i, 0, 3)];
            double k = ColorBright / 100.0;
            var b = new SolidColorBrush(Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k)));
            b.Freeze();
            return b;
        }

        /// <summary>The sublist's color: assigned in the menu, else the palette color nearest the program's VATFLOW color.</summary>
        private int ColorIndex(FcaProgram f)
        {
            if (Settings.Values.TryGetValue("color." + f.Id, out var k))
            {
                int i = Array.IndexOf(ColorKeys, k);
                if (i >= 0) return i;
            }
            return NearestPalette(f.Color);
        }

        public static int NearestPalette(string hex)
        {
            hex = (hex ?? "").Trim().TrimStart('#');
            if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return 2;
            int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
            int best = 2;
            double bd = double.MaxValue;
            for (int i = 0; i < Palette.Length; i++)
            {
                double d = Math.Pow(r - Palette[i].R, 2) + Math.Pow(g - Palette[i].G, 2) + Math.Pow(b - Palette[i].B, 2);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        private bool SecFormat(FcaProgram f) => Settings.Values.TryGetValue("dct." + f.Id, out var v) && v == "SEC";

        /// <summary>VATFLOW programs for your ARTCC, minus the ones you deleted (and disabled ones with ACTIVE on).</summary>
        private List<FcaProgram> Programs() =>
            _live.FcaPrograms
                .Where(f => !Settings.Items.Contains(f.Id, StringComparer.OrdinalIgnoreCase) && (!ActiveOnly || f.Enabled))
                .OrderBy(f => f.Dests.FirstOrDefault() ?? "", StringComparer.Ordinal).ThenBy(f => f.FixTitle, StringComparer.Ordinal)
                .ToList();

        private void Invalidate() { _version = -1; Surface.Refresh(); }

        private List<FcaSequence> Sequences()
        {
            if (_version != _live.MrpVersion)
            {
                _version = _live.MrpVersion;
                _computedUtc = DateTime.UtcNow;
                _seqs = Programs().Select(f => _live.Sequence(f, _localOrder.TryGetValue(f.Id, out var o) ? o : null)).ToList();
            }
            return _seqs;
        }

        // ---------------- menu ----------------

        protected override IReadOnlyList<MenuRow> BuildMenuRows()
        {
            var rows = new List<MenuRow>
            {
                new ModeRow(() => Settings.Opaque, () => { Settings.Opaque = !Settings.Opaque; Changed(); }),
                new ToggleRow("BORDER", () => Settings.Border, () => { Settings.Border = !Settings.Border; Changed(); }),
                new ToggleRow("TEAROFF", () => Settings.Tearoff, () => { Settings.Tearoff = !Settings.Tearoff; Changed(); }),
                LinesRow(),
                FontRow(),
                BrightRow(),
                // RESEQ lights while a re-sequence is being picked; selecting it cancels the picks.
                new ToggleRow("RESEQ", () => _picks.Count > 0, () => { _picks.Clear(); _pickFca = ""; Changed(); }, Eram.MrpCoral, Eram.MrpBlue),
                new ToggleRow("SPEED", () => Speed, () => { Settings.Values["speed"] = Speed ? "0" : "1"; Changed(); }),
                new ToggleRow("DELETE", () => _deleteMode, () => { _deleteMode = !_deleteMode; }, Eram.MrpCoral, Eram.MrpBlue),
                new StepRow("COLOR", () => ColorBright, v => { ColorBright = v; Invalidate(); Host.MarkDirty(); ReopenMenu(); }, 10, 100, 2)
                    { Back = Eram.Black, Fore = _colorPick >= 0 ? PaletteBrush(_colorPick) : Eram.MenuText },
                new SwatchRow(Enumerable.Range(0, 4).Select(PaletteBrush).ToList(), () => _colorPick,
                    i => { _colorPick = _colorPick == i ? -1 : i; ReopenMenu(); }),
            };
            foreach (var f in Programs())
            {
                var fca = f;
                rows.Add(new SublistRow(fca.FixTitle, () => SecFormat(fca) ? "SEC" : "MIN",
                    () => PickSublist(fca),
                    () => { Settings.Values["dct." + fca.Id] = SecFormat(fca) ? "MIN" : "SEC"; Changed(); })
                    { Fore = PaletteBrush(ColorIndex(fca)) });
            }
            // Companion extras.
            rows.Add(new ToggleRow("ACTIVE", () => ActiveOnly, () => { Settings.Values["active"] = ActiveOnly ? "0" : "1"; Invalidate(); Host.MarkDirty(); ReopenMenu(); }));
            rows.Add(new CommandRow("RESTORE", _ =>
            {
                Settings.Items.Clear(); _suppressed.Clear(); _localOrder.Clear(); _picks.Clear(); _pickFca = "";
                Invalidate(); Host.MarkDirty(); ReopenMenu();
            }));
            return rows;
        }

        private void PickSublist(FcaProgram f)
        {
            if (_deleteMode)
            {
                Settings.Items.Add(f.Id);
                _localOrder.Remove(f.Id);
                Invalidate();
                Host.MarkDirty();
                ReopenMenu();
            }
            else if (_colorPick >= 0)
            {
                Settings.Values["color." + f.Id] = ColorKeys[_colorPick];
                Invalidate();
                Host.MarkDirty();
                ReopenMenu();
            }
        }

        private void ReopenMenu()
        {
            if (!MenuOpen) return;
            bool delete = _deleteMode;
            ToggleMenu();
            ToggleMenu();
            _deleteMode = delete;
        }

        protected override void OnBodyWheel(int delta)
        {
            _scroll += delta > 0 ? -1 : 1;
            Surface.Refresh();
        }

        // ---------------- picks: re-sequence and suppress ----------------

        private void ClickAid(FcaSequence s, FcaItem item, MouseButton button)
        {
            var cs = item.Pilot.Callsign;
            if (button == MouseButton.Middle && _picks.Count == 0)
            {
                var key = s.Fca.Id + "|" + cs;
                OpenPopup(cs, new List<MenuRow> { new CommandRow("SUPPRESS", b => { _suppressed.Add(key); ClosePopup(); Changed(); }) });
                return;
            }
            if (_pickFca != s.Fca.Id) { _picks.Clear(); _pickFca = s.Fca.Id; }
            if (!_picks.Contains(cs, StringComparer.OrdinalIgnoreCase) && _picks.Count < MaxResequence) _picks.Add(cs);
            if (button == MouseButton.Middle)
            {
                // Sequence order = crossing order (earliest first).
                var current = s.Items.Select(i => i.Pilot.Callsign).ToList();
                _localOrder[s.Fca.Id] = FcaEngine.Resequence(current, _picks.ToList());
                _picks.Clear();
                _pickFca = "";
                Invalidate();
                return;
            }
            Surface.Refresh();
        }

        // ---------------- body ----------------

        private sealed class Line
        {
            public string Text = "";
            public Brush? Color;
            public bool Header, Title, Gap;
            public FcaSequence? Seq;
            public FcaItem? Item;
            public string Dct = "";
        }

        private List<Line> BuildLines(out string status)
        {
            status = "";
            var lines = new List<Line>();
            if (_live.Facility == null) { status = _live.StatusText.Length > 0 ? _live.StatusText : "NO FACILITY"; return lines; }
            if (_live.FcaStatus.Length > 0) status = _live.FcaStatus;
            var seqs = Sequences();
            if (seqs.Count == 0 && status.Length == 0) status = "NO FCAS FOR " + _live.FcaArtcc;
            foreach (var s in seqs)
            {
                var color = PaletteBrush(ColorIndex(s.Fca));
                if (lines.Count > 0) lines.Add(new Line { Gap = true });
                lines.Add(new Line { Text = s.Fca.Header, Header = true, Seq = s });
                lines.Add(new Line { Text = s.Fca.FixTitle, Title = true, Color = color, Seq = s });
                bool sec = SecFormat(s.Fca);
                foreach (var it in s.Items.Where(i => !_suppressed.Contains(s.Fca.Id + "|" + i.Pilot.Callsign)).OrderByDescending(i => i.SchedSec))
                    lines.Add(new Line { Seq = s, Item = it, Dct = FcaEngine.FormatDct(FcaEngine.DelayToAbsorb(it), sec) });
            }
            return lines;
        }

        protected override Size PaintBody(Painter p, double x0, double y0)
        {
            var m = M;
            int pitch = m.Row;
            int chars = Speed ? SpeedCol + SpeedChars + 1 : DctEnd + 2;
            double tx = x0 + LeftPad;
            double width = LeftPad + m.TextWidth(chars) + EntryGap;
            var tb = TextBrush;

            var lines = BuildLines(out var status);
            double y = 0;
            if (status.Length > 0)
            {
                if (p.Drawing) p.Text(status, tx, y0 + m.GlyphTopInRow, m, Eram.SystemYellow);
                width = Math.Max(width, LeftPad + m.TextWidth(status.Length) + EntryGap);
                y += pitch;
            }
            if (lines.Count == 0) return new Size(width, Math.Max(y, m.GlyphTopInRow + m.Cap + 3));

            int max = Math.Max(1, Settings.Lines);
            int maxScroll = Math.Max(0, lines.Count - max);
            _scroll = Math.Clamp(_scroll, 0, maxScroll);
            var shown = lines.Skip(_scroll).Take(max).ToList();
            var now = _computedUtc == default ? DateTime.UtcNow : _computedUtc;

            foreach (var line in shown)
            {
                if (line.Gap) { y += pitch / 2; continue; }
                double top = y0 + y + m.GlyphTopInRow;
                if (p.Drawing)
                {
                    if (line.Header)
                        p.Text(line.Text, tx + m.TextWidth(AidCol + 3), top, m, tb);
                    else if (line.Title)
                    {
                        if (Settings.Tearoff) DrawMarker(p, tx, top - (m.MarkerH - m.Cap) / 2.0);
                        p.TextCentered(line.Text, tx, m.TextWidth(chars), top, m, line.Color ?? tb);
                    }
                    else if (line.Item != null && line.Seq != null)
                    {
                        var it = line.Item;
                        var cs = it.Pilot.Callsign;
                        bool picked = _pickFca == line.Seq.Fca.Id && _picks.Contains(cs, StringComparer.OrdinalIgnoreCase);
                        if (picked) p.Fill(Eram.Highlight, tx + m.TextWidth(AidCol) - 2, top - 3, m.TextWidth(8) + 4, m.Cap + 6);
                        string ind = _live.Tracker.IsHolding(cs) ? "H" : " ";
                        p.Text(ind, tx, top, m, tb);
                        p.Text(cs, tx + m.TextWidth(AidCol), top, m, tb);
                        p.Text(now.AddSeconds(it.SchedSec).ToString("HHmm", CultureInfo.InvariantCulture), tx + m.TextWidth(TimeCol), top, m, tb);
                        p.Text(line.Dct, tx + m.TextWidth(DctEnd - line.Dct.Length), top, m, tb);
                        var seq = line.Seq;
                        p.Hit(tx + m.TextWidth(AidCol) - 2, top - 4, m.TextWidth(8) + 4, pitch, b => ClickAid(seq, it, b));
                    }
                }
                y += pitch;
            }
            if (p.Drawing && _scroll + shown.Count < lines.Count)
                p.Text(">", tx + m.TextWidth(chars - 1), y0 + y - pitch + m.GlyphTopInRow, m, tb);
            return new Size(width, y - pitch + m.GlyphTopInRow + m.Cap + 4);
        }
    }
}
