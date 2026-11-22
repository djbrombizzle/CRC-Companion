using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// CFR, the Continuous Flight Plan Readout view (ERAM manual ch. 8): up to ten flight plans, kept up to date
    /// from VATSIM. Columns FLID TYP BCN SPD FIX TIM ALT RTE/RMK, a rule under each entry, title bar DEL ALL and
    /// SHOW FIELDS. A non-ADS-B flight has a coral "A" before its FLID; a new readout's FLID is green for a while.
    /// Add with QF R &lt;FLID&gt; (left-click the view). Middle-click a FLID to delete it. Boxed * shows the rest of
    /// the route, boxed R the remarks. M menu: ENTRIES 1-10, RTE 30-100, FILTERS to show or hide columns.
    /// CID and the owning sector "(32)" come from the vUSALink EDST app, which learns them from CRC QF readouts.
    /// </summary>
    public sealed class CfrView : DataListView
    {
        private static readonly TimeSpan GreenFor = TimeSpan.FromSeconds(30);

        private readonly Dictionary<string, DateTime> _addedUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Readout> _last = new Dictionary<string, Readout>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _moreRoute = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _moreRemarks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer _greenTimer;
        private bool _showAll;

        public CfrView(IViewHost h, ViewSettings s, ILiveSource l) : base(h, s, "CFR", l)
        {
            // Earlier versions used this view for call-for-release airports; start clean.
            if (!Settings.Values.ContainsKey("cfr"))
            {
                Settings.Items.Clear();
                Settings.Values["cfr"] = "2";
                Host.MarkDirty();
            }
            _greenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _greenTimer.Tick += (_, _) =>
            {
                var now = DateTime.UtcNow;
                var done = _addedUtc.Where(kv => now - kv.Value >= GreenFor).Select(kv => kv.Key).ToList();
                foreach (var k in done) _addedUtc.Remove(k);
                if (done.Count > 0) Surface.Refresh();
                if (_addedUtc.Count == 0) _greenTimer.Stop();
            };
            Closed += (_, _) => _greenTimer.Stop();
        }

        protected override string MenuTitle => "CFR";
        protected override int BodyPadding => 0;
        protected override List<DataLine> BuildLines() => new List<DataLine>();

        // ---------- settings ----------

        private int Entries
        {
            get => Value("entries", Cfr.EntriesDefault, Cfr.EntriesMin, Cfr.EntriesMax);
            set
            {
                Settings.Values["entries"] = value.ToString(CultureInfo.InvariantCulture);
                while (Settings.Items.Count > value) Settings.Items.RemoveAt(0);   // oldest go first
                Changed();
            }
        }

        private int Rte
        {
            get => Value("rte", Cfr.RteDefault, Cfr.RteMin, Cfr.RteMax);
            set { Settings.Values["rte"] = value.ToString(CultureInfo.InvariantCulture); Changed(); }
        }

        private int Value(string key, int def, int min, int max) =>
            Settings.Values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? Math.Clamp(n, min, max) : def;

        private HashSet<string> Off => new HashSet<string>(
            (Settings.Values.TryGetValue("off", out var v) ? v : "").Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

        private bool Shown(string field) => _showAll || !Off.Contains(field);

        private void ToggleField(string field)
        {
            var off = Off;
            if (!off.Remove(field)) off.Add(field);
            Settings.Values["off"] = string.Join(",", Cfr.Fields.Where(off.Contains));
            Changed();
        }

        // ---------- title bar & menu ----------

        protected override IReadOnlyList<TitleBarButton> TitleButtons
        {
            get
            {
                bool any = Settings.Items.Count > 0;
                bool nothingHidden = Off.Count == 0 && !Rows().Any(r => r.More);
                return new[]
                {
                    new TitleBarButton("DEL ALL", null, () => { Settings.Items.Clear(); _moreRoute.Clear(); _moreRemarks.Clear(); _showAll = false; Changed(); }),
                    new TitleBarButton("SHOW FIELDS", _showAll ? Eram.Highlight : null, () => { _showAll = !_showAll; Changed(); },
                        Dim: !_showAll && (!any || nothingHidden), ClickWhenDim: false),
                };
            }
        }

        protected override IReadOnlyList<MenuRow> BuildMenuRows() => new List<MenuRow>
        {
            new ModeRow(() => Settings.Opaque, () => { Settings.Opaque = !Settings.Opaque; Changed(); }),
            new ToggleRow("BORDER", () => Settings.Border, () => { Settings.Border = !Settings.Border; Changed(); }),
            new StepRow("ENTRIES", () => Entries, v => Entries = v, Cfr.EntriesMin, Cfr.EntriesMax),
            FontRow(),
            BrightRow(),
            new StepRow("RTE", () => Rte, v => Rte = v, Cfr.RteMin, Cfr.RteMax),
            new CommandRow("FILTERS", _ => OpenFilters()) { Back = Eram.Black },
        };

        private void OpenFilters() =>
            OpenPopup("FILTERS", Cfr.Fields.Select(f => (MenuRow)new ToggleRow(f, () => !Off.Contains(f), () => ToggleField(f), Eram.MenuGreen)).ToList());

        // ---------- entries ----------

        /// <summary>QF R &lt;FLID&gt;: add (or "-FLID" to remove) flight plans.</summary>
        private void OpenQf() =>
            OpenEntry("QF R  FLID / -FLID", () => Settings.Items, text =>
            {
                var before = new HashSet<string>(Settings.Items, StringComparer.OrdinalIgnoreCase);
                var feedback = Cfr.Apply(Settings.Items, text, Entries, cs => FindPlan(cs).Plan != null);
                var now = DateTime.UtcNow;
                foreach (var cs in Settings.Items) if (!before.Contains(cs) || text.ToUpperInvariant().Contains(cs)) _addedUtc[cs] = now;
                if (_addedUtc.Count > 0) _greenTimer.Start();
                Changed();
                return feedback;
            });

        private (Pilot? Pilot, FlightPlan? Plan) FindPlan(string cs)
        {
            var p = Live.FindPilot(cs);
            if (p?.Plan != null) return (p, p.Plan);
            var pf = Live.Snapshot?.Prefiles.FirstOrDefault(x => string.Equals(x.Callsign, cs, StringComparison.OrdinalIgnoreCase));
            return (p, pf?.Plan);
        }

        private sealed class Readout
        {
            public string Acid = "";
            /// <summary>CRC CID and owning sector from the vUSALink EDST app ("" when not learned).</summary>
            public string Cid = "", Sector = "";
            public bool NonAdsb;
            public string Typ = "", Bcn = "", Spd = "", Fix = "", Tim = "", Alt = "";
            public string Route = "";
            public string Remarks = "";
            public bool More;
        }

        private List<Readout> Rows()
        {
            var f = Live.Facility;
            Func<string, string> airport = id => f != null ? f.DisplayAirport(id) : (id.Length == 4 && id[0] == 'K' ? id.Substring(1) : id);
            var rows = new List<Readout>();
            foreach (var cs in Settings.Items.ToList())
            {
                var (pilot, plan) = FindPlan(cs);
                Readout r;
                if (plan == null) r = _last.TryGetValue(cs, out var old) ? old : new Readout { Acid = cs };
                else
                {
                    var (fix, tim) = Cfr.Coordination(pilot, plan, f?.Boundary, Live.FixPosition, airport, DateTime.UtcNow);
                    r = new Readout
                    {
                        Acid = cs.ToUpperInvariant(),
                        NonAdsb = plan.NonAdsb,
                        Typ = plan.TypeSuffix.Length > 0 ? plan.TypeSuffix : plan.Type,
                        Bcn = plan.AssignedSquawk,
                        Spd = plan.CruiseTas > 0 ? plan.CruiseTas.ToString(CultureInfo.InvariantCulture) : "",
                        Fix = fix,
                        Tim = tim,
                        Alt = plan.FiledAltitude,
                        Route = Cfr.Route(plan.Departure, plan.Route, plan.Arrival, airport),
                        Remarks = plan.Remarks,
                    };
                    _last[cs] = r;
                }
                var cidEntry = Live.EdstCid(cs);
                r.Cid = cidEntry?.Cid ?? "";
                r.Sector = cidEntry?.Sector ?? "";
                r.More = !_showAll && Shown("RTE") && r.Route.Length > Rte;
                rows.Add(r);
            }
            return rows;
        }

        // ---------- drawing ----------

        protected override Size PaintBody(Painter p, double x0, double y0)
        {
            var m = M;
            int rowH = m.TitleH;
            int gt = m.GlyphTopInTitle;
            var tb = TextBrush;
            var rows = Rows();
            var now = DateTime.UtcNow;

            bool anyA = rows.Any(r => r.NonAdsb);
            int aChars = anyA ? 2 : 0;
            bool showCid = Shown("CID");
            string Flid(Readout r) => (showCid && r.Cid.Length > 0 ? r.Cid + " " : "") + r.Acid + (r.Sector.Length > 0 ? "(" + r.Sector + ")" : "");
            int flidChars = Math.Max(4, rows.Select(r => Flid(r).Length).DefaultIfEmpty(0).Max()) + 1;

            var cols = new List<(string Label, Func<Readout, string> Get)>();
            if (Shown("TYP")) cols.Add(("TYP", r => r.Typ));
            if (Shown("BCN")) cols.Add(("BCN", r => r.Bcn));
            if (Shown("SPD")) cols.Add(("SPD", r => r.Spd));
            if (Shown("FIX")) cols.Add(("FIX", r => r.Fix));
            if (Shown("TIM")) cols.Add(("TIM", r => r.Tim));
            if (Shown("ALT")) cols.Add(("ALT", r => r.Alt));
            var widths = cols.Select(c => Math.Max(c.Label.Length, rows.Select(r => c.Get(r).Length).DefaultIfEmpty(0).Max()) + 1).ToList();

            int rte = _showAll ? int.MaxValue : Rte;
            bool showRte = Shown("RTE");
            int routeCol = aChars + flidChars + widths.Sum();
            int routeChars = showRte ? Math.Max(7, rows.Select(r => Math.Min(r.Route.Length, rte)).DefaultIfEmpty(0).Max()) : 3;
            int boxChars = rows.Any(r => r.More || r.Remarks.Length > 0) ? 5 : 0;   // " [*] [R]"
            int lineChars = routeCol + routeChars + boxChars;
            double width = LeftPad + m.TextWidth(Math.Max(lineChars, 24)) + EntryGap;

            double y = y0;
            void Rule() { if (p.Drawing) p.Fill(Eram.ViewBorder, x0, y, width, 1); y += 1; }

            // Column labels.
            if (Shown("LBL"))
            {
                if (p.Drawing)
                {
                    string head = new string(' ', aChars) + "FLID".PadRight(flidChars) +
                                  string.Concat(cols.Select((c, i) => c.Label.PadRight(widths[i]))) + (showRte ? "RTE/RMK" : "RMK");
                    p.Text(head, x0 + LeftPad, y + 3 + gt, m, tb);
                }
                p.Hit(x0, y, width, rowH + 4, b => { if (b == MouseButton.Left) OpenQf(); });
                y += rowH + 4;
                Rule();
            }

            if (rows.Count == 0)
            {
                if (p.Drawing) p.Text("QF R FLID: CLICK HERE", x0 + LeftPad, y + 3 + gt, m, Eram.SystemYellow);
                p.Hit(x0, y, width, rowH + 4, _ => OpenQf());
                y += rowH + 4;
                return new Size(width, y - y0);
            }

            for (int ri = 0; ri < rows.Count; ri++)
            {
                var r = rows[ri];
                var acid = r.Acid;
                double top = y;
                double ty = y + 3 + gt;
                double tx = x0 + LeftPad;
                var route = showRte ? Cfr.Cut(r.Route, rte, out _) : "";

                if (p.Drawing)
                {
                    if (r.NonAdsb) p.Text("A", tx, ty, m, Eram.NonRvsm);
                    bool green = _addedUtc.TryGetValue(acid, out var t0) && now - t0 < GreenFor;
                    p.Text(Flid(r), tx + m.TextWidth(aChars), ty, m, green ? Eram.CfrGreen : tb);
                    var rest = string.Concat(cols.Select((c, i) => Fit(c.Get(r), widths[i]))) + route;
                    p.Text(rest, tx + m.TextWidth(aChars + flidChars), ty, m, tb);
                }
                // Whole row: left = QF entry; FLID: middle = delete.
                p.Hit(x0, top, width, rowH + 4, b => { if (b == MouseButton.Left) OpenQf(); });
                p.Hit(tx + m.TextWidth(aChars) - 3, top, m.TextWidth(Flid(r).Length) + 6, rowH + 4, b =>
                {
                    if (b == MouseButton.Middle) OpenPopup("CFR", new List<MenuRow> { new CommandRow("DELETE " + acid, _ => { Remove(acid); ClosePopup(); }) });
                    else OpenQf();
                });

                // Boxed * (rest of the route) and R (remarks).
                double bx = tx + m.TextWidth(routeCol + route.Length + 1);
                if (r.More)
                {
                    Box(p, m, bx, top, "*", _moreRoute.Contains(acid), () => ToggleSet(_moreRoute, acid));
                    bx += m.TextWidth(2) + 2;
                }
                if (r.Remarks.Length > 0)
                    Box(p, m, bx, top, "R", _moreRemarks.Contains(acid), () => ToggleSet(_moreRemarks, acid));
                y += rowH + 4;

                // Expansions, under the route column.
                var extra = new List<string>();
                if (r.More && _moreRoute.Contains(acid)) extra.AddRange(Chunks(r.Route.Substring(Math.Min(r.Route.Length, rte)), Math.Max(routeChars, 30)));
                if (_moreRemarks.Contains(acid) && r.Remarks.Length > 0) extra.AddRange(Metar.Wrap(r.Remarks, Math.Max(routeChars, 30)));
                foreach (var line in extra)
                {
                    if (p.Drawing) p.Text(line, tx + m.TextWidth(showRte ? routeCol : aChars + flidChars), y + gt, m, tb);
                    y += rowH;
                }
                if (extra.Count > 0) y += 3;
                Rule();
            }
            return new Size(width, y - y0);
        }

        private static IEnumerable<string> Chunks(string s, int n)
        {
            for (int i = 0; i < s.Length; i += n) yield return s.Substring(i, Math.Min(n, s.Length - i));
        }

        private void Box(Painter p, EramMetrics m, double x, double top, string label, bool on, Action toggle)
        {
            double w = m.TextWidth(1) + 6, h = m.TitleH;
            if (p.Drawing)
            {
                if (on) p.Fill(Eram.Highlight, x, top + 2, w, h);
                p.Frame(Eram.ViewBorder, x, top + 2, w, h, 1);
                p.Text(label, x + 3, top + 3 + m.GlyphTopInTitle, m, TextBrush);
            }
            p.Hit(x, top + 2, w, h, _ => toggle());
        }

        private void ToggleSet(HashSet<string> set, string key)
        {
            if (!set.Remove(key)) set.Add(key);
            Changed();
        }

        private void Remove(string acid)
        {
            Settings.Items.RemoveAll(i => string.Equals(i, acid, StringComparison.OrdinalIgnoreCase));
            _moreRoute.Remove(acid);
            _moreRemarks.Remove(acid);
            Changed();
        }
    }
}
