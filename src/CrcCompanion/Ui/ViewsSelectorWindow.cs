using System;
using System.Windows;
using System.Windows.Media;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// Copy of CRC's ERAM VIEWS Toolbar menu, in screen pixels (live capture, Nov 21 2026).
    /// Cell 94 x 32: 1 px border, 11 px tear-off bar, 2 px border, 79 px body, 1 px border.
    /// 2 px gaps, 2 px gold frame around the 2 x 10 grid; the VIEWS button sits left of the frame.
    /// Label pixels are drawn from the capture (ToolbarLabels), so they match exactly.
    /// A button is live when the app has a view for it (see AppController); others do nothing.
    /// </summary>
    public sealed class ViewsSelectorWindow : EramWindow, ITearOffSource
    {
        public string Menu => "VIEWS";
        public Action<string>? TearOff { get; set; }

        public bool HasTearOff(string id) => id == InSectorKey || System.Linq.Enumerable.Contains(Labels(), id);

        private static System.Collections.Generic.IEnumerable<string> Labels()
        {
            foreach (var l in Grid) if (l != null) yield return l;
        }

        /// <summary>A torn-off view button: toggles its view like the one in the grid.</summary>
        public Size PaintTearOff(string id, Painter p, TearOffWindow w)
        {
            bool wired = id == InSectorKey || _wired(id);
            bool on = wired && _isOn(id);
            DrawCell(p, 0, 0, id, on ? Eram.TbOn : Eram.TbOff, onBody: wired ? () => _toggle(id) : (Action?)null, onBar: null);
            return new Size(CellW, CellH);
        }

        public const string AltimKey = "ALTIM SET";
        public const string WxKey = "WX REPORT";
        public const string InboundKey = "INBND LIST";
        public const string DeptKey = "DEPT LIST";
        /// <summary>Not an ERAM button: drawn in its own framed row under the grid.</summary>
        public const string InSectorKey = "IN SECTOR";
        /// <summary>Companion button placed in the grid's blank cell (row 2, column 10).</summary>
        public const string StatusKey = "STATUS";

        private readonly Func<string, bool> _wired;

        private static readonly string?[,] Grid =
        {
            { "ALTIM SET", "AUTO HO INHIB", "CFR", "CODE", "CONFLCT ALERT", "CPDLC ADV", "CPDLC HIST", "CPDLC MSGOUT", "CPDLC TOC SET", "CRR" },
            { "DEPT LIST", "FLIGHT EVENT", "GROUP SUP", "HOLD LIST", "INBND LIST", "MRP LIST", "SAA FILTER", "UA", "WX REPORT", "STATUS" },
        };

        private const int CellW = 94, CellH = 32, Gap = 2, FrameT = 2, ExtraGap = 4;
        private const int BarX = 1, BarW = 11, BodyX = 14, BodyY = 1;

        private readonly SelectorSettings _settings;
        private readonly Func<string, bool> _isOn;
        private readonly Action<string> _toggle;

        public event Action? Changed;

        public ViewsSelectorWindow(SelectorSettings settings, Func<string, bool> isOn, Action<string> toggle, Func<string, bool> wired)
        {
            _wired = wired;
            _settings = settings;
            _isOn = isOn;
            _toggle = toggle;
            Surface.Paint = Paint;
            Repositioned += _ =>
            {
                var (x, y) = ScreenPosition;
                _settings.X = x;
                _settings.Y = y;
                Changed?.Invoke();
            };
        }

        public void Refresh() => Surface.Refresh();

        private Size Paint(Painter p)
        {
            double gridW = FrameT * 2 + 10 * CellW + 9 * Gap;
            double gridH = FrameT * 2 + 2 * CellH + Gap;

            // VIEWS menu button, aligned with grid row 1.
            DrawCell(p, 0, FrameT, "VIEWS",
                _settings.Open ? Eram.TbMenuOpen : Eram.TbMenuClosed,
                onBody: () => { _settings.Open = !_settings.Open; Surface.Refresh(); Changed?.Invoke(); },
                onBar: BeginReposition);

            if (!_settings.Open) return new Size(CellW, gridH);

            double gx = CellW;
            p.Fill(Eram.TbFrame, gx, 0, gridW, gridH);
            p.Fill(Eram.TbGap, gx + FrameT, FrameT, gridW - FrameT * 2, gridH - FrameT * 2);

            for (int r = 0; r < 2; r++)
            {
                for (int c = 0; c < 10; c++)
                {
                    var label = Grid[r, c];
                    if (label == null) continue;
                    double cx = gx + FrameT + c * (CellW + Gap);
                    double cy = FrameT + r * (CellH + Gap);
                    bool wired = _wired(label);
                    bool on = wired && _isOn(label);
                    DrawCell(p, cx, cy, label, on ? Eram.TbOn : Eram.TbOff,
                        onBody: wired ? () => _toggle(label) : (Action?)null,
                        onBar: () => TearOff?.Invoke(label));
                }
            }

            // Companion-only row: IN SECTOR, framed like the grid, under its first column.
            double ey = gridH + ExtraGap;
            p.Fill(Eram.TbFrame, gx, ey, CellW + FrameT * 2, CellH + FrameT * 2);
            bool inSectorOn = _isOn(InSectorKey);
            DrawCell(p, gx + FrameT, ey + FrameT, InSectorKey, inSectorOn ? Eram.TbOn : Eram.TbOff,
                onBody: () => _toggle(InSectorKey), onBar: () => TearOff?.Invoke(InSectorKey));

            return new Size(CellW + gridW, ey + CellH + FrameT * 2);
        }

        private static void DrawCell(Painter p, double x, double y, string label, Brush body, Action? onBody, Action? onBar)
        {
            p.Fill(Eram.TbBorder, x, y, CellW, CellH);
            p.Fill(Eram.TbTearoff, x + BarX, y + BodyY, BarW, ToolbarLabels.Height);
            p.Fill(body, x + BodyX, y + BodyY, ToolbarLabels.Width, ToolbarLabels.Height);
            p.Runs(ToolbarLabels.Get(label), x + BodyX, y + BodyY, Eram.TbText);

            if (onBar != null) p.Hit(x, y, BodyX, CellH, _ => onBar());
            if (onBody != null) p.Hit(x + BodyX, y, ToolbarLabels.Width + 1, CellH, _ => onBody());
        }
    }
}
