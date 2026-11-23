using System;
using System.Collections.Generic;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// The ERAM bitmap font. Glyph data lives in EramFont.Data.cs. Glyphs are drawn as filled
    /// rectangles in screen pixels, with no smoothing, the way CRC draws them.
    /// </summary>
    public static partial class EramFont
    {
        public const int CellW = 12;
        public const int CapH = 11;

        /// <summary>A rectangle in glyph pixel units.</summary>
        public readonly struct Run
        {
            public readonly int X, Y, W, H;
            public Run(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
        }

        private static readonly Dictionary<char, Run[]> RunCache = new Dictionary<char, Run[]>();

        public static bool Has(char c) => Glyphs.ContainsKey(char.ToUpperInvariant(c));

        public static IEnumerable<char> Characters => Glyphs.Keys;

        /// <summary>Glyph as horizontal ink runs (rows merged where identical runs stack).</summary>
        public static Run[] Runs(char c)
        {
            c = char.ToUpperInvariant(c);
            if (RunCache.TryGetValue(c, out var cached)) return cached;
            if (!Glyphs.TryGetValue(c, out var rows))
            {
                // Unknown character: draw nothing (space) rather than a wrong shape.
                RunCache[c] = Array.Empty<Run>();
                return RunCache[c];
            }

            var flat = new List<Run>();
            for (int y = 0; y < rows.Length; y++)
            {
                int bits = rows[y];
                int x = 0;
                while (x < CellW)
                {
                    if (((bits >> (CellW - 1 - x)) & 1) == 1)
                    {
                        int s = x;
                        while (x < CellW && ((bits >> (CellW - 1 - x)) & 1) == 1) x++;
                        flat.Add(new Run(s, y, x - s, 1));
                    }
                    else x++;
                }
            }

            // Merge vertically stacked identical runs into taller rectangles.
            var merged = new List<Run>();
            var used = new bool[flat.Count];
            for (int i = 0; i < flat.Count; i++)
            {
                if (used[i]) continue;
                var r = flat[i];
                int h = 1;
                while (true)
                {
                    int next = flat.FindIndex(i + 1, f => f.X == r.X && f.W == r.W && f.Y == r.Y + h);
                    if (next < 0 || used[next]) break;
                    used[next] = true;
                    h++;
                }
                merged.Add(new Run(r.X, r.Y, r.W, h));
            }
            var arr = merged.ToArray();
            RunCache[c] = arr;
            return arr;
        }

        /// <summary>Text-mode preview of a glyph (used by tests).</summary>
        public static string[] Preview(char c)
        {
            var grid = new char[CapH][];
            for (int y = 0; y < CapH; y++) grid[y] = new string('.', CellW).ToCharArray();
            foreach (var r in Runs(c))
                for (int y = r.Y; y < r.Y + r.H; y++)
                    for (int x = r.X; x < r.X + r.W; x++)
                        grid[y][x] = '#';
            var result = new string[CapH];
            for (int y = 0; y < CapH; y++) result[y] = new string(grid[y]);
            return result;
        }
    }

    /// <summary>
    /// Pixel layout for an ERAM view at a given FONT level, measured from CRC at FONT 2:
    /// 12 px character cells, 11 px caps, 17 px title bar, 21 px rows, 13 x 17 markers.
    /// Other FONT levels scale the cell (FONT 2 is the measured size).
    /// </summary>
    public readonly struct EramMetrics
    {
        public readonly double S;     // glyph scale relative to FONT 2
        public readonly int Cell;     // character pitch
        public readonly int Cap;      // cap height
        public readonly int Row;      // list row pitch
        public readonly int TitleH;   // title bar / menu row height
        public readonly int GlyphTopInRow;
        public readonly int GlyphTopInTitle;
        public readonly int BoxW;     // M, -, X and scroll cells
        public readonly int MarkerW, MarkerH;

        public EramMetrics(int fontLevel)
        {
            // 10 px cells at FONT 1, +2 px per level (FONT 2 = 12 px); levels 6-8 are for DRAW text annotations.
            S = fontLevel >= 1 && fontLevel <= 8 ? (8 + 2 * fontLevel) / 12.0 : 1.0;
            Cell = (int)Math.Round(12 * S);
            Cap = (int)Math.Round(11 * S);
            Row = Cap + 10;
            TitleH = Cap + 6;
            MarkerW = 13;
            MarkerH = Row - 4;                         // 17 at FONT 2
            GlyphTopInRow = MarkerH - Cap - 1;         // 5 at FONT 2 (1 px above the marker's bottom)
            GlyphTopInTitle = (TitleH - Cap) / 2;      // 3 at FONT 2
            BoxW = Cell * 2 - 1;                       // 23 at FONT 2
        }

        public int TextWidth(int chars) => chars * Cell;
    }
}
