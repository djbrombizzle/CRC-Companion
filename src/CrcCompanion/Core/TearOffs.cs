using System.Collections.Generic;

namespace CrcCompanion.Core
{
    /// <summary>
    /// A toolbar button torn off its menu (ERAM tear-off): a free-floating copy that works like the original.
    /// <see cref="Menu"/> names the menu it came from (VIEWS, RECORD, COMMAND MENUS, DRAW), <see cref="Id"/> the button.
    /// </summary>
    public sealed class TearOffSettings
    {
        public string Menu { get; set; } = "";
        public string Id { get; set; } = "";
        public int? X { get; set; }
        public int? Y { get; set; }
    }

    public static class TearOffs
    {
        /// <summary>Drops entries loaded from disk that name no button, or that duplicate one at the same spot.</summary>
        public static void Sanitize(List<TearOffSettings> list)
        {
            list.RemoveAll(t => t == null || string.IsNullOrWhiteSpace(t.Menu) || string.IsNullOrWhiteSpace(t.Id));
            var seen = new HashSet<(string, string, int?, int?)>();
            list.RemoveAll(t => !seen.Add((t.Menu, t.Id, t.X, t.Y)));
        }
    }
}
