using System;
using System.Collections.Generic;
using System.Linq;
using CrcCompanion.Core;

/// <summary>Tests for ERAM macros (RECORD toolbar): cleaning, labels, flight IDs and limits.</summary>
internal static class MacroTests
{
    public static void Run(Action<string, bool, string> check, Action<string, object?, object?> eq)
    {
        // Flight ID typed into CRC's MCA before clicking a macro
        var t0 = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var mca = new McaTracker();
        void TypeKeys(string s) { foreach (var ch in s) mca.Key(ch == ' ' ? 0x20 : ch, false, false, t0); }
        TypeKeys("151");
        eq("typed CID", "151", mca.Flid(t0));
        eq("typed CID still good at 59 s", "151", mca.Flid(t0.AddSeconds(59)));
        eq("typed CID too old", null, mca.Flid(t0.AddSeconds(61)));
        mca.Key(0x08, false, false, t0);
        eq("backspace leaves 15: too short", null, mca.Flid(t0));
        TypeKeys("2");
        eq("backspace then retype", "152", mca.Flid(t0));
        mca.Key(0x1B, false, false, t0);
        eq("Esc clears", null, mca.Flid(t0));
        TypeKeys("AAL123");
        eq("typed callsign", "AAL123", mca.Flid(t0));
        mca.Key(0x0D, false, false, t0);
        TypeKeys("QZ 240");
        eq("a command isn't a flight ID", null, mca.Flid(t0));
        mca.Key(0x1B, false, false, t0);
        mca.Key(0x61, false, false, t0); mca.Key(0x65, false, false, t0); mca.Key(0x61, false, false, t0);
        eq("numpad digits", "151", mca.Flid(t0));
        mca.Clicked();
        eq("a click in CRC starts a fresh line", null, mca.Flid(t0));
        TypeKeys("A2B");
        eq("CID typed after a click is used", "A2B", mca.Flid(t0));
        mca.Key(0x25, false, false, t0);
        eq("unknown key", null, mca.Flid(t0));
        mca.Clicked();
        TypeKeys("151");
        eq("a click also recovers from an unknown key", "151", mca.Flid(t0));
        mca.Key(0x1B, false, false, t0);
        mca.Key(0x31, true, false, t0);
        eq("shifted digit makes it unknown", null, mca.Flid(t0));
        mca.Clear();
        TypeKeys("40");
        eq("two characters are too short", null, mca.Flid(t0));
        mca.Clear();
        mca.Key(0x10, false, false, t0);
        TypeKeys("ABC");
        eq("Shift alone is ignored", "ABC", mca.Flid(t0));
        mca.Key(0x25, false, false, t0);
        eq("arrow key makes it unknown", null, mca.Flid(t0));

        eq("clean line", "QZ 340", Macros.CleanLine("  qz   340; "));
        eq("split on ;", "QZ 340|QP J|QP 22", string.Join("|", Macros.CleanLines(new[] { "qz 340;", "", "QP J; QP 22;" })));
        eq("label cut to 7", "ABCDEFG", Macros.CleanLabel("abcdefghij"));
        eq("default label: first six characters", "QZ340;", Macros.DefaultLabel(new[] { "QZ 340;" }));
        eq("default label across lines", "QZ340Q", Macros.DefaultLabel(new[] { "QZ 340", "QP J" }));
        eq("MSG number", "MSG 4", Macros.MsgButton(3));
        eq("MSG MAX", "MSG MAX", Macros.MsgButton(99));

        var one = new Macro { Label = "A240", Lines = { "QZ 240" } };
        var multi = new Macro { Label = "CLR1", Lines = { "QZ 340", "QP J", "QP 22" } };
        eq("single command, one flight", "QZ 240 AAL1", string.Join("|", Macros.Commands(one, new[] { "aal1" }, out var err)));
        check("no error", err == null, err ?? "");
        eq("single command, several flights", "QZ 240 AAL1 DAL2", string.Join("|", Macros.Commands(one, new[] { "AAL1", "DAL2", "AAL1" }, out err)));
        eq("several commands, one flight", "QZ 340 UAL3|QP J UAL3|QP 22 UAL3", string.Join("|", Macros.Commands(multi, new[] { "UAL3" }, out err)));
        Macros.Commands(multi, new[] { "UAL3", "AAL1" }, out err);
        eq("several commands, two flights", "TOO MANY FLIGHT IDS", err);
        Macros.Commands(one, Enumerable.Range(1, 16).Select(i => "N" + i).ToList(), out err);
        eq("more than 15 flights", "TOO MANY FLIGHT IDS", err);
        eq("no flight: commands as recorded", "QZ 240", string.Join("|", Macros.Commands(one, Array.Empty<string>(), out err)));

        var list = new List<Macro>();
        for (int i = 0; i < 100; i++) Macros.Add(list, new Macro { Label = "M" + i, Lines = { "QF" } });
        eq("at most 99, oldest dropped", "M1", list[0].Label);
        eq("99 kept", 99, list.Count);

        var r = new RecordSettings { Macros = { new Macro { Label = "", Lines = { " qz 340;" } }, new Macro { Label = "x", Lines = { " " } } } };
        Macros.Sanitize(r);
        eq("sanitize drops empty macros", 1, r.Macros.Count);
        eq("sanitize labels unlabeled", "QZ340", r.Macros[0].Label);
    }
}
