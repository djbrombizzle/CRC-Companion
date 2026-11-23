using System.Collections.Generic;

namespace CrcCompanion.Core
{
    /// <summary>Saved state of the COMMAND MENUS toolbar button.</summary>
    public sealed class CommandMenuSettings
    {
        public int? X { get; set; }
        public int? Y { get; set; }
        public bool Open { get; set; }
    }

    /// <summary>One command on an ERAM command menu.</summary>
    public sealed class MenuCommand
    {
        /// <summary>The ERAM command ("LA").</summary>
        public string Code { get; init; } = "";
        /// <summary>The menu row ("LA RANGE/BEARING").</summary>
        public string Label { get; init; } = "";
        /// <summary>Whether CRC runs this command; if not, the menu shows it dimmed.</summary>
        public bool InCrc { get; init; }
        /// <summary>What the controller does next, shown on the feedback line.</summary>
        public string Prompt { get; init; } = "";
    }

    /// <summary>
    /// ERAM command menus (ERAM manual 13.9). CRC has no COMMAND MENUS of its own, so picking a command types its
    /// code into CRC's MCA and the controller finishes the entry in CRC, as when typing it by hand.
    /// </summary>
    public static class CommandMenus
    {
        /// <summary>RNG BRG: the Range/Bearing/Fix/Time commands. CRC runs LA, LB and LC; it has no LD or LE.</summary>
        public static readonly IReadOnlyList<MenuCommand> RngBrg = new[]
        {
            new MenuCommand { Code = "LA", Label = "LA RANGE/BEARING", InCrc = true,
                Prompt = "LA: CLICK TRACK OR POINT, THEN POINT" },
            new MenuCommand { Code = "LB", Label = "LB RANGE/BEARING/FIX", InCrc = true,
                Prompt = "LB: TYPE FIX, THEN CLICK TRACK OR POINT" },
            new MenuCommand { Code = "LC", Label = "LC FIX/TIME", InCrc = true,
                Prompt = "LC: TYPE FIX/TIME, THEN CLICK TRACK" },
            new MenuCommand { Code = "LD", Label = "LD ROUTE FIX/TIME", InCrc = false,
                Prompt = "LD: NOT AVAILABLE IN CRC" },
            new MenuCommand { Code = "LE", Label = "LE ROUTE FIX/TIME/SPD", InCrc = false,
                Prompt = "LE: NOT AVAILABLE IN CRC" },
        };

        /// <summary>What to type into CRC's MCA for a command (code and a space, no Enter), or null if CRC lacks it.</summary>
        public static string? TypedText(MenuCommand c) => c.InCrc ? c.Code + " " : null;
    }
}
