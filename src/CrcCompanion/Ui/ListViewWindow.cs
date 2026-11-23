using System;
using System.Collections.Generic;
using System.Windows;
using CrcCompanion.Core;
using CrcCompanion.Services;

namespace CrcCompanion.Ui
{
    public interface IViewHost
    {
        MetarService Metar { get; }
        double UiScale { get; }
        /// <summary>A setting or position changed: schedule a profile save.</summary>
        void MarkDirty();
        void StationsChanged(ListViewWindow view);
        void SetViewVisible(ListViewWindow view, bool visible);
    }

    /// <summary>
    /// Shared ERAM list-view chrome, measured from CRC at FONT 2 (screen pixels):
    /// 1 px border, 17 px title bar with 23 px "M" and "-" cells, 1 px rule, 8 px body padding,
    /// 21 px rows. Also owns the M menu, the station entry box and the delete popup.
    /// </summary>
    /// <summary>A title bar button. Dim draws the label grayed out; a dim button ignores clicks unless ClickWhenDim.</summary>
    public sealed record TitleBarButton(string Label, System.Windows.Media.Brush? Back, Action Run, bool Dim = false, bool ClickWhenDim = true);

    public abstract class ListViewWindow : EramWindow
    {
        protected readonly IViewHost Host;
        public ViewSettings Settings { get; }
        public string ViewTitle { get; }
        protected abstract string MenuTitle { get; }

        private MenuWindow? _menu;
        private MenuWindow? _popup;
        private StationEntryWindow? _entry;

        /// <summary>Gap between the left border and the first marker.</summary>
        protected const int LeftPad = 6;
        /// <summary>Gap between a marker and its text, and after each entry's text.</summary>
        protected const int EntryGap = 11;
        /// <summary>Extra space after the last column, before the right border.</summary>
        protected const int RightPad = 13;
        /// <summary>Body padding above the first row and below the last.</summary>
        protected const int BodyPad = 8;

        protected ListViewWindow(IViewHost host, ViewSettings settings, string title)
        {
            Host = host;
            Settings = settings;
            ViewTitle = title;
            Surface.Paint = Paint;
            Surface.Wheel = (d, _) => OnBodyWheel(d);
            Repositioned += _ => SavePosition();
            Moved += _ => FollowView();
            Closed += (_, _) => CloseChildren();
        }

        /// <summary>Where the pop-up and entry box sit relative to the view's top-left, so they move with it.</summary>
        private (int X, int Y) _popupOffset, _entryOffset;

        /// <summary>Keeps the M menu, the pop-up and the entry box attached while the view moves.</summary>
        private void FollowView()
        {
            var r = ScreenRect;
            if (_menu != null)
            {
                var mr = _menu.ScreenRect;
                _menu.MoveTo(r.Left - (mr.Right - mr.Left), r.Top);
            }
            _popup?.MoveTo(r.Left + _popupOffset.X, r.Top + _popupOffset.Y);
            if (_entry != null)
                Native.MoveWindow(new System.Windows.Interop.WindowInteropHelper(_entry).Handle, r.Left + _entryOffset.X, r.Top + _entryOffset.Y);
        }

        public void SavePosition()
        {
            var (x, y) = ScreenPosition;
            Settings.X = x;
            Settings.Y = y;
            Host.MarkDirty();
        }

        public void Refresh()
        {
            Surface.Refresh();
            _menu?.Refresh();
        }

        public void CloseChildren()
        {
            _menu?.Close();
            _popup?.Close();
            _entry?.Close();
        }

        protected EramMetrics M => new EramMetrics(Settings.Font);
        protected System.Windows.Media.Brush TextBrush => Eram.TextGray(Settings.Bright);

        /// <summary>
        /// Paints the body (rows only, not its padding) with its top-left content corner at (x, y),
        /// where x is the inner left edge (just inside the border). Returns the content size,
        /// width including its own right padding.
        /// </summary>
        protected abstract Size PaintBody(Painter p, double x, double y);

        protected virtual void OnBodyWheel(int delta) { }

        /// <summary>Title bar text (the view name unless a view adds to it, e.g. "HISTORY UAL1026").</summary>
        protected virtual string TitleText => ViewTitle;

        /// <summary>Optional title bar button drawn left of "-" (e.g. HISTORY's DEL ALL).</summary>
        protected virtual (string Label, System.Windows.Media.Brush Back, Action Run)? TitleButton => null;

