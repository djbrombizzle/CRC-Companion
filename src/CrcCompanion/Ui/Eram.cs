using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// ERAM colors. Values sampled from live CRC captures (Nov 21 2026); see
    /// CRC-ERAM-Views-Inventory.md in the project docs.
    /// </summary>
    public static class Eram
    {
        // View chrome
        public static readonly Brush Black = Solid(0x00, 0x00, 0x00);
        public static readonly Brush HalfBlack = Frozen(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)));
        public static readonly Brush ViewBorder = Solid(0x83, 0x83, 0x83);
        public static readonly Brush Marker = Solid(0x9F, 0xA3, 0x09);
        public static readonly Brush MarkerOutline = Solid(0x83, 0x83, 0x83);
        public static readonly Brush ScrollGray = Solid(0x91, 0x91, 0x91);
        /// <summary>Selected-line bar, as in CRC's Check List view.</summary>
        public static readonly Brush Highlight = Solid(0x4B, 0x4B, 0x4B);
        /// <summary>HISTORY title bar DEL ALL button (dark teal).</summary>
        public static readonly Brush DelAll = Solid(0x00, 0x4E, 0x58);
        /// <summary>System message text such as "NO CLOSED MESSAGES FOUND".</summary>
        public static readonly Brush SystemYellow = Solid(0xE0, 0xE0, 0x40);
        /// <summary>MESSAGE OUT special area status (errors).</summary>
        public static readonly Brush ErrorOrange = Solid(0xF0, 0x8A, 0x3A);
        /// <summary>MESSAGE OUT area header bars and the full-text box.</summary>
        public static readonly Brush AreaBar = Solid(0x8C, 0x8C, 0x8C);
        public static readonly Brush AreaBarText = Solid(0xDC, 0xDC, 0xDC);
        /// <summary>Line under every third entry.</summary>
        public static readonly Brush GroupRule = Solid(0x5E, 0x5E, 0x5E);
        /// <summary>SORT menu DELETE row while on.</summary>
        public static readonly Brush DeleteTeal = Solid(0x00, 0x8C, 0x96);
        /// <summary>Non-RVSM indicator box around a filed altitude.</summary>
        public static readonly Brush NonRvsm = Solid(0xE0, 0x5A, 0x50);
        /// <summary>MRP menu RESEQ / DELETE: blue when available, burnt coral when on.</summary>
        public static readonly Brush MrpBlue = Solid(0x00, 0x9C, 0xDC);
        /// <summary>CFR: a newly displayed readout's FLID.</summary>
        public static readonly Brush CfrGreen = Solid(0x00, 0xC8, 0x00);
        public static readonly Brush MrpCoral = Solid(0xD0, 0x6E, 0x64);

        // Menus
        public static readonly Brush MenuTitle = Solid(0x99, 0x99, 0x99);
        public static readonly Brush MenuGreen = Solid(0x00, 0x9D, 0x00);
        public static readonly Brush MenuOn = Solid(0x99, 0x99, 0x99);
        public static readonly Brush MenuText = Solid(0xD0, 0xD0, 0xD0);
        public static readonly Brush MenuLine = Solid(0x83, 0x83, 0x83);

        // Toolbar / VIEWS selector
        public static readonly Brush TbFrame = Solid(0x63, 0x4B, 0x00);
        public static readonly Brush TbBorder = Solid(0x5E, 0x5E, 0x5E);
        public static readonly Brush TbGap = Solid(0x61, 0x61, 0x61);
        public static readonly Brush TbTearoff = Solid(0x7C, 0x7C, 0x4E);
        public static readonly Brush TbOff = Solid(0x00, 0x00, 0x00);
        public static readonly Brush TbOn = Solid(0x61, 0x61, 0x61);
        public static readonly Brush TbText = Solid(0xD5, 0xD5, 0xD5);
        public static readonly Brush TbMenuOpen = Solid(0x6B, 0x4E, 0x4B);
        public static readonly Brush TbMenuClosed = Solid(0x00, 0x00, 0x75);

        // Station entry box (a standard text box, so it uses a system monospace font)
        public static readonly FontFamily EntryFamily = new FontFamily("Consolas");
        public const double EntryFontSize = 13;

        private static readonly Dictionary<int, Brush> GrayCache = new Dictionary<int, Brush>();

        /// <summary>Text gray for a BRIGHT level (80 -> #BBBBBB, CRC's default).</summary>
        public static Brush TextGray(int bright)
        {
            bright = Math.Clamp(bright, 0, 100);
            if (GrayCache.TryGetValue(bright, out var b)) return b;
            var v = (byte)Math.Clamp(Math.Round(234.0 * bright / 100.0), 0, 255);
            b = Solid(v, v, v);
            GrayCache[bright] = b;
            return b;
        }

        private static Brush Solid(byte r, byte g, byte b) => Frozen(new SolidColorBrush(Color.FromRgb(r, g, b)));
        private static Brush Frozen(SolidColorBrush b) { b.Freeze(); return b; }
    }
}
