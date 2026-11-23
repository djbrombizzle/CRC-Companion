using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CrcCompanion.Core;
using CrcCompanion.Services;

namespace CrcCompanion.Ui
{
    /// <summary>Toolbar-button drawing shared by the RECORD toolbar and macro buttons (same 94 x 32 cell as VIEWS).</summary>
    internal static class TbCell
    {
        public const int W = 94, H = 32, Gap = 2, FrameT = 2, BarX = 1, BarW = 11, BodyX = 14, BodyY = 1;
        public const int BodyW = ToolbarLabels.Width, BodyH = ToolbarLabels.Height;
        public static readonly EramMetrics M = new EramMetrics(1);   // toolbar glyphs: 10 px pitch, 9 px caps

        public static readonly Brush ClrBlue = Make(0x3C, 0x6E, 0x9C);
        public static readonly Brush BurntOrange = Make(0xC8, 0x5A, 0x1E);
        public static readonly Brush ListRow = Make(0x3C, 0x3C, 0x3C);
        public static readonly Brush DeleteX = Make(0xE0, 0x5A, 0x50);
        /// <summary>A saved macro button at rest, cyan as on the ERAM display (dark enough for the white label).</summary>
        public static readonly Brush MacroCyan = Make(0x0E, 0x86, 0x8A);
        /// <summary>Macro button label: pure white, brighter than the toolbar's gray text.</summary>
        public static readonly Brush MacroText = Make(0xFF, 0xFF, 0xFF);
        /// <summary>Macro labels: Verdana Bold, top-aligned like ERAM's macro buttons; long labels shrink to fit.</summary>
        public static readonly Typeface MacroFace = new Typeface(new FontFamily("Verdana"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        public const double MacroSize = 13, MacroCapTop = 4;

        private static Brush Make(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

        /// <summary>One or two centered lines in a button body ("\n" splits).</summary>
        public static void Label(Painter p, string label, double x, double y, double w, double h, Brush b) =>
            ToolbarFont.Label(p, label, x, y, w, b);

        /// <summary>A button with a tear-off bar (VIEWS style).</summary>
        public static void WithBar(Painter p, double x, double y, string label, Brush body, Action? onBody, Action? onBar,
            Brush? bar = null, bool macro = false)
        {
            p.Fill(Eram.TbBorder, x, y, W, H);
            p.Fill(bar ?? Eram.TbTearoff, x + BarX, y + BodyY, BarW, BodyH);
            p.Fill(body, x + BodyX, y + BodyY, BodyW, BodyH);
            if (macro) p.SmoothText(label.Replace('\n', ' ').Trim(), x + BodyX + 2, BodyW - 4, y + BodyY + MacroCapTop, MacroFace, MacroSize, MacroText);
            else if (ToolbarLabels.Has(label)) p.Runs(ToolbarLabels.Get(label), x + BodyX, y + BodyY, Eram.TbText);
            else Label(p, label, x + BodyX, y + BodyY, BodyW, BodyH, Eram.TbText);
            if (onBar != null) p.Hit(x, y, BodyX, H, _ => onBar());
            if (onBody != null) p.Hit(x + BodyX, y, BodyW + 1, H, _ => onBody());
        }

        /// <summary>A toolbar menu button with no tear-off bar.</summary>
        public static void Plain(Painter p, double x, double y, string label, Brush body, Action<MouseButton>? onClick)
        {
            p.Fill(Eram.TbBorder, x, y, W, H);
            p.Fill(body, x + 1, y + 1, W - 2, H - 2);
            Label(p, label, x + 1, y + 1, W - 2, H - 2, Eram.TbText);
            if (onClick != null) p.Hit(x, y, W, H, onClick);
        }

        /// <summary>An X drawn over a box (MACRO DELETE selection).</summary>
        public static void Cross(Painter p, double x, double y, double w, double h, Brush b)
        {
            int n = (int)Math.Min(w, h);
            double ox = x + Math.Floor((w - n) / 2), oy = y + Math.Floor((h - n) / 2);
            for (int i = 0; i < n; i++)
            {
                p.Fill(b, ox + i, oy + i, 2, 1);
                p.Fill(b, ox + n - 2 - i, oy + i, 2, 1);
            }
        }
    }

    /// <summary>
    /// ERAM RECORD toolbar (ERAM manual 7.2): RECORD opens MSG #, MACRO EDIT, MACRO DELETE and CLR ALL MACROS.
    /// All state lives in <see cref="MacroManager"/>; this only draws it and routes clicks and keys.
    /// </summary>
    public sealed class RecordToolbarWindow : EramWindow, ITearOffSource
    {
        public const string MsgId = "MSG", EditId = "MACRO EDIT", DeleteId = "MACRO DELETE", ClearId = "CLR ALL MACROS";

        public string Menu => "RECORD";
        public Action<string>? TearOff { get; set; }
        public bool HasTearOff(string id) => id is MsgId or EditId or DeleteId or ClearId;

        /// <summary>
        /// A torn-off RECORD button. It works like the one on the toolbar; what it opens (the recording box, the
        /// macro list) appears on the RECORD toolbar, which opens if it's closed.
        /// </summary>
        public Size PaintTearOff(string id, Painter p, TearOffWindow w)
        {
            MenuButton(id, p, 0, 0, null, openMenu: true);
            return new Size(TbCell.W, TbCell.H);
        }

        /// <summary>One of RECORD's buttons, with its tear-off bar.</summary>
        private void MenuButton(string id, Painter p, double x, double y, Action? onBar, bool openMenu = false)
        {
            Action Run(Action a) => () => { if (openMenu) _mgr.OpenMenu(); a(); };
            switch (id)
            {
                case MsgId:
                    TbCell.WithBar(p, x, y, Macros.MsgButton(_mgr.Settings.Macros.Count),
                        _mgr.Mode == MacroManager.Entry.Lines ? Eram.TbOn : Eram.TbOff, Run(_mgr.MsgClicked), onBar);
                    break;
                case EditId:
                    TbCell.WithBar(p, x, y, "MACRO\nEDIT", _mgr.EditOpen ? Eram.TbOn : Eram.TbOff, Run(_mgr.ToggleEdit), onBar);
                    break;
                case DeleteId:
                    TbCell.WithBar(p, x, y, "MACRO\nDELETE", _mgr.DeleteOpen ? Eram.TbOn : Eram.TbOff, Run(_mgr.ToggleDelete), onBar);
                    break;
                case ClearId:
                    TbCell.WithBar(p, x, y, "CLR ALL\nMACROS", _mgr.ClearArmed ? TbCell.BurntOrange : TbCell.ClrBlue, Run(_mgr.ClearAllClicked), onBar);
                    break;
            }
        }

        private readonly MacroManager _mgr;
        private readonly DispatcherTimer _blinkTimer;
        private bool _blinkOn = true;

        public RecordToolbarWindow(MacroManager mgr)
        {
            _mgr = mgr;
            Surface.Paint = Paint;
            Surface.KeyHandler = (k, m) => _mgr.OnKey(k, m);
            Surface.TextHandler = t => _mgr.OnText(t);
            KeyboardEnded += () => _mgr.OnKeyboardEnded();
            Repositioned += _ => _mgr.ToolbarMoved();
            _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _blinkTimer.Tick += (_, _) => { _blinkOn = !_blinkOn; if (KeyboardActive) Surface.Refresh(); else _blinkTimer.Stop(); };
        }

        public void Refresh() => Surface.Refresh();

        public void StartTyping()
        {
            BeginKeyboard();
            _blinkOn = true;
            _blinkTimer.Start();
            Surface.Refresh();
        }

        /// <summary>Screen pixel offset of the MSG slot from the window origin, in drawing units.</summary>
        public static Point SlotOffset => new Point(TbCell.W + TbCell.FrameT, TbCell.FrameT);

        private const int LineH = 14, Pad = 4, BoxChars = 32;

        private Size Paint(Painter p)
        {
            var m = TbCell.M;
            double right = TbCell.W, bottom = TbCell.FrameT * 2 + TbCell.H;
            bool open = _mgr.Settings.Open;

            TbCell.WithBar(p, 0, TbCell.FrameT, "RECORD", open ? Eram.TbMenuOpen : Eram.TbMenuClosed,
                onBody: _mgr.ToggleOpen, onBar: BeginReposition);

            if (open)
            {
                double gx = TbCell.W;
                double gridW = TbCell.FrameT * 2 + 4 * TbCell.W + 3 * TbCell.Gap;
                p.Fill(Eram.TbFrame, gx, 0, gridW, bottom);
                p.Fill(Eram.TbGap, gx + TbCell.FrameT, TbCell.FrameT, gridW - TbCell.FrameT * 2, TbCell.H);
                double y = TbCell.FrameT;
                double X(int i) => gx + TbCell.FrameT + i * (TbCell.W + TbCell.Gap);
                right = gx + gridW;

                // MSG slot: MSG #, the blank label button, or the saved macro waiting to be moved.
                var pending = _mgr.Pending;
                if (_mgr.Mode == MacroManager.Entry.Label)
                {
                    p.Fill(Eram.TbBorder, X(0), y, TbCell.W, TbCell.H);
                    p.Fill(Eram.TbOff, X(0) + 1, y + 1, TbCell.W - 2, TbCell.H - 2);
                    double tx = X(0) + 1 + Math.Floor((TbCell.W - 2 - TbCell.BodyW) / 2.0) + 4, ty = y + 1 + ToolbarFont.Line1Top;
                    ToolbarFont.Text(p, _mgr.LabelText, tx, ty, Eram.TbText);
                    Cursor(p, tx, ty, _mgr.LabelText.Length);
                }
                else if (pending != null && !_mgr.Tearing)
                {
                    TbCell.WithBar(p, X(0), y, pending.Label, _mgr.IsArmed(pending) ? Eram.TbOn : TbCell.MacroCyan,
                        onBody: () => _mgr.MacroClicked(pending, MouseButton.Left), onBar: () => _mgr.TearOff(pending), macro: true);
                    if (_mgr.IsMarked(pending)) TbCell.Cross(p, X(0) + TbCell.BodyX, y + 1, TbCell.BodyW, TbCell.BodyH, TbCell.DeleteX);
                }
                else if (pending == null)
                {
                    MenuButton(MsgId, p, X(0), y, () => TearOff?.Invoke(MsgId));
                }

                MenuButton(EditId, p, X(1), y, () => TearOff?.Invoke(EditId));
                MenuButton(DeleteId, p, X(2), y, () => TearOff?.Invoke(DeleteId));
                MenuButton(ClearId, p, X(3), y, () => TearOff?.Invoke(ClearId));

                // Recording: input box under MSG with SAVE MSG below it.
                if (_mgr.Mode == MacroManager.Entry.Lines)
                {
                    var (w, h) = TextBox(p, X(0), bottom + 2, _mgr.EntryLines, BoxChars, true);
                    double sy = bottom + 2 + h;
                    SaveButton(p, X(0), sy, w, _mgr.SaveRecorded);
                    right = Math.Max(right, X(0) + w);
                    bottom = Math.Max(bottom, sy + LineH + 2);
                }

                // MACRO EDIT / MACRO DELETE: the list of macros, right of the button that opened it.
                if (_mgr.EditOpen || _mgr.DeleteOpen)
                {
                    double lx = _mgr.EditOpen ? X(2) : X(3), ly = y;
                    foreach (var mac in _mgr.Settings.Macros)
                    {
                        bool sel = _mgr.EditOpen && mac == _mgr.Editing;
                        p.Fill(Eram.TbFrame, lx, ly, TbCell.W, LineH);
                        p.Fill(sel ? Eram.MenuText : TbCell.ListRow, lx + 1, ly, TbCell.W - 2, LineH - 1);
                        ToolbarFont.Text(p, mac.Label, lx + Pad, ly + 2, sel ? Eram.Black : Eram.TbText);
                        if (_mgr.IsMarked(mac)) TbCell.Cross(p, lx + 1, ly, TbCell.W - 2, LineH - 1, TbCell.DeleteX);
                        var mc = mac;
                        p.Hit(lx, ly, TbCell.W, LineH, b => _mgr.ListClicked(mc, b));
                        ly += LineH;
                    }
                    if (_mgr.Settings.Macros.Count == 0)
                    {
                        p.Fill(TbCell.ListRow, lx, ly, TbCell.W, LineH);
                        ToolbarFont.Text(p, "NONE", lx + Pad, ly + 2, Eram.TbText);
                        ly += LineH;
                    }
                    right = Math.Max(right, lx + TbCell.W);
                    bottom = Math.Max(bottom, ly + 1);

                    // Edit pop-up: macro text, then its label, then SAVE MSG.
                    if (_mgr.EditOpen && _mgr.Editing != null)
                    {
                        double px = lx + TbCell.W, py = y;
                        var (w, h) = TextBox(p, px, py, _mgr.EntryLines, BoxChars, _mgr.EditField == 0);
                        p.Hit(px, py, w, h, _ => _mgr.SetEditField(0));
                        py += h;
                        var (_, lh) = TextBox(p, px, py, new List<string> { _mgr.LabelText }, BoxChars, _mgr.EditField == 1, single: true);
                        p.Hit(px, py, w, lh, _ => _mgr.SetEditField(1));
                        py += lh;
                        SaveButton(p, px, py, w, _mgr.SaveEdit);
                        right = Math.Max(right, px + w);
                        bottom = Math.Max(bottom, py + LineH + 2);
                    }
                }
            }

            // Feedback line (ERAM puts these in the MCA Feedback Area; CRC's MCA can't show ours).
            var fb = _mgr.Feedback;
            if (fb.Length > 0)
            {
                double fy = bottom + 2, fw = fb.Length * m.Cell + Pad * 2;
                p.Fill(Eram.Black, 0, fy, fw, LineH);
                ToolbarFont.Text(p, fb, Pad, fy + 2, Eram.SystemYellow);
                right = Math.Max(right, fw);
                bottom = fy + LineH;
            }
            return new Size(right, bottom);
        }

        private (double W, double H) TextBox(Painter p, double x, double y, IReadOnlyList<string> lines, int chars, bool active, bool single = false)
        {
            var m = TbCell.M;
            double w = chars * m.Cell + Pad * 2 + 2, h = Math.Max(1, lines.Count) * LineH + 4;
            p.Fill(Eram.ViewBorder, x, y, w, h);
            p.Fill(Eram.Black, x + 1, y + 1, w - 2, h - 2);
            for (int i = 0; i < lines.Count; i++)
                ToolbarFont.Text(p, lines[i], x + 1 + Pad, y + 3 + i * LineH, Eram.TbText);
            if (active && KeyboardActive)
            {
                int line = single ? 0 : Math.Clamp(_mgr.CursorLine, 0, Math.Max(0, lines.Count - 1));
                int col = single ? _mgr.LabelText.Length : _mgr.CursorCol;
                Cursor(p, x + 1 + Pad, y + 3 + line * LineH, col);
            }
            return (w, h);
        }

        private void Cursor(Painter p, double x, double top, int col)
        {
            if (!KeyboardActive || !_blinkOn) return;
            p.Fill(Eram.TbText, x + col * TbCell.M.Cell, top - 1, 2, TbCell.M.Cap + 2);
        }

        private static void SaveButton(Painter p, double x, double y, double w, Action save)
        {
            p.Fill(Eram.ViewBorder, x, y, w, LineH + 2);
            p.Fill(TbCell.ListRow, x + 1, y, w - 2, LineH + 1);
            ToolbarFont.TextCentered(p, "SAVE MSG", x, w, y + 3, Eram.TbText);
            p.Hit(x, y, w, LineH + 2, _ => save());
        }
    }

    /// <summary>A macro button placed on the display: tear-off bar to move it, body to invoke it.</summary>
    public sealed class MacroButtonWindow : EramWindow
    {
        private readonly MacroManager _mgr;
        public Macro Macro { get; }

        /// <summary>Raised when a move started with BeginReposition is cancelled (Esc or right-click).</summary>
        public event Action<MacroButtonWindow>? MoveCancelled;

        public MacroButtonWindow(MacroManager mgr, Macro macro)
        {
            _mgr = mgr;
            Macro = macro;
            Surface.Paint = Paint;
            Repositioned += _ => _mgr.MacroMoved(this);
            RepositionCancelled += _ => MoveCancelled?.Invoke(this);
        }

        public void Refresh() => Surface.Refresh();

        private Size Paint(Painter p)
        {
            TbCell.WithBar(p, 0, 0, Macro.Label, _mgr.IsArmed(Macro) ? Eram.TbOn : TbCell.MacroCyan,
                onBody: null, onBar: BeginReposition, macro: true);
            p.Hit(TbCell.BodyX, 0, TbCell.BodyW + 1, TbCell.H, b => _mgr.MacroClicked(Macro, b));
            if (_mgr.IsMarked(Macro)) TbCell.Cross(p, TbCell.BodyX, TbCell.BodyY, TbCell.BodyW, TbCell.BodyH, TbCell.DeleteX);
            return new Size(TbCell.W, TbCell.H);
        }
    }

    /// <summary>
    /// Macros (ERAM RECORD, manual 7.2). Recording, editing, deleting and moving follow the manual. Invoking:
    ///  - a one-command macro is typed into CRC's MCA (Esc first, no Enter); you then click the track in CRC, so
    ///    CRC adds the flight ID (left-click + Enter, or middle-click), several flights included, as in ERAM;
    ///  - a macro with several commands waits for one flight: QF it in CRC (needs vUSALink App or Lite, which
    ///    records QFs) or middle-click it in INBND, DEPT or IN SECTOR; each command is then typed with its flight ID.
    /// Typing into CRC uses <see cref="CrcTyper"/> (the vUSALink app's CRC echo method).
    /// </summary>
    public sealed class MacroManager
    {
        public enum Entry { None, Lines, Label }

        private readonly Func<double> _scale;
        private readonly Action _dirty;
        private readonly RecordToolbarWindow _bar;
        private readonly Dictionary<Macro, MacroButtonWindow> _windows = new Dictionary<Macro, MacroButtonWindow>();
        private readonly HashSet<Macro> _marked = new HashSet<Macro>();
        private readonly DispatcherTimer _timer;
        private DateTime _feedbackUntil;
        private bool _busy;

        public RecordSettings Settings { get; }
        public Entry Mode { get; private set; }
        public List<string> EntryLines { get; private set; } = new List<string> { "" };
        public string LabelText { get; private set; } = "";
        public int CursorLine { get; private set; }
        public int CursorCol { get; private set; }
        public bool EditOpen { get; private set; }
        public bool DeleteOpen { get; private set; }
        public bool ClearArmed { get; private set; }
        public Macro? Editing { get; private set; }
        /// <summary>0 = macro text, 1 = label (Tab switches).</summary>
        public int EditField { get; private set; }
        public bool Tearing { get; private set; }
        public string Feedback { get; private set; } = "";

        /// <summary>The macro waiting for a flight (several commands), and since when.</summary>
        private Macro? _armed;
        private DateTime _armedAt;
        public static readonly TimeSpan ArmTime = TimeSpan.FromSeconds(60);

        public MacroManager(RecordSettings settings, Func<double> scale, Action dirty)
        {
            Settings = settings;
            _scale = scale;
            _dirty = dirty;
            _bar = new RecordToolbarWindow(this);
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
        }

        /// <summary>Macro button not moved off the toolbar yet (holds the MSG slot).</summary>
        public Macro? Pending => Settings.Macros.LastOrDefault(m => m.X == null);

        public bool IsArmed(Macro m) => _armed == m;
        public bool IsMarked(Macro m) => _marked.Contains(m);

        public void Show(int x, int y, Func<int?, int?, int, int, (int, int)> place)
        {
            _bar.SetScale(_scale());
            _bar.ShowAt(x, y);
            // Older unmoved macros (only the newest holds the MSG slot) go next to the toolbar.
            var pending = Pending;
            int n = 0;
            foreach (var m in Settings.Macros.Where(m => m.X == null && m != pending).ToList())
            {
                m.X = x + (int)(TbCell.W * _scale()) * (n % 8);
                m.Y = y + (int)((TbCell.H * 2 + 20) * _scale()) + (int)(TbCell.H * _scale()) * (n / 8);
                n++;
            }
            foreach (var m in Settings.Macros.Where(m => m.X != null))
            {
                var (mx, my) = place(m.X, m.Y, x, y + (int)(80 * _scale()));
                m.X = mx; m.Y = my;
                OpenWindow(m, mx, my);
            }
        }

        public void SetScale(double s)
        {
            _bar.SetScale(s);
            foreach (var w in _windows.Values) w.SetScale(s);
        }

        public void Close()
        {
            _timer.Stop();
            foreach (var w in _windows.Values) w.Close();
            _windows.Clear();
            _bar.Close();
        }

        public (int X, int Y) ToolbarPosition => _bar.ScreenPosition;

        private MacroButtonWindow OpenWindow(Macro m, int x, int y)
        {
            var w = new MacroButtonWindow(this, m);
            w.SetScale(_scale());
            w.ShowAt(x, y);
            _windows[m] = w;
            return w;
        }

        private void Refresh()
        {
            _bar.Refresh();
            foreach (var w in _windows.Values) w.Refresh();
        }

        private void Say(string text, int seconds = 10)
        {
            Feedback = text;
            _feedbackUntil = DateTime.UtcNow.AddSeconds(seconds);
            _bar.Refresh();
        }

        private void Tick()
        {
            if (Feedback.Length > 0 && DateTime.UtcNow > _feedbackUntil) { Feedback = ""; _bar.Refresh(); }
            if (_armed != null && DateTime.UtcNow - _armedAt > ArmTime) { Disarm(); Say("MACRO CANCELLED"); }
        }

        // ---------------- Toolbar buttons ----------------

        public void ToggleOpen()
        {
            CancelEntry();
            Settings.Open = !Settings.Open;
            if (!Settings.Open) { EditOpen = DeleteOpen = ClearArmed = false; Editing = null; _marked.Clear(); }
            _dirty();
            Refresh();
        }

        public void ToolbarMoved()
        {
            var (x, y) = _bar.ScreenPosition;
            Settings.X = x; Settings.Y = y;
            _dirty();
        }

        /// <summary>Opens the RECORD toolbar if it's closed (for torn-off RECORD buttons).</summary>
        public void OpenMenu()
        {
            if (!Settings.Open) ToggleOpen();
        }

        /// <summary>The RECORD toolbar, as a source of tear-off buttons.</summary>
        public ITearOffSource TearOffSource => _bar;

        public void MsgClicked()
        {
            if (Mode == Entry.Lines) return;
            CloseMenus();
            if (Pending != null) { Say("MOVE THE NEW MACRO FIRST"); return; }
            if (Settings.Macros.Count >= Macros.Max) { Say("MSG MAX"); return; }
            Mode = Entry.Lines;
            EntryLines = new List<string> { "" };
            CursorLine = CursorCol = 0;
            _bar.StartTyping();
        }

        public void SaveRecorded()
        {
            if (Mode != Entry.Lines) return;
            var lines = Macros.CleanLines(EntryLines);
            if (lines.Count == 0) { Say("NO COMMANDS"); return; }
            EntryLines = lines;
            Mode = Entry.Label;
            LabelText = "";
            _bar.Refresh();
        }

        public void ToggleEdit()
        {
            CancelEntry();
            bool open = !EditOpen;
            CloseMenus();
            EditOpen = open;
            Refresh();
        }

        public void ToggleDelete()
        {
            CancelEntry();
            bool open = !DeleteOpen;
            CloseMenus();
            DeleteOpen = open;
            Refresh();
        }

        public void ClearAllClicked()
        {
            CancelEntry();
            if (!ClearArmed)
            {
                CloseMenus();
                ClearArmed = true;   // coding changes to burnt orange; a second click verifies
                _bar.Refresh();
                return;
            }
            ClearArmed = false;
            Disarm();
            foreach (var w in _windows.Values) w.Close();
            _windows.Clear();
            Settings.Macros.Clear();
            _dirty();
            Say("ALL MACROS DELETED", 5);
            Refresh();
        }

        private void CloseMenus()
        {
            EditOpen = DeleteOpen = ClearArmed = false;
            Editing = null;
            _marked.Clear();
        }

        /// <summary>A click in the MACRO EDIT / MACRO DELETE list.</summary>
        public void ListClicked(Macro m, MouseButton b)
        {
            if (DeleteOpen) { DeleteClick(m, b); return; }
            if (!EditOpen) return;
            Editing = m;
            EntryLines = m.Lines.Count > 0 ? m.Lines.ToList() : new List<string> { "" };
            LabelText = m.Label;
            EditField = 0;
            CursorLine = EntryLines.Count - 1;
            CursorCol = EntryLines[CursorLine].Length;
            _bar.StartTyping();
        }

        public void SetEditField(int f)
        {
            if (Editing == null) return;
            EditField = f;
            if (!_bar.KeyboardActive) _bar.StartTyping();
            _bar.Refresh();
        }

        public void SaveEdit()
        {
            if (Editing == null) return;
            var lines = Macros.CleanLines(EntryLines);
            if (lines.Count == 0) { Say("NO COMMANDS"); return; }
            Editing.Lines = lines;
            var label = Macros.CleanLabel(LabelText);
            Editing.Label = label.Length > 0 ? label : Macros.DefaultLabel(lines);
            if (_windows.TryGetValue(Editing, out var w)) w.Refresh();
            Say(Editing.Label + " SAVED", 5);
            Editing = null;
            _dirty();
            _bar.EndKeyboard();
            Refresh();
        }

        // ---------------- Typing (record and edit) ----------------

        public bool OnKey(Key key, ModifierKeys mods)
        {
            bool editing = EditOpen && Editing != null;
            if (Mode == Entry.None && !editing) return false;
            bool labelField = Mode == Entry.Label || editing && EditField == 1;
            int maxLines = editing ? Macros.EditLines : Macros.RecordLines;
            switch (key)
            {
                case Key.Escape:   // CLEAR
                    CancelEntry();
                    return true;
                case Key.Tab:
                    if (editing) EditField = 1 - EditField;
                    break;
                case Key.Enter:
                    if (Mode == Entry.Label) { FinishRecording(); return true; }
                    if (labelField) { SaveEdit(); return true; }
                    if (EntryLines.Count >= maxLines) return true;
                    var rest = EntryLines[CursorLine].Substring(CursorCol);
                    EntryLines[CursorLine] = EntryLines[CursorLine].Substring(0, CursorCol);
                    EntryLines.Insert(++CursorLine, rest);
                    CursorCol = 0;
                    break;
                case Key.Back:
                    if (labelField) { if (LabelText.Length > 0) LabelText = LabelText[..^1]; break; }
                    if (CursorCol > 0)
                    {
                        EntryLines[CursorLine] = EntryLines[CursorLine].Remove(CursorCol - 1, 1);
                        CursorCol--;
                    }
                    else if (CursorLine > 0)
                    {
                        CursorCol = EntryLines[CursorLine - 1].Length;
                        EntryLines[CursorLine - 1] += EntryLines[CursorLine];
                        EntryLines.RemoveAt(CursorLine--);
                    }
                    break;
                case Key.Delete:
                    if (labelField) break;
                    if (CursorCol < EntryLines[CursorLine].Length) EntryLines[CursorLine] = EntryLines[CursorLine].Remove(CursorCol, 1);
                    else if (CursorLine < EntryLines.Count - 1) { EntryLines[CursorLine] += EntryLines[CursorLine + 1]; EntryLines.RemoveAt(CursorLine + 1); }
                    break;
                case Key.Left: if (!labelField && CursorCol > 0) CursorCol--; break;
                case Key.Right: if (!labelField && CursorCol < EntryLines[CursorLine].Length) CursorCol++; break;
                case Key.Up: if (!labelField && CursorLine > 0) { CursorLine--; CursorCol = Math.Min(CursorCol, EntryLines[CursorLine].Length); } break;
                case Key.Down: if (!labelField && CursorLine < EntryLines.Count - 1) { CursorLine++; CursorCol = Math.Min(CursorCol, EntryLines[CursorLine].Length); } break;
                case Key.Home: if (!labelField) CursorCol = 0; break;
                case Key.End: if (!labelField) CursorCol = EntryLines[CursorLine].Length; break;
                default:
                    return false;
            }
            _bar.Refresh();
            return true;
        }

        public void OnText(string text)
        {
            bool editing = EditOpen && Editing != null;
            if (Mode == Entry.None && !editing) return;
            bool labelField = Mode == Entry.Label || editing && EditField == 1;
            foreach (var raw in text)
            {
                if (raw < ' ') continue;
                var c = char.ToUpperInvariant(raw);
                if (labelField)
                {
                    if (LabelText.Length < Macros.LabelMax) LabelText += c;
                }
                else if (EntryLines[CursorLine].Length < Macros.LineMax)
                {
                    EntryLines[CursorLine] = EntryLines[CursorLine].Insert(CursorCol++, c.ToString());
                }
            }
            _bar.Refresh();
        }

        /// <summary>Focus left the toolbar (a click anywhere else): ERAM cancels recording.</summary>
        public void OnKeyboardEnded()
        {
            if (Mode != Entry.None) { Mode = Entry.None; Say("MACRO NOT SAVED", 4); }
            if (Editing != null) Editing = null;
            _bar.Refresh();
        }

        private void CancelEntry()
        {
            bool was = Mode != Entry.None || Editing != null;
            Mode = Entry.None;
            Editing = null;
            if (was) _bar.EndKeyboard();
            _bar.Refresh();
        }

        private void FinishRecording()
        {
            var label = Macros.CleanLabel(LabelText);
            var macro = new Macro { Lines = EntryLines.ToList(), Label = label.Length > 0 ? label : Macros.DefaultLabel(EntryLines) };
            Macros.Add(Settings.Macros, macro);
            foreach (var gone in _windows.Keys.Where(k => !Settings.Macros.Contains(k)).ToList()) { _windows[gone].Close(); _windows.Remove(gone); }
            Mode = Entry.None;
            _dirty();
            _bar.EndKeyboard();
            Say("MOVE " + macro.Label + " TO PLACE IT", 6);
            _bar.Refresh();
        }

        // ---------------- Moving ----------------

        /// <summary>Tear-off bar of the macro waiting in the MSG slot: it moves with the cursor until you click.</summary>
        public void TearOff(Macro m)
        {
            if (Tearing) return;
            var (bx, by) = _bar.ScreenPosition;
            double s = _scale();
            int x = bx + (int)Math.Round(RecordToolbarWindow.SlotOffset.X * s), y = by + (int)Math.Round(RecordToolbarWindow.SlotOffset.Y * s);
            Tearing = true;
            var w = OpenWindow(m, x, y);
            w.MoveCancelled += win => { win.Close(); _windows.Remove(win.Macro); Tearing = false; _bar.Refresh(); };
            _bar.Refresh();
            w.BeginReposition();
        }

        public void MacroMoved(MacroButtonWindow w)
        {
            var (x, y) = w.ScreenPosition;
            w.Macro.X = x; w.Macro.Y = y;
            Tearing = false;
            _dirty();
            _bar.Refresh();
        }

        // ---------------- Deleting ----------------

        /// <summary>MACRO DELETE: left-click marks (X) all but the last; middle-click the last deletes them all.</summary>
        private void DeleteClick(Macro m, MouseButton b)
        {
            if (b != MouseButton.Middle)
            {
                if (!_marked.Remove(m)) _marked.Add(m);
                Refresh();
                return;
            }
            _marked.Add(m);
            foreach (var d in _marked.ToList())
            {
                if (_armed == d) Disarm();
                Settings.Macros.Remove(d);
                if (_windows.TryGetValue(d, out var w)) { w.Close(); _windows.Remove(d); }
            }
            Say(_marked.Count == 1 ? m.Label + " DELETED" : _marked.Count + " MACROS DELETED", 5);
            _marked.Clear();
            DeleteOpen = false;
            _dirty();
            Refresh();
        }

        // ---------------- Invoking ----------------

        /// <summary>Left- or middle-click on a macro button.</summary>
        public void MacroClicked(Macro m, MouseButton b)
        {
            if (DeleteOpen) { DeleteClick(m, b); return; }
            if (Mode != Entry.None) CancelEntry();
            if (_armed == m) { Disarm(); Say("MACRO CANCELLED", 3); return; }
            Disarm();

            // Workaround (tray option): a flight ID typed into CRC's MCA first runs the whole macro on it now.
            var typed = TypedFlid?.Invoke();
            if (typed != null)
            {
                var cmds = Macros.Commands(m, new[] { typed }, out var error);
                if (error != null) { Say(error); return; }
                _ = TypeAsync(cmds, enterLast: true, m.Label + " " + typed + " SENT", sent: TypedFlidUsed);
                return;
            }

            if (m.Lines.Count == 1)
            {
                // One command: into CRC's MCA, waiting for the flight. Click its track in CRC.
                _ = TypeAsync(new[] { m.Lines[0] + " " }, enterLast: false, m.Label + ": CLICK TRACK IN CRC");
                return;
            }
            _armed = m;
            _armedAt = DateTime.UtcNow;
            Say(m.Label + ": QF ONE FLIGHT OR MIDDLE-CLICK A LIST", (int)ArmTime.TotalSeconds);
            Refresh();
        }

        private void Disarm()
        {
            if (_armed == null) return;
            var w = _windows.TryGetValue(_armed, out var x) ? x : null;
            _armed = null;
            w?.Refresh();
            _bar.Refresh();
        }

        /// <summary>A middle-click on a flight in INBND, DEPT or IN SECTOR. True when a macro took it.</summary>
        public bool TryApply(string callsign)
        {
            if (_armed == null) return false;
            Apply(_armed, callsign);
            return true;
        }

        /// <summary>QFs recorded by the vUSALink app: the first flight QF'd after a macro was armed gets it.</summary>
        public void CheckQfs(EdstCidBook book)
        {
            if (_armed == null) return;
            var qf = book.QfSince(_armedAt).FirstOrDefault(e => e.Usable);
            if (qf != null) Apply(_armed, qf.Callsign);
        }

        /// <summary>True while a macro waits for a QF (the caller then reads cids.json every second).</summary>
        public bool WantsQfs => _armed != null;

        private void Apply(Macro m, string callsign)
        {
            Disarm();
            var cmds = Macros.Commands(m, new[] { callsign }, out var error);
            if (error != null) { Say(error); return; }
            _ = TypeAsync(cmds, enterLast: true, m.Label + " " + callsign.ToUpperInvariant() + " SENT");
        }

        /// <summary>
        /// Set when the tray option "Macros: use FLID typed in CRC" is on: the flight ID typed into CRC's MCA, or null.
        /// </summary>
        public Func<string?>? TypedFlid { get; set; }

        /// <summary>Called after a macro ran on the typed flight ID (CRC's MCA is empty again).</summary>
        public Action? TypedFlidUsed { get; set; }

        private async System.Threading.Tasks.Task TypeAsync(IReadOnlyList<string> lines, bool enterLast, string ok, Action? sent = null)
        {
            if (_busy) { Say("BUSY"); return; }
            _busy = true;
            try
            {
                var why = await CrcTyper.Type(lines, enterLast);
                if (why == null) sent?.Invoke();
                Say(why == null ? ok : "NOT TYPED: " + why);
            }
            catch (Exception ex) { Say("NOT TYPED: " + ex.Message.ToUpperInvariant()); }
            finally { _busy = false; }
        }
    }
}