        /// <summary>Draw the title bar button's label dimmed (nothing for it to do).</summary>
        protected virtual bool TitleButtonDim => false;

        /// <summary>Title bar buttons, left to right, drawn left of "-" (CFR has DEL ALL and SHOW FIELDS).</summary>
        protected virtual IReadOnlyList<TitleBarButton> TitleButtons =>
            TitleButton is { } b ? new[] { new TitleBarButton(b.Label, b.Back, b.Run, TitleButtonDim) } : Array.Empty<TitleBarButton>();

        /// <summary>Space above and below the body rows. Views that draw their own panels use 0.</summary>
        protected virtual int BodyPadding => BodyPad;

        protected abstract IReadOnlyList<MenuRow> BuildMenuRows();

        /// <summary>Draws one outlined tear-off marker with its top-left at (x, y).</summary>
        protected void DrawMarker(Painter p, double x, double y)
        {
            var m = M;
            p.Fill(Eram.MarkerOutline, x, y, m.MarkerW, m.MarkerH);
            p.Fill(Eram.Marker, x + 1, y + 1, m.MarkerW - 2, m.MarkerH - 2);
        }

        private Size Paint(Painter p)
        {
            var m = M;
            int th = m.TitleH, box = m.BoxW;
            double bodyTop = 2 + th;                 // border + title + rule
            int pad = BodyPadding;
            var title = TitleText;
            var buttons = TitleButtons;
            var widths = new double[buttons.Count];
            double buttonsW = 0;
            for (int i = 0; i < buttons.Count; i++) { widths[i] = m.TextWidth(buttons[i].Label.Length) + 2 * m.Cell; buttonsW += widths[i] + 1; }

            var body = PaintBody(p.MeasureOnly(), 1, bodyTop + pad);
            double minW = 2 + box * 2 + 2 + m.TextWidth(title.Length) + 2 * m.Cell + buttonsW;
            double w = Math.Max(minW, 1 + body.Width + 1);
            double h = bodyTop + pad + body.Height + pad + 1;

            p.Fill(Settings.Opaque ? Eram.Black : Eram.HalfBlack, 0, 0, w, h);

            // Title bar: [M][   TITLE   ][buttons][-]
            var tb = TextBrush;
            double gt = 1 + m.GlyphTopInTitle;
            p.TextCentered("M", 1, box, gt, m, tb);
            p.Fill(Eram.ViewBorder, 1 + box, 1, 1, th);
            double titleX = 2 + box, titleW = w - 4 - box * 2 - buttonsW;
            p.TextCentered(title, titleX, titleW, gt, m, tb);
            double bx = w - 2 - box - buttonsW + 1;
            for (int i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i];
                p.Fill(Eram.ViewBorder, bx - 1, 1, 1, th);
                if (b.Back != null) p.Fill(b.Back, bx, 1, widths[i], th);
                p.TextCentered(b.Label, bx, widths[i], gt, m, b.Dim ? Eram.TextGray(Settings.Bright / 2) : tb);
                var run = b.Run;
                if (!b.Dim || b.ClickWhenDim) p.Hit(bx, 1, widths[i], th, _ => run());
                bx += widths[i] + 1;
            }
            p.Fill(Eram.ViewBorder, w - 2 - box, 1, 1, th);
            p.TextCentered("-", w - 1 - box, box, gt, m, tb);
            p.Fill(Eram.ViewBorder, 1, 1 + th, w - 2, 1);

            p.Hit(1, 1, box, th, _ => ToggleMenu());
            p.Hit(titleX, 1, titleW, th, _ => BeginReposition());
            p.Hit(w - 1 - box, 1, box, th, _ => Host.SetViewVisible(this, false));

            if (p.Drawing) PaintBody(p, 1, bodyTop + pad);

            if (Settings.Border) p.Frame(Eram.ViewBorder, 0, 0, w, h, 1);
            return new Size(w, h);
        }

        // ---------- Menu ----------

        protected bool MenuOpen => _menu != null;

