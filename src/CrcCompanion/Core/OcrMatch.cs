using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CrcCompanion.Core
{
    /// <summary>
    /// Matches a callsign the vUSALink app misread off CRC's screen (for example CIALI183 for UAL1183) to a real
    /// flight. Both sides are reduced to a "shape" form that folds together characters screen reading confuses:
    /// CI/IJ and U, I/L/1, O/D/Q/0, S/5, B/8, Z/2, G/6. Only a single clear match counts.
    /// </summary>
    public static class OcrMatch
    {
        public static string Canonical(string s)
        {
            var t = (s ?? "").Trim().ToUpperInvariant().Replace("CI", "U").Replace("IJ", "U");
            var b = new StringBuilder(t.Length);
            foreach (var c in t)
            {
                b.Append(c switch
                {
                    'I' or 'L' => '1',
                    'O' or 'D' or 'Q' => '0',
                    'S' => '5',
                    'B' => '8',
                    'Z' => '2',
                    'G' => '6',
                    _ => c,
                });
            }
            return b.ToString();
        }

        /// <summary>The one callsign in <paramref name="callsigns"/> the misread text stands for, or null if none or several.</summary>
        public static string? Find(string read, IEnumerable<string> callsigns)
        {
            var target = Canonical(read);
            if (target.Length < 3) return null;
            var matches = callsigns.Where(c => !string.IsNullOrEmpty(c) && Canonical(c) == target)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }
    }
}
