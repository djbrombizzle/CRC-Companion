using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// UA, the ERAM Update Area view (manual 6.3): a Message Waiting Area and a text area. MESSAGE WAITING is
    /// emphasized (yellow) while a message needs acknowledgement; click it to acknowledge the oldest one, which
    /// then shows at the top of the text area. A scroll bar appears when the text is longer than the area.
    /// Middle-click a message to delete it. M menu: MSG WAIT and TEXT show or suppress each area.
    ///
    /// Messages (VATSIM stand-ins for ERAM's GI messages and UTMs):
    ///   GI    General Information notes sent from the vUSAlink EDST (GI button) by your ARTCC and its tier-1
    ///         neighbors, through the vUSAlink hub;
    ///   TMU   VATFLOW Airport TMU ground stops, restrictions and arrival rate programs that concern your ARTCC;
    ///   FLOW  VATFLOW FCA programs for your ARTCC turned on, amended or ended.
    /// </summary>
    public sealed class UpdateAreaView : DataListView
    {
        private const int TextChars = 36;
        private const int TextRows = 5;
        private const int ScrollRightPad = 4;
        private int _scroll;

        public UpdateAreaView(IViewHost h, ViewSettings s, ILiveSource l) : base(h, s, "UA", l) { }

        protected override string MenuTitle => "UA";
        protected override int BodyPadding => 0;
        protected override List<DataLine> BuildLines() => new List<DataLine>();

        private bool ShowWait => !Settings.Values.TryGetValue("nowait", out var v) || v != "1";
        private bool ShowText => !Settings.Values.TryGetValue("notext", out var v) || v != "1";

        private void Flip(string key)
        {
            bool off = Settings.Values.TryGetValue(key, out var v) && v == "1";
            Settings.Values[key] = off ? "0" : "1";
            Changed();
        }

        protected override IReadOnlyList<MenuRow> BuildMenuRows() => new List<MenuRow>
        {
            new ModeRow(() => Settings.Opaque, () => { Settings.Opaque = !Settings.Opaque; Changed(); }),
            new ToggleRow("BORDER", () => Settings.Border, () => { Settings.Border = !Settings.Border; Changed(); }),
            FontRow(),
            BrightRow(),
            new ToggleRow("MSG WAIT", () => ShowWait, () => Flip("nowait")),
            new ToggleRow("TEXT", () => ShowText, () => Flip("notext")),
        };

        protected override void OnBodyWheel(int delta)
        {
            _scroll += delta > 0 ? -1 : 1;
            Surface.Refresh();
        }

        protected override Size PaintBody(Painter p, double x0, double y0)
        {
            var m = M;
            int rowH = m.TitleH;
            int gt = m.GlyphTopInTitle;
            int sbW = m.BoxW;
            var tb = TextBrush;
            var ua = Live.UpdateArea;
            double width = LeftPad + m.TextWidth(TextChars) + EntryGap + sbW + ScrollRightPad;
            double y = y0;

            if (ShowWait)
            {
                bool waiting = ua.Waiting.Count > 0;
                double areaH = rowH + 8;
                if (p.Drawing)
                {
                    var brush = waiting ? Eram.SystemYellow : Eram.TextGray(Math.Max(10, Settings.Bright / 3));
                    p.TextCentered("MESSAGE WAITING", x0, width, y + 4 + gt, m, brush);
                }
                if (waiting) p.Hit(x0, y, width, areaH, _ => { ua.AcknowledgeNext(); _scroll = 0; Changed(); });
                y += areaH;
                if (ShowText && p.Drawing) p.Fill(Eram.ViewBorder, x0, y, width, 1);
                if (ShowText) y += 1;
            }

            if (ShowText)
            {
                // Each acknowledged message, newest first, wrapped under itself.
                var lines = new List<(string Text, UaMessage? Msg)>();
                var status = Live.UaStatus;
                if (status.Length > 0) lines.Add((status, null));
                foreach (var msg in ua.Shown)
                    foreach (var part in Metar.Wrap(msg.Display, TextChars)) lines.Add((part, msg));

                int maxScroll = Math.Max(0, lines.Count - TextRows);
                _scroll = Math.Clamp(_scroll, 0, maxScroll);
                double top = y + 4;
                if (p.Drawing)
                {
                    for (int v = 0; v < TextRows && _scroll + v < lines.Count; v++)
                    {
                        var (text, msg) = lines[_scroll + v];
                        double ry = top + v * rowH;
                        p.Text(text, x0 + LeftPad, ry + gt, m, msg == null ? Eram.SystemYellow : tb);
                        if (msg != null)
                        {
                            var key = msg.Key;
                            p.Hit(x0, ry, LeftPad + m.TextWidth(TextChars), rowH, b =>
                            {
                                if (b == MouseButton.Middle)
                                    OpenPopup("UA", new List<MenuRow> { new CommandRow("DELETE", _ => { ua.Delete(key); ClosePopup(); Changed(); }) });
                            });
                        }
                    }
                }
                double areaH = TextRows * rowH + 8;
                if (p.Drawing && lines.Count > TextRows)
                    DrawScrollBar(p, x0 + width - ScrollRightPad - sbW, y + 2, sbW, areaH - 4, lines.Count, TextRows, maxScroll);
                y += areaH;
            }
            if (y == y0) y += 4;
            return new Size(width, y - y0);
        }

        private void DrawScrollBar(Painter p, double sx, double sy, int sbW, double sh, int total, int visible, int maxScroll)
        {
            p.Frame(Eram.ViewBorder, sx, sy, sbW, sh, 1);
            p.Fill(Eram.ViewBorder, sx, sy + sbW, sbW, 1);
            p.Fill(Eram.ViewBorder, sx, sy + sh - sbW - 1, sbW, 1);
            p.Triangle(Eram.ScrollGray, sx, sy, sbW, sbW, up: true);
            p.Triangle(Eram.ScrollGray, sx, sy + sh - sbW, sbW, sbW, up: false);
            p.Hit(sx, sy, sbW, sbW, _ => { _scroll--; Surface.Refresh(); });
            p.Hit(sx, sy + sh - sbW, sbW, sbW, _ => { _scroll++; Surface.Refresh(); });

            double trackY = sy + sbW + 1, trackH = sh - 2 * sbW - 2;
            if (trackH <= 6) return;
            double frac = Math.Min(1.0, visible / (double)Math.Max(total, 1));
            double thumbH = Math.Max(6, Math.Floor((trackH - 4) * frac));
            double thumbY = Math.Floor(trackY + 2 + (trackH - 4 - thumbH) * (maxScroll == 0 ? 0 : _scroll / (double)maxScroll));
            p.Frame(Eram.ScrollGray, sx + 3, thumbY, sbW - 6, thumbH, 1);
        }
    }
}
