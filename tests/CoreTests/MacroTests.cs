using System;
using System.Collections.Generic;
using System.Linq;
using CrcCompanion.Core;

/// <summary>Tests for ERAM macros (RECORD toolbar): cleaning, labels, flight IDs and limits.</summary>
internal static class MacroTests
{
    public static void Run(Action<string, bool, string> check, Action<string, object?, object?> eq)
    {
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
