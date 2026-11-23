using System;
using System.Linq;
using CrcCompanion.Core;

/// <summary>Tests for the COMMAND MENUS RNG BRG menu (ERAM manual 13.9).</summary>
internal static class CommandMenuTests
{
    public static void Run(Action<string, bool, string> check, Action<string, object?, object?> eq)
    {
        var codes = string.Join(",", CommandMenus.RngBrg.Select(c => c.Code));
        eq("RNG BRG lists LA to LE in order", "LA,LB,LC,LD,LE", codes);
        eq("LA types its code and a space", "LA ", CommandMenus.TypedText(CommandMenus.RngBrg[0]));
        eq("LC types its code and a space", "LC ", CommandMenus.TypedText(CommandMenus.RngBrg[2]));
        eq("LD isn't typed (not in CRC)", null, CommandMenus.TypedText(CommandMenus.RngBrg[3]));
        eq("LE isn't typed (not in CRC)", null, CommandMenus.TypedText(CommandMenus.RngBrg[4]));
        check("rows fit the menu (22 chars)", CommandMenus.RngBrg.All(c => c.Label.Length <= 22), "a label is too long");
        check("every command has a prompt", CommandMenus.RngBrg.All(c => c.Prompt.Length > 0), "missing prompt");
    }
}
