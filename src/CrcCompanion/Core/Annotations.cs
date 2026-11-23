using System;
using System.Collections.Generic;
using System.Linq;

namespace CrcCompanion.Core
{
    public enum AnnotationKind { Rectangle, Circle, Line, Text }

    /// <summary>A point in screen pixels, relative to the drawing overlay's origin.</summary>
    public sealed class AnnPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public AnnPoint() { }
        public AnnPoint(double x, double y) { X = x; Y = y; }
    }

    /// <summary>
    /// One DRAW annotation (ERAM manual 5.2). Anchored to the display: positions are screen pixels relative to the
    /// overlay origin. Rectangles and circles use X/Y/W/H (a circle fills its box); lines use Points; text uses
    /// X/Y (top-left) and Lines.
    /// </summary>
    public sealed class Annotation
    {
        public AnnotationKind Kind { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public List<AnnPoint> Points { get; set; } = new List<AnnPoint>();
        public List<string> Lines { get; set; } = new List<string>();
        /// <summary>Text size, 1-8.</summary>
        public int Font { get; set; } = Annotations.DefaultFont;
        /// <summary>Index into <see cref="Annotations.ColorNames"/>.</summary>
        public int Color { get; set; }

        /// <summary>Top-left of the annotation, where its selection handle sits.</summary>
        public AnnPoint Origin => Kind == AnnotationKind.Line && Points.Count > 0
            ? new AnnPoint(Points.Min(p => p.X), Points.Min(p => p.Y))
            : new AnnPoint(X, Y);

        /// <summary>Moves the whole annotation by an offset.</summary>
        public void Offset(double dx, double dy)
        {
            X += dx; Y += dy;
            foreach (var p in Points) { p.X += dx; p.Y += dy; }
        }

        public Annotation Clone() => new Annotation
        {
            Kind = Kind, X = X, Y = Y, W = W, H = H, Font = Font, Color = Color,
            Points = Points.Select(p => new AnnPoint(p.X, p.Y)).ToList(),
            Lines = new List<string>(Lines),
        };
    }

    /// <summary>Saved DRAW toolbar state and annotations (the profile's equivalent of an ERAM preference set).</summary>
    public sealed class DrawSettings
    {
        public int? X { get; set; }
        public int? Y { get; set; }
        public bool Open { get; set; }
        /// <summary>Selected color for new annotations (and for recoloring one by middle-clicking its handle).</summary>
        public int Color { get; set; }
        /// <summary>Annotation brightness, 0-100 (ERAM default 60). Applies to every annotation.</summary>
        public int Brightness { get; set; } = Annotations.DefaultBrightness;
        /// <summary>A FONT: text size for new text, 1-8.</summary>
        public int Font { get; set; } = Annotations.DefaultFont;
        public List<Annotation> Items { get; set; } = new List<Annotation>();
    }

    /// <summary>ERAM DRAW rules (ERAM manual 5.2 and 5.3), kept apart from the UI so they can be tested.</summary>
    public static class Annotations
    {
        public const int Max = 200, MaxSegments = 29, TextChars = 20, TextLines = 5;
        public const int DefaultFont = 2, MinFont = 1, MaxFont = 8;
        public const int DefaultBrightness = 60, BrightnessStep = 2;
        /// <summary>The four annotation colors, in Color Selection order.</summary>
        public static readonly string[] ColorNames = { "GREEN", "WHITE", "CORAL", "YELLOW" };
        public static readonly (byte R, byte G, byte B)[] ColorRgb =
        {
            (0x00, 0xFF, 0x00), (0xFF, 0xFF, 0xFF), (0xFF, 0x7F, 0x50), (0xFF, 0xFF, 0x00),
        };

        /// <summary>An annotation color at a brightness (0-100), as RGB.</summary>
        public static (byte R, byte G, byte B) Rgb(int color, int brightness)
        {
            var (r, g, b) = ColorRgb[Math.Clamp(color, 0, ColorRgb.Length - 1)];
            double k = Math.Clamp(brightness, 0, 100) / 100.0;
            return ((byte)Math.Round(r * k), (byte)Math.Round(g * k), (byte)Math.Round(b * k));
        }

        public static int StepBrightness(int value, bool up) =>
            Math.Clamp(value + (up ? BrightnessStep : -BrightnessStep), 0, 100);

        public static int StepFont(int value, bool up) => Math.Clamp(value + (up ? 1 : -1), MinFont, MaxFont);

        /// <summary>Whether a line with this many points can take another point (29 segments at most).</summary>
        public static bool CanAddPoint(int points) => points < MaxSegments + 1;

        /// <summary>
        /// Adds a typed character to text being composed: uppercase, 20 characters a line, 5 lines. A line that
        /// overflows wraps at its last space (or mid-word if it has none). Returns false if the text is full.
        /// </summary>
        public static bool Type(List<string> lines, char c)
        {
            if (lines.Count == 0) lines.Add("");
            c = char.ToUpperInvariant(c);
            if (c < ' ' || c > '~') return false;
            int last = lines.Count - 1;
            if (lines[last].Length < TextChars)
            {
                if (c == ' ' && lines[last].Length == 0) return true;   // no leading space on a line
                lines[last] += c;
                return true;
            }
            if (lines.Count >= TextLines) return false;
            if (c == ' ') { lines.Add(""); return true; }
            var full = lines[last];
            int cut = full.LastIndexOf(' ');
            if (cut > 0)
            {
                lines[last] = full.Substring(0, cut);
                lines.Add(full.Substring(cut + 1) + c);
            }
            else lines.Add(c.ToString());
            return true;
        }

        /// <summary>Backspace in text being composed; an empty last line joins the one above.</summary>
        public static void Backspace(List<string> lines)
        {
            if (lines.Count == 0) return;
            int last = lines.Count - 1;
            if (lines[last].Length > 0) lines[last] = lines[last].Substring(0, lines[last].Length - 1);
            else if (last > 0) lines.RemoveAt(last);
        }

        /// <summary>Text lines worth keeping (trailing blank lines dropped), or empty if nothing was typed.</summary>
        public static List<string> Finish(IEnumerable<string> lines)
        {
            var l = lines.Select(s => s.TrimEnd()).ToList();
            while (l.Count > 0 && l[^1].Length == 0) l.RemoveAt(l.Count - 1);
            return l;
        }

        /// <summary>A rectangle or circle box from two corners, in any drag direction.</summary>
        public static (double X, double Y, double W, double H) Box(double x0, double y0, double x1, double y1) =>
            (Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0));

        /// <summary>Repairs settings loaded from disk.</summary>
        public static void Sanitize(DrawSettings s)
        {
            s.Items ??= new List<Annotation>();
            s.Items.RemoveAll(a => a == null);
            foreach (var a in s.Items)
            {
                a.Points ??= new List<AnnPoint>();
                a.Points.RemoveAll(p => p == null);
                a.Lines ??= new List<string>();
                a.Font = Math.Clamp(a.Font, MinFont, MaxFont);
                a.Color = Math.Clamp(a.Color, 0, ColorNames.Length - 1);
            }
            s.Items.RemoveAll(a => (a.Kind == AnnotationKind.Line && a.Points.Count < 2)
                || (a.Kind == AnnotationKind.Text && a.Lines.Count == 0));
            if (s.Items.Count > Max) s.Items.RemoveRange(0, s.Items.Count - Max);
            s.Color = Math.Clamp(s.Color, 0, ColorNames.Length - 1);
            s.Brightness = Math.Clamp(s.Brightness, 0, 100);
            s.Font = Math.Clamp(s.Font, MinFont, MaxFont);
        }
    }
}
