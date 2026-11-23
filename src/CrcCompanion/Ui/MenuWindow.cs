using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CrcCompanion.Ui
{
    public abstract class MenuRow
    {
        public abstract string Label { get; }
        /// <summary>Background override (e.g. the MRP menu's black COLOR row).</summary>
        public Brush? Back { get; init; }
        /// <summary>Label color override.</summary>
        public Brush? Fore { get; init; }
        /// <summary>Height in rows.</summary>
        public virtual int Rows => 1;
    }

    /// <summary>Four color bars in a 2 x 2 grid (MRP COLOR); the selected one has a white border.</summary>
    public sealed class SwatchRow : MenuRow
    {
        public IReadOnlyList<Brush> Colors { get; }
        public Func<int> Selected { get; }
        public Action<int> Select { get; }
        public SwatchRow(IReadOnlyList<Brush> colors, Func<int> selected, Action<int> select) { Colors = colors; Selected = selected; Select = select; }
        public override string Label => "";
        public override int Rows => 2;
    }

    /// <summary>A sublist name (in its color) with a SEC / MIN cell at the right (MRP menu).</summary>
    public sealed class SublistRow : MenuRow
    {
        private readonly string _label;
        public Func<string> Right { get; }
        public Action Pick { get; }
        public Action PickRight { get; }
        public SublistRow(string label, Func<string> right, Action pick, Action pickRight) { _label = label; Right = right; Pick = pick; PickRight = pickRight; }
        public string Name => _label;
        /// <summary>Name plus room for the SEC / MIN cell (the menu sizes itself from labels).</summary>
        public override string Label => _label + "     ";
    }

    /// <summary>
    /// The O/T row. As in CRC it shows the mode a click switches to:
    /// "T" while the view is opaque, "O" while it is semi-transparent.
    /// </summary>
    public sealed class ModeRow : MenuRow
    {
        private readonly Func<bool> _opaque;
        public Action Toggle { get; }
        public ModeRow(Func<bool> opaque, Action toggle) { _opaque = opaque; Toggle = toggle; }
        public override string Label => _opaque() ? "T" : "O";
    }

    public sealed class ToggleRow : MenuRow
    {
        private readonly string _label;
        public Func<bool> IsOn { get; }
        public Action Toggle { get; }
        /// <summary>Background while on (default: the menu's gray).</summary>
        public Brush? OnBrush { get; }
        /// <summary>Background while off (default: black).</summary>
        public Brush? OffBrush { get; }
        public ToggleRow(string label, Func<bool> isOn, Action toggle, Brush? onBrush = null, Brush? offBrush = null)
        { _label = label; IsOn = isOn; Toggle = toggle; OnBrush = onBrush; OffBrush = offBrush; }
        public override string Label => _label;
    }

    /// <summary>
    /// A row you type into (ERAM's sublist entry in the INBND / DEPT menus): click it, type, Enter submits.
    /// Esc or clicking elsewhere cancels. The menu takes the keyboard only while typing.
    /// </summary>
    public sealed class InputRow : MenuRow
    {
        public string Text { get; set; } = "";
        public int MaxChars { get; }
        public Action<string> Submit { get; }
        public InputRow(Action<string> submit, int maxChars = 5) { Submit = submit; MaxChars = maxChars; }
        public override string Label => Text;
    }

    /// <summary>Numeric row. As in CRC: middle click increments, left click decrements. Wheel also works.</summary>
    public sealed class StepRow : MenuRow
    {
        private readonly string _label;
        public Func<int> Get { get; }
        public Action<int> Set { get; }
        public int Min { get; }
        public int Max { get; }
        public int Step { get; }
        public StepRow(string label, Func<int> get, Action<int> set, int min, int max, int step = 1)
        { _label = label; Get = get; Set = set; Min = min; Max = max; Step = step; }
        public override string Label => _label + " " + Get();
        public void Nudge(int dir) => Set(Math.Clamp(Get() + dir * Step, Min, Max));
    }

    public sealed class CommandRow : MenuRow
    {
        private readonly string _label;
        public Action<MouseButton>? Run { get; }
        public CommandRow(string label, Action<MouseButton>? run) { _label = label; Run = run; }
        public override string Label => _label;
    }

    /// <summary>
    /// ERAM view menu, measured from CRC (screen pixels): 1 px border, 17 px rows separated by
    /// 1 px lines, gray title row with a 23 px X cell, text 5 px in from the border.
    /// Inner width fits 11 characters (or the longest label plus 2).
    /// </summary>
    public sealed class MenuWindow : EramWindow
    {
        private readonly string _title;
        private readonly IReadOnlyList<MenuRow> _rows;
        private static readonly EramMetrics M = new EramMetrics(2);
        private const int TextInset = 5;

        /// <summary>
        /// What a click on the title does. A view's menu sets this to move the view (the menu stays attached and
        /// follows it); left null, the title moves the menu itself.
        /// </summary>
        public Action? TitleDrag { get; set; }

        public MenuWindow(string title, IReadOnlyList<MenuRow> rows)
        {
            _title = title;
            _rows = rows;
            Surface.Paint = Paint;
            Surface.Wheel = OnWheel;
            Surface.KeyHandler = OnKey;
            Surface.TextHandler = OnText;
            KeyboardEnded += () => { if (_editing != null) _editing.Text = ""; _editing = null; Refresh(); };
        }

        private InputRow? _editing;

        private void StartEditing(InputRow row)
        {
            _editing = row;
            row.Text = "";
            BeginKeyboard();
            Refresh();
        }

        private bool OnKey(Key key, ModifierKeys mods)
        {
            if (!KeyboardActive || _editing == null) return false;
            var row = _editing;
            switch (key)
            {
                case Key.Escape:
                    EndKeyboard();
                    return true;
                case Key.Enter:
                    var text = row.Text.Trim();
                    EndKeyboard();
                    if (text.Length > 0) row.Submit(text);
                    Refresh();
                    return true;
                case Key.Back:
                    if (row.Text.Length > 0) row.Text = row.Text.Substring(0, row.Text.Length - 1);
                    Refresh();
                    return true;
                default:
                    return false;
            }
        }

        private void OnText(string text)
        {
            if (!KeyboardActive || _editing == null) return;
            foreach (var c in text.ToUpperInvariant())
                if (((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) && _editing.Text.Length < _editing.MaxChars) _editing.Text += c;
            Refresh();
        }

        public void Refresh() => Surface.Refresh();

        private Size Paint(Painter p)
        {
            var m = M;
            int rh = m.TitleH, box = m.BoxW;
            int chars = Math.Max(11, _rows.Select(r => r.Label.Length).DefaultIfEmpty(0).Max() + 2);
            chars = Math.Max(chars, _title.Length + 4);
            double inner = 6 + m.TextWidth(chars);
            double w = inner + 2;
            double h = 1 + rh + _rows.Sum(r => r.Rows * rh + 1) + 1;
            double gt = m.GlyphTopInTitle;

            // Title row: [   TITLE   |X]
            double y = 1;
            p.Fill(Eram.MenuTitle, 1, y, inner, rh);
            double titleW = inner - box - 1;
            p.TextCentered(_title, 1, titleW, y + gt, m, Eram.MenuText);
            p.Fill(Eram.MenuLine, 1 + titleW, y, 1, rh);
            p.TextCentered("X", 2 + titleW, box, y + gt, m, Eram.MenuText);
            p.Hit(2 + titleW, y, box, rh, _ => Close());
            p.Hit(1, y, titleW, rh, _ => (TitleDrag ?? BeginReposition)());
            y += rh;

            foreach (var row in _rows)
            {
                p.Fill(Eram.MenuLine, 1, y, inner, 1);
                y += 1;
                Brush bg = Eram.Black;
                bool centered = false;
                switch (row)
                {
                    case ModeRow md:
                        centered = true;
                        p.Hit(1, y, inner, rh, _ => { md.Toggle(); Refresh(); });
                        break;
                    case ToggleRow t:
                        bg = t.IsOn() ? t.OnBrush ?? Eram.MenuOn : t.OffBrush ?? Eram.Black;
                        p.Hit(1, y, inner, rh, _ => { t.Toggle(); Refresh(); });
                        break;
                    case StepRow s:
                        bg = Eram.MenuGreen;
                        p.Hit(1, y, inner, rh, b => { s.Nudge(b == MouseButton.Middle ? +1 : -1); Refresh(); });
                        break;
                    case SwatchRow sw:
                    {
                        p.Fill(Eram.Black, 1, y, inner, sw.Rows * rh);
                        double cw = (inner - 18) / 2.0, ch = rh - 4;
                        for (int i = 0; i < sw.Colors.Count && i < 4; i++)
                        {
                            double cx = 1 + 6 + (i % 2) * (cw + 6), cy = y + 3 + (i / 2) * rh;
                            if (sw.Selected() == i) p.Frame(Eram.MenuText, cx - 2, cy - 2, cw + 4, ch + 4, 1);
                            p.Fill(sw.Colors[i], cx, cy, cw, ch);
                            int idx = i;
                            p.Hit(cx - 2, cy - 2, cw + 4, ch + 4, _ => { sw.Select(idx); Refresh(); });
                        }
                        p.Tag(row, y, sw.Rows * rh);
                        y += sw.Rows * rh;
                        continue;
                    }
                    case SublistRow sl:
                    {
                        var right = sl.Right();
                        double rw = m.TextWidth(right.Length) + 4;
                        p.Fill(Eram.Black, 1, y, inner, rh);
                        p.Text(sl.Name, 1 + TextInset, y + gt, m, sl.Fore ?? Eram.MenuText);
                        p.Fill(Eram.MenuOn, 1 + inner - rw, y + 1, rw, rh - 2);
                        p.Text(right, 1 + inner - rw + 2, y + gt, m, Eram.Black);
                        p.Hit(1, y, inner - rw, rh, _ => { sl.Pick(); Refresh(); });
                        p.Hit(1 + inner - rw, y, rw, rh, _ => { sl.PickRight(); Refresh(); });
                        p.Tag(row, y, rh);
                        y += rh;
                        continue;
                    }
                    case InputRow ir:
                        p.Hit(1, y, inner, rh, _ => StartEditing(ir));
                        break;
                    case CommandRow c:
                        var run = c.Run;
                        if (run != null) p.Hit(1, y, inner, rh, b => run(b));
                        break;
                }
                if (row.Back != null && !(row is ToggleRow tr && tr.IsOn())) bg = row.Back;
                var fore = row.Fore ?? Eram.MenuText;
                p.Fill(bg, 1, y, inner, rh);
                if (centered) p.TextCentered(row.Label, 1, inner, y + gt, m, fore);
                else p.Text(row is InputRow edit && ReferenceEquals(edit, _editing) ? edit.Text + "_" : row.Label, 1 + TextInset, y + gt, m, fore);
                p.Tag(row, y, rh);
                y += rh;
            }

            p.Frame(Eram.ViewBorder, 0, 0, w, h, 1);
            return new Size(w, h);
        }

        private void OnWheel(int delta, Point pos)
        {
            if (Surface.RowAt(pos.Y) is StepRow row)
            {
                row.Nudge(delta > 0 ? +1 : -1);
                Refresh();
            }
        }
    }
}
