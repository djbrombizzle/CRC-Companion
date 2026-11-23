using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// The VIEWS toolbar's own font (8 x 9 px glyphs, 2 px strokes, 10 px pitch), so companion toolbar buttons
    /// such as RECORD and macros read like CRC's VIEWS labels. Glyph data in ToolbarFont.Data.cs.
    /// </summary>
    public static partial class ToolbarFont
    {
        public const int Pitch = 10, GlyphW = 8, Cap = 9;
        /// <summary>Caps top of line 1 and line 2 in a 79 x 30 button body, as in the captures.</summary>
        public const int Line1Top = 3, Line2Top = 18;

        private static readonly Dictionary<char, EramFont.Run[]> Cache = new Dictionary<char, EramFont.Run[]>();

        public static bool Has(char c) => c == ' ' || Glyphs.ContainsKey(char.ToUpperInvariant(c));

        private static EramFont.Run[] Runs(char c)
        {
            c = char.ToUpperInvariant(c);
            if (Cache.TryGetValue(c, out var r)) return r;
            var list = new List<EramFont.Run>();
            if (Glyphs.TryGetValue(c, out var rows))
            {
                for (int y = 0; y < rows.Length; y++)
                {
                    int x = 0;
                    while (x < rows[y].Length)
                    {
                        if (rows[y][x] != '#') { x++; continue; }
                        int s = x;
                        while (x < rows[y].Length && rows[y][x] == '#') x++;
                        list.Add(new EramFont.Run(s, y, x - s, 1));
                    }
                }
            }
            return Cache[c] = list.ToArray();
        }

        /// <summary>Text with the left edge of the first glyph at x and the caps top at top.</summary>
        public static void Text(Painter p, string s, double x, double top, Brush b)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] != ' ') p.Runs(Runs(s[i]), x + i * Pitch, top, b);
        }

        /// <summary>Pixel width of a line (last glyph's ink edge).</summary>
        public static int Width(string s) => s.Length == 0 ? 0 : s.Length * Pitch - (Pitch - GlyphW);

        /// <summary>Centered in a width, placed the way CRC centers VIEWS labels.</summary>
        public static void TextCentered(Painter p, string s, double x, double w, double top, Brush b) =>
            Text(p, s, x + Math.Floor((w - Width(s)) / 2), top, b);

        /// <summary>
        /// Text at a larger size: each glyph scaled by <paramref name="scale"/> (runs rounded to whole pixels, so
        /// strokes stay solid) on a given pitch.
        /// </summary>
        public static void TextScaled(Painter p, string s, double x, double top, Brush b, double scale, double pitch)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == ' ') continue;
                double ox = Math.Round(x + i * pitch);
                foreach (var r in Runs(s[i]))
                {
                    double x0 = Math.Round(r.X * scale), x1 = Math.Round((r.X + r.W) * scale);
                    double y0 = Math.Round(r.Y * scale), y1 = Math.Round((r.Y + r.H) * scale);
                    p.Fill(b, ox + x0, top + y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
                }
            }
        }

        /// <summary>
        /// A macro button label: one line, larger than the toolbar labels (as ERAM draws macro buttons), centered
        /// both ways in the body. The size shrinks only if the label wouldn't fit.
        /// </summary>
        public static void BigLabel(Painter p, string label, double x, double y, double w, double h, Brush b)
        {
            label = label.Replace('\n', ' ').Trim();
            if (label.Length == 0) return;
            double scale = 1.5, pitch = 14;
            foreach (var (s, pt) in new[] { (1.5, 14.0), (1.375, 12.0), (1.25, 11.0), (1.0, Pitch) })
            {
                scale = s; pitch = pt;
                if ((label.Length - 1) * pitch + Math.Round(GlyphW * scale) <= w - 2) break;
            }
            double width = (label.Length - 1) * pitch + Math.Round(GlyphW * scale);
            double cap = Math.Round(Cap * scale);
            TextScaled(p, label, x + Math.Floor((w - width) / 2), y + Math.Floor((h - cap) / 2), b, scale, pitch);
        }

        /// <summary>
        /// A button label in a 79 x 30 body, as on CRC's VIEWS toolbar: one or two centered lines ("\n" splits),
        /// the first line at the top (single-line labels are top-aligned too, like CFR and CODE).
        /// </summary>
        public static void Label(Painter p, string label, double x, double y, double w, Brush b)
        {
            var lines = label.Split('\n');
            TextCentered(p, lines[0], x, w, y + Line1Top, b);
            if (lines.Length > 1) TextCentered(p, lines[1], x, w, y + Line2Top, b);
        }
    }
}