        public void ToggleMenu()
        {
            if (_menu != null) { _menu.Close(); return; }
            _menu = new MenuWindow(MenuTitle, BuildMenuRows()) { TitleDrag = BeginReposition };
            _menu.SetScale(Host.UiScale);
            _menu.Closed += (_, _) => _menu = null;
            var r = ScreenRect;
            // CRC opens the menu against the view's left edge, top-aligned.
            _menu.ShowAt(r.Left, r.Top);
            var mr = _menu.ScreenRect;
            _menu.MoveTo(r.Left - (mr.Right - mr.Left), r.Top);
        }

        protected List<MenuRow> CommonRowsHead() => new List<MenuRow>
        {
            new ModeRow(() => Settings.Opaque, () => { Settings.Opaque = !Settings.Opaque; Changed(); }),
            new ToggleRow("BORDER", () => Settings.Border, () => { Settings.Border = !Settings.Border; Changed(); }),
            new ToggleRow("TEAROFF", () => Settings.Tearoff, () => { Settings.Tearoff = !Settings.Tearoff; Changed(); }),
        };

        protected StepRow LinesRow() => new StepRow("LINES", () => Settings.Lines, v => { Settings.Lines = v; Changed(); }, Limits.LinesMin, Limits.LinesMax);
        protected StepRow FontRow() => new StepRow("FONT", () => Settings.Font, v => { Settings.Font = v; Changed(); }, Limits.FontMin, Limits.FontMax);
        protected StepRow BrightRow() => new StepRow("BRIGHT", () => Settings.Bright, v => { Settings.Bright = v; Changed(); }, Limits.BrightMin, Limits.BrightMax, Limits.BrightStep);
        protected CommandRow StationsRow() => new CommandRow("STATIONS", _ => OpenStationEntry());

        protected void Changed()
        {
            Surface.Refresh();
            Host.MarkDirty();
        }

        // ---------- Stations ----------

        public void OpenStationEntry() =>
            OpenEntry(MenuTitle + " STATIONS", () => Settings.Stations, text =>
            {
                var result = StationIds.Apply(Settings.Stations, text);
                if (result.Added.Count > 0 || result.Removed.Count > 0) Host.StationsChanged(this);
                return StationIds.Feedback(result);
            });

        /// <summary>
        /// One-line typed entry under the view (the station entry box, reused for other commands).
        /// <paramref name="apply"/> handles a line and returns feedback.
        /// </summary>
        protected void OpenEntry(string title, Func<IReadOnlyList<string>> current, Func<string, string> apply)
        {
            if (_entry != null) { _entry.Activate(); return; }
            var previous = Native.GetForegroundWindow();
            _entry = new StationEntryWindow(title, current, apply, previous, Host.UiScale);
            _entry.Closed += (_, _) => _entry = null;
            var r = ScreenRect;
            _entry.ShowNear(r.Left, r.Bottom + 4);
            _entryOffset = (0, r.Bottom + 4 - r.Top);
        }

        /// <summary>Popup with a DELETE row for one station, as in CRC.</summary>
        protected void OpenDeletePopup(string station) =>
            OpenPopup(station, new List<MenuRow>
            {
                new CommandRow("DELETE", _ =>
                {
                    Settings.Stations.Remove(station);
                    Host.StationsChanged(this);
                    ClosePopup();
                }),
            });

        /// <summary>Small ERAM menu at the cursor (one at a time per view).</summary>
        protected void OpenPopup(string title, List<MenuRow> rows, Action? onClosed = null)
        {
            _popup?.Close();
            var popup = new MenuWindow(title, rows) { TitleDrag = BeginReposition };
            _popup = popup;
            popup.SetScale(Host.UiScale);
            popup.Closed += (_, _) => { if (ReferenceEquals(_popup, popup)) _popup = null; onClosed?.Invoke(); };
            Native.GetCursorPos(out var c);
            popup.ShowAt(c.X + 8, c.Y + 8);
            var r = ScreenRect;
            _popupOffset = (c.X + 8 - r.Left, c.Y + 8 - r.Top);
        }

        protected void ClosePopup() => _popup?.Close();

        /// <summary>Station state shared by both views.</summary>
        protected (string Time, MetarReport? Report, bool Fetched, bool Stale, bool Expired) StationState(string id, DateTime now)
        {
            var fetched = Host.Metar.HasFetched(id);
            var rep = Host.Metar.Get(id);
            if (rep == null) return ("    ", null, fetched, false, false);
            return (Metar.FormatTime(rep.ObservedUtc), rep, true, Metar.IsStale(rep, now), Metar.IsExpired(rep, now));
        }
    }
}
