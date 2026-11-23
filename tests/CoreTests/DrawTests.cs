using System;
using System.Collections.Generic;
using CrcCompanion.Core;

/// <summary>Tests for DRAW annotations (ERAM manual 5.2 and 5.3): limits, text entry, colors.</summary>
internal static class DrawTests
{
    public static void Run(Action<string, bool, string> check, Action<string, object?, object?> eq)
    {
        // Brightness and font steps stay in range
        eq("brightness up", 62, Annotations.StepBrightness(60, true));
        eq("brightness capped at 100", 100, Annotations.StepBrightness(99, true));
        eq("brightness floor 0", 0, Annotations.StepBrightness(1, false));
        eq("font up", 3, Annotations.StepFont(2, true));
        eq("font capped at 8", 8, Annotations.StepFont(8, true));
        eq("font floor 1", 1, Annotations.StepFont(1, false));

        // Color at brightness
        eq("white at 60", ((byte)153, (byte)153, (byte)153), Annotations.Rgb(1, 60));
        eq("green at 100", ((byte)0, (byte)255, (byte)0), Annotations.Rgb(0, 100));
        eq("color index clamped", Annotations.Rgb(3, 50), Annotations.Rgb(9, 50));

        // Lines: up to 29 segments (30 points)
        check("29th segment allowed", Annotations.CanAddPoint(29), "29 points should take one more");
        check("30th segment refused", !Annotations.CanAddPoint(30), "30 points is 29 segments");

        // Text: 20 characters a line, 5 lines, word wrap
        var t = new List<string>();
        foreach (var c in "bull dog d moa hot") Annotations.Type(t, c);
        eq("uppercased", "BULL DOG D MOA HOT", string.Join("|", t));
        foreach (var c in "TER") Annotations.Type(t, c);
        eq("wraps at the last space", "BULL DOG D MOA|HOTTER", string.Join("|", t));
        var full = new List<string>();
        for (int i = 0; i < 100; i++) Annotations.Type(full, 'X');
        eq("5 lines of 20", "XXXXXXXXXXXXXXXXXXXX|XXXXXXXXXXXXXXXXXXXX|XXXXXXXXXXXXXXXXXXXX|XXXXXXXXXXXXXXXXXXXX|XXXXXXXXXXXXXXXXXXXX", string.Join("|", full));
        check("full text refuses more", !Annotations.Type(full, 'X'), "should refuse a 101st character");
        var bs = new List<string> { "AB", "" };
        Annotations.Backspace(bs);
        eq("backspace joins empty line", "AB", string.Join("|", bs));
        Annotations.Backspace(bs);
        eq("backspace deletes", "A", string.Join("|", bs));
        eq("finish drops trailing blanks", "HI", string.Join("|", Annotations.Finish(new[] { "HI ", "", "" })));

        // Boxes from either drag direction
        eq("box from corners", (10.0, 20.0, 30.0, 40.0), Annotations.Box(40, 60, 10, 20));

        // Origin and moving
        var line = new Annotation { Kind = AnnotationKind.Line, Points = { new AnnPoint(50, 10), new AnnPoint(20, 40) } };
        eq("line origin is its top-left", "20,10", $"{line.Origin.X},{line.Origin.Y}");
        line.Offset(5, 5);
        eq("line moved", "55,15|25,45", $"{line.Points[0].X},{line.Points[0].Y}|{line.Points[1].X},{line.Points[1].Y}");
        var copy = line.Clone();
        copy.Offset(100, 0);
        eq("clone is independent", 55.0, line.Points[0].X);

        // Sanitize
        var s = new DrawSettings { Brightness = 400, Font = 0, Color = 7 };
        s.Items.Add(new Annotation { Kind = AnnotationKind.Line, Points = { new AnnPoint(1, 1) } });
        s.Items.Add(new Annotation { Kind = AnnotationKind.Text });
        for (int i = 0; i < 210; i++) s.Items.Add(new Annotation { Kind = AnnotationKind.Rectangle, X = i, Font = 99 });
        Annotations.Sanitize(s);
        eq("sanitize caps at 200", 200, s.Items.Count);
        eq("sanitize drops oldest", 10.0, s.Items[0].X);
        eq("sanitize font", 8, s.Items[0].Font);
        eq("sanitize brightness", 100, s.Brightness);
        eq("sanitize settings font", 1, s.Font);
        eq("sanitize color", 3, s.Color);
    }
}
