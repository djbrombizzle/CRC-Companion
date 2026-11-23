using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// ERAM DRAW tools (ERAM manual 5.2 and 5.3): rectangles, circles, lines and text drawn over CRC as "scratch pad"
    /// annotations. CRC has no DRAW and doesn't share its map position, so every annotation is anchored to the
    /// display (D); MAP anchoring and O SIZE (a radius in NM) aren't available.
    /// The annotations live on a full-screen overlay that passes clicks through to CRC except while drawing.
    /// </summary>
    public sealed class DrawManager
    {
        public enum Tool { None, Rectangle, Circle, Line, Text, Delete }
        public enum Phase { Idle, Placing, Sizing, Moving, Composing }

        private readonly Func<double> _scale;
        private readonly Action _dirty;
        private readonly Action _raiseOthers;
        private readonly DrawToolbarWindow _bar;
        private readonly DrawOverlayWindow _overlay;
        private readonly DispatcherTimer _track;
        private readonly DispatcherTimer _clock;
        private DateTime _feedbackUntil;
        private bool _escWasDown;
        private Native.POINT _lastCursor;

        public DrawSettings Settings { get; }
        public Tool ActiveTool { get; private set; }
        public Phase Step { get; private set; }
        /// <summary>The white frame being drawn, moved or sized.</summary>
        public Annotation? Draft { get; private set; }
        /// <summary>The anchored annotation a move or resize started from (it stays in place until anchored).</summary>
        public Annotation? Editing { get; private set; }
        public bool DelAllArmed { get; private set; }
        public string Feedback { get; private set; } = "";
        private AnnPoint _anchor = new AnnPoint();

        public DrawManager(DrawSettings settings, Func<double> scale, Action dirty, Action raiseOthers)
        {
            Settings = settings;
            _scale = scale;
            _dirty = dirty;
            _raiseOthers = raiseOthers;
            _overlay = new DrawOverlayWindow(this);
            _bar = new DrawToolbarWindow(this);
            _track = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(25) };
            _track.Tick += (_, _) => Track();
            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (_, _) =>
            {
                if (Feedback.Length > 0 && DateTime.UtcNow > _feedbackUntil) { Feedback = ""; _bar.Refresh(); }
            };
            _clock.Start();
        }

        /// <summary>Shows the overlay. Call before the other companion windows so they stay above it.</summary>
        public void ShowOverlay()
        {
            var (vx, vy, _, _) = Native.VirtualScreen();
            _overlay.SetScale(1.0);
            _overlay.ShowAt(vx, vy);
            UpdateOverlayMode();
        }

        public void ShowToolbar(int x, int y)
        {
            _bar.SetScale(_scale());
            _bar.ShowAt(x, y);
        }

        public void SetScale(double s) => _bar.SetScale(s);

        public void Close()
        {
            _track.Stop();
            _clock.Stop();
            _bar.Close();
            _overlay.Close();
        }

        public void ToolbarMoved()
        {
            var (x, y) = _bar.ScreenPosition;
            Settings.X = x; Settings.Y = y;
            _dirty();
        }

        /// <summary>Overlay origin on screen, to turn cursor positions into annotation coordinates.</summary>
        private AnnPoint Cursor()
        {
            Native.GetCursorPos(out var p);
            var (ox, oy) = _overlay.ScreenPosition;
            return new AnnPoint(p.X - ox, p.Y - oy);
        }

        private void WarpTo(double x, double y)
        {
            var (ox, oy) = _overlay.ScreenPosition;
            Native.SetCursorPos((int)Math.Round(ox + x), (int)Math.Round(oy + y));
        }

        private void Say(string text, int seconds = 8)
        {
            Feedback = text;
            _feedbackUntil = DateTime.UtcNow.AddSeconds(seconds);
            _bar.Refresh();
        }

        private static void Alarm() => SystemSounds.Beep.Play();

        /// <summary>Whether the overlay takes every click (drawing, placing or moving) rather than passing them on.</summary>
        public bool Capturing => Step != Phase.Idle ||
            (ActiveTool is Tool.Rectangle or Tool.Circle or Tool.Line or Tool.Text);

        /// <summary>Handles show, and can be clicked, while the DRAW toolbar is open.</summary>
        public bool HandlesVisible => Settings.Open;

        private void UpdateOverlayMode()
        {
            _overlay.SetClickThrough(!Settings.Open);
            bool tracking = ActiveTool != Tool.None || Step != Phase.Idle || DelAllArmed;   // also watches for CLEAR
            if (tracking && !_track.IsEnabled) { _escWasDown = Native.IsDown(Native.VK_ESCAPE); _track.Start(); }
            else if (!tracking) _track.Stop();
            _overlay.Refresh();
            _bar.Refresh();
        }

        // ---------------- Toolbar ----------------

        public void ToggleOpen()
        {
            Cancel();
            Settings.Open = !Settings.Open;
            DelAllArmed = false;
            _dirty();
            UpdateOverlayMode();
        }

        public void ToolClicked(Tool t)
        {
            DelAllArmed = false;
            bool same = ActiveTool == t;
            Cancel();
            if (!same)
            {
                ActiveTool = t;
                Step = t == Tool.Delete ? Phase.Idle : Phase.Placing;
                if (t != Tool.Delete) _raiseOthers();
            }
            UpdateOverlayMode();
        }

        public void CircleSizeClicked()
        {
            DelAllArmed = false;
            Say("O SIZE: MAP ANCHOR ONLY");
        }

        public void AnchorClicked()
        {
            DelAllArmed = false;
            Say("MAP ANCHOR NOT AVAILABLE IN CRC");
        }

        public void ColorClicked(MouseButton b)
        {
            DelAllArmed = false;
            Settings.Brightness = Annotations.StepBrightness(Settings.Brightness, b == MouseButton.Middle);
            _dirty();
            _overlay.Refresh();
            _bar.Refresh();
        }

        public void SwatchClicked(int color)
        {
            DelAllArmed = false;
            Settings.Color = color;
            _dirty();
            _bar.Refresh();
            _overlay.Refresh();
        }

        public void FontDefaultClicked()
        {
            DelAllArmed = false;
            Settings.Font = Annotations.DefaultFont;
            _dirty();
            _bar.Refresh();
            _overlay.Refresh();
        }

        public void FontClicked(MouseButton b)
        {
            DelAllArmed = false;
            Settings.Font = Annotations.StepFont(Settings.Font, b == MouseButton.Middle);
            _dirty();
            _bar.Refresh();
            _overlay.Refresh();
        }

        public void DelAllClicked()
        {
            if (!DelAllArmed)
            {
                Cancel();
                DelAllArmed = true;
                UpdateOverlayMode();
                return;
            }
            DelAllArmed = false;
            Settings.Items.Clear();
            _dirty();
            Say("ALL ANNOTATIONS DELETED");
            UpdateOverlayMode();
        }

        // ---------------- Overlay ----------------

        /// <summary>A click anywhere on the overlay while it's capturing.</summary>
        public void OverlayClicked(MouseButton b)
        {
            var c = Cursor();
            switch (ActiveTool)
            {
                case Tool.Rectangle:
                case Tool.Circle:
                    BoxClick(b, c);
                    break;
                case Tool.Line:
                    LineClick(b, c);
                    break;
                case Tool.Text:
                    TextClick(b, c);
                    break;
            }
            _overlay.Refresh();
        }

        private void BoxClick(MouseButton b, AnnPoint c)
        {
            switch (Step)
            {
                case Phase.Placing when b == MouseButton.Left:
                    if (!RoomForOne()) return;
                    _anchor = c;
                    Draft = new Annotation { Kind = ActiveTool == Tool.Circle ? AnnotationKind.Circle : AnnotationKind.Rectangle, X = c.X, Y = c.Y };
                    Step = Phase.Sizing;
                    break;
                case Phase.Sizing when b == MouseButton.Left && Draft != null:
                    // Size set: the cursor jumps to the top-left corner and the frame follows it (move mode).
                    Step = Phase.Moving;
                    WarpTo(Draft.X, Draft.Y);
                    break;
                case Phase.Moving when b == MouseButton.Left && Draft != null:
                    // Back to sizing, from the bottom-right corner (ERAM 5.2.8).
                    _anchor = new AnnPoint(Draft.X, Draft.Y);
                    Step = Phase.Sizing;
                    WarpTo(Draft.X + Draft.W, Draft.Y + Draft.H);
                    break;
                case Phase.Sizing when b == MouseButton.Middle && Editing != null:
                case Phase.Moving when b == MouseButton.Middle:
                    if (Draft == null || Draft.W < 2 || Draft.H < 2) { Alarm(); return; }
                    Commit();
                    break;
                default:
                    Alarm();   // ERAM's incorrect-ENTER cursor and alarm
                    break;
            }
            UpdateOverlayMode();
        }

        private void LineClick(MouseButton b, AnnPoint c)
        {
            if (Step == Phase.Moving)
            {
                if (b == MouseButton.Middle) Commit(); else Alarm();
                UpdateOverlayMode();
                return;
            }
            if (b == MouseButton.Left)
            {
                if (Step == Phase.Placing)
                {
                    if (!RoomForOne()) return;
                    Draft = new Annotation { Kind = AnnotationKind.Line, Points = { new AnnPoint(c.X, c.Y), new AnnPoint(c.X, c.Y) } };
                    Step = Phase.Sizing;
                }
                else if (Draft != null)
                {
                    if (!Annotations.CanAddPoint(Draft.Points.Count)) { Alarm(); Say("MAX 29 SEGMENTS"); return; }
                    Draft.Points.Add(new AnnPoint(c.X, c.Y));   // the new rubber-band point
                }
            }
            else if (Step == Phase.Sizing && Draft != null)
            {
                Draft.Points.RemoveAt(Draft.Points.Count - 1);   // drop the rubber band
                if (Draft.Points.Count < 2) { Draft.Points.Add(new AnnPoint(c.X, c.Y)); Alarm(); return; }
                Commit();
            }
            else Alarm();
            UpdateOverlayMode();
        }

        private void TextClick(MouseButton b, AnnPoint c)
        {
            if (Step == Phase.Placing && b == MouseButton.Left)
            {
                if (!RoomForOne()) return;
                Draft = new Annotation { Kind = AnnotationKind.Text, X = c.X, Y = c.Y, Font = Settings.Font, Lines = { "" } };
                Step = Phase.Composing;
                _overlay.StartTyping();
                _raiseOthers();
            }
            else if (Step == Phase.Composing && b == MouseButton.Middle) FinishText();   // ERAM's alternative to ENTER
            else if (Step == Phase.Moving && b == MouseButton.Middle) Commit();
            else Alarm();
            UpdateOverlayMode();
        }

        public bool OnKey(Key k, ModifierKeys m)
        {
            if (Step != Phase.Composing || Draft == null) return false;
            switch (k)
            {
                case Key.Escape: Cancel(); break;
                case Key.Enter: FinishText(); break;
                case Key.Back: Annotations.Backspace(Draft.Lines); break;
                default: return false;
            }
            UpdateOverlayMode();
            return true;
        }

        public void OnText(string t)
        {
            if (Step != Phase.Composing || Draft == null) return;
            foreach (var ch in t)
                if (!Annotations.Type(Draft.Lines, ch)) { Alarm(); break; }
            _overlay.Refresh();
        }

        public void OnKeyboardEnded()
        {
            if (Step == Phase.Composing) { Cancel(); UpdateOverlayMode(); }
        }

        private void FinishText()
        {
            if (Draft == null) return;
            Draft.Lines = Annotations.Finish(Draft.Lines);
            if (Draft.Lines.Count == 0) { Cancel(); return; }
            Commit();
        }

        /// <summary>A click on an annotation's selection handle (only while the toolbar is open).</summary>
        public void HandleClicked(Annotation a, MouseButton b)
        {
            DelAllArmed = false;
            if (ActiveTool == Tool.Delete)
            {
                if (b == MouseButton.Middle)
                {
                    Settings.Items.Remove(a);
                    _dirty();
                }
                else Alarm();
                UpdateOverlayMode();
                return;
            }
            if (ActiveTool != Tool.None || Step != Phase.Idle) { Alarm(); return; }

            if (b == MouseButton.Middle)
            {
                // Apply the selected color (and, for text, the A FONT size) to this annotation.
                a.Color = Settings.Color;
                if (a.Kind == AnnotationKind.Text) a.Font = Settings.Font;
                _dirty();
                UpdateOverlayMode();
                return;
            }

            // Left-click: move mode with a white copy; the original stays until the copy is anchored.
            Editing = a;
            Draft = a.Clone();
            ActiveTool = a.Kind switch
            {
                AnnotationKind.Rectangle => Tool.Rectangle,
                AnnotationKind.Circle => Tool.Circle,
                AnnotationKind.Line => Tool.Line,
                _ => Tool.Text,
            };
            Step = Phase.Moving;
            var o = Draft.Origin;
            WarpTo(o.X, o.Y);
            _raiseOthers();
            UpdateOverlayMode();
        }

        private bool RoomForOne()
        {
            if (Settings.Items.Count < Annotations.Max) return true;
            Alarm();
            Say("MAX " + Annotations.Max + " ANNOTATIONS");
            return false;
        }

        private void Commit()
        {
            if (Draft == null) return;
            Draft.Color = Settings.Color;
            if (Editing != null)
            {
                int i = Settings.Items.IndexOf(Editing);
                if (i >= 0) Settings.Items[i] = Draft; else Settings.Items.Add(Draft);
            }
            else Settings.Items.Add(Draft);
            _dirty();
            Draft = null;
            Editing = null;
            ActiveTool = Tool.None;
            Step = Phase.Idle;
            _overlay.EndKeyboard();
        }

        /// <summary>CLEAR: drops whatever is being drawn or moved.</summary>
        public void Cancel()
        {
            Draft = null;
            Editing = null;
            ActiveTool = Tool.None;
            Step = Phase.Idle;
            if (_overlay.KeyboardActive) _overlay.EndKeyboard();
        }

        /// <summary>Follows the cursor while sizing or moving, and watches for CLEAR (Esc).</summary>
        private void Track()
        {
            bool esc = Native.IsDown(Native.VK_ESCAPE);
            if (esc && !_escWasDown && Step != Phase.Composing)
            {
                _escWasDown = true;
                Cancel();
                DelAllArmed = false;
                UpdateOverlayMode();
                return;
            }
            _escWasDown = esc;

            Native.GetCursorPos(out var raw);
            if (raw.X == _lastCursor.X && raw.Y == _lastCursor.Y) return;
            _lastCursor = raw;
            if (Draft == null) return;
            var c = Cursor();
            if (Step == Phase.Sizing)
            {
                if (Draft.Kind == AnnotationKind.Line) { var last = Draft.Points[^1]; last.X = c.X; last.Y = c.Y; }
                else
                {
                    var (x, y, w, h) = Annotations.Box(_anchor.X, _anchor.Y, c.X, c.Y);
                    Draft.X = x; Draft.Y = y; Draft.W = w; Draft.H = h;
                }
            }
            else if (Step == Phase.Moving)
            {
                var o = Draft.Origin;
                Draft.Offset(c.X - o.X, c.Y - o.Y);
            }
            else return;
            _overlay.Refresh();
        }

        // ---------------- Colors ----------------

        private readonly Dictionary<(int, int), Brush> _brushes = new Dictionary<(int, int), Brush>();

        /// <summary>An annotation color at the current brightness.</summary>
        public Brush ColorBrush(int color) => BrushAt(color, Settings.Brightness);

        public Brush BrushAt(int color, int brightness)
        {
            if (_brushes.TryGetValue((color, brightness), out var b)) return b;
            var (r, g, bl) = Annotations.Rgb(color, brightness);
            var br = new SolidColorBrush(Color.FromRgb(r, g, bl));
            br.Freeze();
            return _brushes[(color, brightness)] = br;
        }
    }

    /// <summary>The DRAW toolbar (ERAM manual figure 5-2).</summary>
    public sealed class DrawToolbarWindow : EramWindow
    {
        private const int CellW = 66, CellH = 32, Gap = 2, Frame = 2, LineH = 14, Pad = 4;
        private static readonly Brush Teal = Make(0x3C, 0x8C, 0x8C);
        private static readonly Brush TealOn = Make(0x5C, 0xB8, 0xB8);
        private static readonly Brush ValueGreen = Make(0x3C, 0x96, 0x28);
        private static readonly Brush Hatched = Make(0x6E, 0x6E, 0x6E);
        private static readonly Brush DisabledX = Make(0xB4, 0xB4, 0xB4);
        private static readonly Brush White = Make(0xFF, 0xFF, 0xFF);

        private readonly DrawManager _mgr;

        public DrawToolbarWindow(DrawManager mgr)
        {
            _mgr = mgr;
            Surface.Paint = Paint;
            Repositioned += _ => _mgr.ToolbarMoved();
        }

        private static Brush Make(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

        public void Refresh() => Surface.Refresh();

        private static void Cell(Painter p, double x, double y, Brush body, string label, Action<MouseButton>? click)
        {
            p.Fill(Eram.TbBorder, x, y, CellW, CellH);
            p.Fill(body, x + 1, y + 1, CellW - 2, CellH - 2);
            if (label.Length > 0) ToolbarFont.Label(p, label, x + 1, y + 1, CellW - 2, Eram.TbText);
            if (click != null) p.Hit(x, y, CellW, CellH, click);
        }

        private Size Paint(Painter p)
        {
            double right = TbCell.W, bottom = Frame * 2 + TbCell.H;
            var s = _mgr.Settings;
            TbCell.WithBar(p, 0, Frame, "DRAW", s.Open ? Eram.TbMenuOpen : Eram.TbMenuClosed,
                onBody: _mgr.ToggleOpen, onBar: BeginReposition);

            if (s.Open)
            {
                double gx = TbCell.W;
                double gridW = Frame * 2 + 7 * CellW + 6 * Gap, gridH = Frame * 2 + 2 * CellH + Gap;
                p.Fill(Eram.TbFrame, gx, 0, gridW, gridH);
                p.Fill(Eram.TbGap, gx + Frame, Frame, gridW - Frame * 2, gridH - Frame * 2);
                double X(int i) => gx + Frame + i * (CellW + Gap);
                double y0 = Frame, y1 = Frame + CellH + Gap;
                var tool = _mgr.ActiveTool;
                var ink = _mgr.BrushAt(s.Color, 100);

                // Row 1: shapes, O SIZE, line, text, COLOR and the color selection.
                Cell(p, X(0), y0, tool == DrawManager.Tool.Rectangle ? TealOn : Teal, "", _ => _mgr.ToolClicked(DrawManager.Tool.Rectangle));
                p.Frame(ink, X(0) + 13, y0 + 7, CellW - 26, CellH - 14, 2);

                Cell(p, X(1), y0, tool == DrawManager.Tool.Circle ? TealOn : Teal, "", _ => _mgr.ToolClicked(DrawManager.Tool.Circle));
                p.Ellipse(ink, X(1) + 21, y0 + 4, CellW - 42, CellH - 8, 2);

                Cell(p, X(2), y0, ValueGreen, "O SIZE\n5", _ => _mgr.CircleSizeClicked());
                TbCell.Cross(p, X(2) + 1, y0 + 1, CellW - 2, CellH - 2, DisabledX);   // display anchor: deactivated

                Cell(p, X(3), y0, tool == DrawManager.Tool.Line ? TealOn : Teal, "", _ => _mgr.ToolClicked(DrawManager.Tool.Line));
                p.Polyline(ink, new[] { new Point(X(3) + 10, y0 + 10), new Point(X(3) + CellW - 10, y0 + CellH - 10) }, 2);

                Cell(p, X(4), y0, tool == DrawManager.Tool.Text ? TealOn : Teal, "", _ => _mgr.ToolClicked(DrawManager.Tool.Text));
                ToolbarFont.Text(p, "A", X(4) + (CellW - ToolbarFont.GlyphW) / 2.0, y0 + 1 + ToolbarFont.Line1Top, ink);

                Cell(p, X(5), y0, Eram.Black, "COLOR\n" + s.Brightness, b => _mgr.ColorClicked(b));

                // Color selection: four swatches, the selected one framed.
                double sx = X(6), sw = (CellW - 2) / 2.0, sh = (CellH - 2) / 2.0;
                p.Fill(Eram.TbBorder, sx, y0, CellW, CellH);
                int[] order = { 0, 2, 1, 3 };   // green, coral / white, yellow, as in the ERAM toolbar
                for (int i = 0; i < 4; i++)
                {
                    int color = order[i];
                    double cx = sx + 1 + (i % 2) * sw, cy = y0 + 1 + (i / 2) * sh;
                    p.Fill(_mgr.BrushAt(color, 100), cx + 2, cy + 2, sw - 4, sh - 4);
                    if (color == s.Color) p.Frame(White, cx, cy, sw, sh, 2);
                    p.Hit(cx, cy, sw, sh, _ => _mgr.SwatchClicked(color));
                }

                // Row 2: FONT, A FONT, DISP, DEL, DEL ALL.
                Cell(p, X(0), y1, Teal, "FONT", _ => _mgr.FontDefaultClicked());
                Cell(p, X(1), y1, ValueGreen, "A FONT\n" + s.Font, b => _mgr.FontClicked(b));
                Cell(p, X(2), y1, Hatched, "DISP", _ => _mgr.AnchorClicked());
                Cell(p, X(3), y1, tool == DrawManager.Tool.Delete ? TealOn : Teal, "DEL", _ => _mgr.ToolClicked(DrawManager.Tool.Delete));
                Cell(p, X(4), y1, _mgr.DelAllArmed ? TbCell.BurntOrange : Teal, "DEL\nALL", _ => _mgr.DelAllClicked());

                right = gx + gridW;
                bottom = Math.Max(bottom, gridH);
            }

            if (_mgr.Feedback.Length > 0)
            {
                double fy = bottom + 2, fw = ToolbarFont.Width(_mgr.Feedback) + Pad * 2;
                p.Fill(Eram.Black, 0, fy, fw, LineH);
                ToolbarFont.Text(p, _mgr.Feedback, Pad, fy + 2, Eram.SystemYellow);
                right = Math.Max(right, fw);
                bottom = fy + LineH;
            }
            return new Size(right, bottom);
        }
    }

    /// <summary>
    /// Full-screen, see-through overlay that draws the annotations over CRC. It passes clicks through to CRC
    /// except on handles (toolbar open) and while drawing or moving, when it takes every click.
    /// </summary>
    public sealed class DrawOverlayWindow : EramWindow
    {
        private const int HandleSize = 16, HandleGap = 4, Stroke = 2;
        private static readonly Brush CaptureBg = Frozen(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));
        private static readonly Brush White = Frozen(new SolidColorBrush(Colors.White));
        private static readonly Brush HandleBg = Frozen(new SolidColorBrush(Colors.Black));

        private readonly DrawManager _mgr;
        private readonly DispatcherTimer _blink;
        private bool _blinkOn = true;
        private bool _clickThrough = true;

        public DrawOverlayWindow(DrawManager mgr)
        {
            _mgr = mgr;
            Surface.Paint = Paint;
            Surface.KeyHandler = (k, m) => _mgr.OnKey(k, m);
            Surface.TextHandler = t => _mgr.OnText(t);
            KeyboardEnded += () => _mgr.OnKeyboardEnded();
            SourceInitialized += (_, _) => Native.SetClickThrough(Hwnd, _clickThrough);
            _blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _blink.Tick += (_, _) => { _blinkOn = !_blinkOn; if (KeyboardActive) Surface.Refresh(); else _blink.Stop(); };
        }

        private static Brush Frozen(Brush b) { b.Freeze(); return b; }

        public void Refresh() => Surface.Refresh();

        public void SetClickThrough(bool on)
        {
            _clickThrough = on;
            if (Hwnd != IntPtr.Zero) Native.SetClickThrough(Hwnd, on);
        }

        public void StartTyping()
        {
            BeginKeyboard();
            _blinkOn = true;
            _blink.Start();
        }

        private Size Paint(Painter p)
        {
            var (_, _, vw, vh) = Native.VirtualScreen();
            if (_mgr.Capturing)
            {
                p.Fill(CaptureBg, 0, 0, vw, vh);   // nearly invisible, but takes the clicks
                p.Hit(0, 0, vw, vh, b => _mgr.OverlayClicked(b));
            }

            foreach (var a in _mgr.Settings.Items)   // a moved or resized original stays until its copy is anchored
            {
                DrawAnnotation(p, a, _mgr.ColorBrush(a.Color));
                if (_mgr.HandlesVisible && !_mgr.Capturing) DrawHandle(p, a);
            }

            var d = _mgr.Draft;
            if (d != null)
            {
                DrawAnnotation(p, d, White);
                if (_mgr.Step == DrawManager.Phase.Composing)
                {
                    var m = new EramMetrics(d.Font);
                    int cols = Math.Max(Annotations.TextChars, d.Lines.Max(l => l.Length));
                    double w = cols * m.Cell + 8, h = d.Lines.Count * (m.Cap + 6) + 6;
                    p.Frame(White, d.X - 4, d.Y - 4, w, h, 1);
                    if (_blinkOn && KeyboardActive)
                    {
                        int line = d.Lines.Count - 1;
                        p.Fill(White, d.X + d.Lines[line].Length * m.Cell, d.Y + line * (m.Cap + 6) - 1, 2, m.Cap + 2);
                    }
                }
            }
            return new Size(vw, vh);
        }

        private void DrawAnnotation(Painter p, Annotation a, Brush b)
        {
            switch (a.Kind)
            {
                case AnnotationKind.Rectangle:
                    if (a.W >= 1 && a.H >= 1) p.Frame(b, a.X, a.Y, Math.Max(a.W, Stroke), Math.Max(a.H, Stroke), Stroke);
                    break;
                case AnnotationKind.Circle:
                    p.Ellipse(b, a.X, a.Y, a.W, a.H, Stroke);
                    break;
                case AnnotationKind.Line:
                    p.Polyline(b, a.Points.Select(q => new Point(q.X, q.Y)).ToList(), Stroke + 1);
                    break;
                case AnnotationKind.Text:
                    var m = new EramMetrics(a.Font);
                    for (int i = 0; i < a.Lines.Count; i++) p.Text(a.Lines[i], a.X, a.Y + i * (m.Cap + 6), m, b);
                    break;
            }
        }

        /// <summary>The selection handle: a small box with D (anchored to the display), left of the annotation.</summary>
        private void DrawHandle(Painter p, Annotation a)
        {
            var o = a.Origin;
            double hx = o.X - HandleSize - HandleGap, hy = o.Y;
            var b = _mgr.ColorBrush(a.Color);
            p.Fill(HandleBg, hx, hy, HandleSize, HandleSize);
            p.Frame(b, hx, hy, HandleSize, HandleSize, 2);
            ToolbarFont.Text(p, "D", hx + (HandleSize - ToolbarFont.GlyphW) / 2.0, hy + (HandleSize - ToolbarFont.Cap) / 2.0, b);
            var ann = a;
            p.Hit(hx, hy, HandleSize, HandleSize, btn => _mgr.HandleClicked(ann, btn));
        }
    }
}
