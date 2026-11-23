using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// Draws an ERAM element in code. Units are screen pixels (the window applies a transform
    /// that undoes Windows display scaling, as CRC does). The same Paint function runs for
    /// measuring (no DrawingContext) and rendering, so size, drawing and click areas always agree.
    /// </summary>
    public sealed class EramSurface : FrameworkElement
    {
        private readonly List<(Rect Rect, Action<MouseButton> Action)> _hits = new List<(Rect, Action<MouseButton>)>();
        private readonly List<(double Y, double H, object Tag)> _tags = new List<(double, double, object)>();

        public Func<Painter, Size>? Paint { get; set; }
        public Func<bool>? ClicksSuppressed { get; set; }
        public Action<int, Point>? Wheel { get; set; }
        /// <summary>Key handler while the window has keyboard focus (typing mode). Return true if handled.</summary>
        public Func<Key, ModifierKeys, bool>? KeyHandler { get; set; }
        public Action<string>? TextHandler { get; set; }

        public EramSurface()
        {
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            // Smooth text (macro labels): grayscale, since ClearType can't render on see-through windows.
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        }

        internal void AddHit(Rect r, Action<MouseButton> a) => _hits.Add((r, a));
        internal void AddTag(double y, double h, object tag) => _tags.Add((y, h, tag));

        /// <summary>Returns the object tagged at a vertical position (used for wheel input on rows).</summary>
        public object? RowAt(double y)
        {
            foreach (var t in _tags)
                if (y >= t.Y && y < t.Y + t.H) return t.Tag;
            return null;
        }

        public void Refresh()
        {
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize) =>
            Paint?.Invoke(new Painter(null, this)) ?? new Size(1, 1);

        protected override Size ArrangeOverride(Size finalSize) => finalSize;

        protected override void OnRender(DrawingContext dc)
        {
            _hits.Clear();
            _tags.Clear();
            Paint?.Invoke(new Painter(dc, this));
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            e.Handled = true;
            if (ClicksSuppressed?.Invoke() == true) return;
            if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Middle) return;
            var p = e.GetPosition(this);
            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                if (_hits[i].Rect.Contains(p))
                {
                    _hits[i].Action(e.ChangedButton);
                    return;
                }
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (KeyHandler != null && KeyHandler(key, Keyboard.Modifiers)) e.Handled = true;
        }

        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            base.OnTextInput(e);
            if (TextHandler != null && !string.IsNullOrEmpty(e.Text)) { TextHandler(e.Text); e.Handled = true; }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            e.Handled = true;
            Wheel?.Invoke(e.Delta, e.GetPosition(this));
        }
    }

    /// <summary>Drawing helper in screen-pixel units. Every call is a no-op during the measure pass.</summary>
    public sealed class Painter
    {
        private readonly DrawingContext? _dc;
        private readonly EramSurface _surface;
        public bool Drawing => _dc != null;

        internal Painter(DrawingContext? dc, EramSurface surface)
        {
            _dc = dc;
            _surface = surface;
        }

        /// <summary>A painter that draws nothing (for sizing sub-areas).</summary>
        public Painter MeasureOnly() => new Painter(null, _surface);

        public void Fill(Brush b, double x, double y, double w, double h)
        {
            if (_dc != null && w > 0 && h > 0) _dc.DrawRectangle(b, null, new Rect(x, y, w, h));
        }

        /// <summary>Rectangle outline drawn as filled strips, which keeps 1 px lines crisp.</summary>
        public void Frame(Brush b, double x, double y, double w, double h, double t = 1)
        {
            Fill(b, x, y, w, t);
            Fill(b, x, y + h - t, w, t);
            Fill(b, x, y, t, h);
            Fill(b, x + w - t, y, t, h);
        }

        /// <summary>ERAM text: x = left edge of the first character cell, top = top of the caps.</summary>
        public void Text(string s, double x, double top, EramMetrics m, Brush b)
        {
            if (_dc == null || string.IsNullOrEmpty(s)) return;
            double scale = m.S;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == ' ') continue;
                double ox = x + i * m.Cell;
                foreach (var r in EramFont.Runs(s[i]))
                {
                    double x0 = Math.Round(r.X * scale), x1 = Math.Round((r.X + r.W) * scale);
                    double y0 = Math.Round(r.Y * scale), y1 = Math.Round((r.Y + r.H) * scale);
                    Fill(b, ox + x0, top + y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
                }
            }
        }

        public void TextCentered(string s, double x, double w, double top, EramMetrics m, Brush b) =>
            Text(s, x + Math.Floor((w - s.Length * m.Cell) / 2), top, m, b);

        /// <summary>Draws pre-captured ink runs (toolbar labels) at an origin.</summary>
        public void Runs(EramFont.Run[] runs, double x, double y, Brush b)
        {
            foreach (var r in runs) Fill(b, x + r.X, y + r.Y, r.W, r.H);
        }

        /// <summary>Underlines n characters starting at column col of text drawn at x / top.</summary>
        public void Underline(double x, double top, EramMetrics m, int col, int n, Brush b)
        {
            Fill(b, x + col * m.Cell + 1, top + m.Cap + 2, n * m.Cell - 2, 2);
        }

        /// <summary>Filled triangle centered in a box, pointing up or down.</summary>
        public void Triangle(Brush b, double x, double y, double w, double h, bool up)
        {
            if (_dc == null) return;
            double s = Math.Floor(Math.Min(w, h) * 0.5), cx = Math.Floor(x + w / 2), cy = Math.Floor(y + h / 2);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                if (up)
                {
                    ctx.BeginFigure(new Point(cx, cy - s / 2), true, true);
                    ctx.LineTo(new Point(cx + s / 2, cy + s / 2), true, false);
                    ctx.LineTo(new Point(cx - s / 2, cy + s / 2), true, false);
                }
                else
                {
                    ctx.BeginFigure(new Point(cx, cy + s / 2), true, true);
                    ctx.LineTo(new Point(cx + s / 2, cy - s / 2), true, false);
                    ctx.LineTo(new Point(cx - s / 2, cy - s / 2), true, false);
                }
            }
            g.Freeze();
            _dc.DrawGeometry(b, null, g);
        }

        /// <summary>
        /// Smooth (anti-aliased) text in a Windows font, centered across a width with the cap top at
        /// <paramref name="capTop"/>. The size drops in half-point steps until the text fits the width.
        /// </summary>
        public void SmoothText(string s, double x, double w, double capTop, Typeface face, double size, Brush b)
        {
            if (_dc == null || string.IsNullOrEmpty(s)) return;
            FormattedText ft;
            while (true)
            {
                ft = new FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    face, size, b, 1.0);
                if (ft.WidthIncludingTrailingWhitespace <= w || size <= 8) break;
                size -= 0.5;
            }
            double capH = face.CapsHeight * size;
            double tx = Math.Round(x + (w - ft.Width) / 2);
            double ty = Math.Round(capTop - (ft.Baseline - capH));
            _dc.DrawText(ft, new Point(tx, ty));
        }

        /// <summary>Ellipse outline filling a box.</summary>
        public void Ellipse(Brush b, double x, double y, double w, double h, double t = 1)
        {
            if (_dc == null || w <= 0 || h <= 0) return;
            var pen = new Pen(b, t); pen.Freeze();
            _dc.DrawEllipse(null, pen, new Point(x + w / 2, y + h / 2), Math.Max(0, w / 2 - t / 2), Math.Max(0, h / 2 - t / 2));
        }

        /// <summary>Connected line segments through the points.</summary>
        public void Polyline(Brush b, IReadOnlyList<Point> pts, double t = 1)
        {
            if (_dc == null || pts.Count < 2) return;
            var pen = new Pen(b, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                for (int i = 1; i < pts.Count; i++) ctx.LineTo(pts[i], true, true);
            }
            g.Freeze();
            _dc.DrawGeometry(null, pen, g);
        }

        public void Hit(double x, double y, double w, double h, Action<MouseButton> a)
        {
            if (_dc != null) _surface.AddHit(new Rect(x, y, w, h), a);
        }

        public void Tag(object tag, double y, double h)
        {
            if (_dc != null) _surface.AddTag(y, h, tag);
        }
    }
}
