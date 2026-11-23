using System;
using System.Windows.Media;
using System.Windows.Threading;
using CrcCompanion.Core;
using CrcCompanion.Services;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// ERAM COMMAND MENUS toolbar button (ERAM manual 13.9). It opens RNG BRG, whose menu lists LA to LE.
    /// Picking a command CRC runs types its code into CRC's MCA, with no Enter, so the controller finishes it in CRC.
    /// </summary>
    public sealed class CommandMenusWindow : EramWindow
    {
        private const int LineH = 14, Pad = 4, ListChars = 22;
        private static readonly Brush Dim = MakeBrush(0x80, 0x80, 0x80);

        private readonly CommandMenuSettings _settings;
        private readonly Action _dirty;
        private readonly DispatcherTimer _timer;
        private bool _rngBrgOpen, _busy;
        private string _feedback = "";
        private DateTime _feedbackUntil;

        public CommandMenusWindow(CommandMenuSettings settings, Action dirty)
        {
            _settings = settings;
            _dirty = dirty;
            Surface.Paint = Paint;
            Repositioned += _ =>
            {
                var (x, y) = ScreenPosition;
                _settings.X = x; _settings.Y = y;
                _dirty();
            };
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) =>
            {
                if (_feedback.Length > 0 && DateTime.UtcNow > _feedbackUntil) { _feedback = ""; Surface.Refresh(); }
            };
            _timer.Start();
            Closed += (_, _) => _timer.Stop();
        }

        private static Brush MakeBrush(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

        public void Refresh() => Surface.Refresh();

        private void Say(string text, int seconds = 10)
        {
            _feedback = text;
            _feedbackUntil = DateTime.UtcNow.AddSeconds(seconds);
            Surface.Refresh();
        }

        private void ToggleOpen()
        {
            _settings.Open = !_settings.Open;
            if (!_settings.Open) _rngBrgOpen = false;
            _dirty();
            Surface.Refresh();
        }

        private void ToggleRngBrg()
        {
            _rngBrgOpen = !_rngBrgOpen;
            Surface.Refresh();
        }

        private async void Pick(MenuCommand c)
        {
            var text = CommandMenus.TypedText(c);
            if (text == null) { Say(c.Prompt); return; }
            if (_busy) { Say("BUSY"); return; }
            _busy = true;
            try
            {
                var why = await CrcTyper.Type(new[] { text }, enterLast: false);
                Say(why == null ? c.Prompt : "NOT TYPED: " + why);
            }
            catch (Exception ex) { Say("NOT TYPED: " + ex.Message.ToUpperInvariant()); }
            finally { _busy = false; }
        }

        private System.Windows.Size Paint(Painter p)
        {
            var m = TbCell.M;
            double right = TbCell.W, bottom = TbCell.FrameT * 2 + TbCell.H;
            bool open = _settings.Open;

            TbCell.WithBar(p, 0, TbCell.FrameT, "COMMAND\nMENUS", open ? Eram.TbMenuOpen : Eram.TbMenuClosed,
                onBody: ToggleOpen, onBar: BeginReposition);

            if (open)
            {
                double gx = TbCell.W, gridW = TbCell.FrameT * 2 + TbCell.W;
                p.Fill(Eram.TbFrame, gx, 0, gridW, bottom);
                p.Fill(Eram.TbGap, gx + TbCell.FrameT, TbCell.FrameT, gridW - TbCell.FrameT * 2, TbCell.H);
                double cx = gx + TbCell.FrameT, y = TbCell.FrameT;
                TbCell.Plain(p, cx, y, "RNG BRG", _rngBrgOpen ? Eram.TbOn : Eram.TbOff, _ => ToggleRngBrg());
                right = gx + gridW;

                // RNG BRG menu: under its button, one row per command; ones CRC lacks are dimmed.
                if (_rngBrgOpen)
                {
                    double lx = cx, ly = bottom, lw = ListChars * m.Cell + Pad * 2;
                    foreach (var c in CommandMenus.RngBrg)
                    {
                        p.Fill(Eram.TbFrame, lx, ly, lw, LineH);
                        p.Fill(TbCell.ListRow, lx + 1, ly, lw - 2, LineH - 1);
                        ToolbarFont.Text(p, c.Label, lx + Pad, ly + 2, c.InCrc ? Eram.TbText : Dim);
                        var cmd = c;
                        p.Hit(lx, ly, lw, LineH, _ => Pick(cmd));
                        ly += LineH;
                    }
                    right = Math.Max(right, lx + lw);
                    bottom = ly + 1;
                }
            }

            // Feedback line (ERAM shows these in the MCA; CRC's MCA can't show ours).
            if (_feedback.Length > 0)
            {
                double fy = bottom + 2, fw = _feedback.Length * m.Cell + Pad * 2;
                p.Fill(Eram.Black, 0, fy, fw, LineH);
                ToolbarFont.Text(p, _feedback, Pad, fy + 2, Eram.SystemYellow);
                right = Math.Max(right, fw);
                bottom = fy + LineH;
            }
            return new System.Windows.Size(right, bottom);
        }
    }
}
