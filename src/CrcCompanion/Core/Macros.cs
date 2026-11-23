using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CrcCompanion.Core
{
    /// <summary>A recorded ERAM macro (RECORD toolbar): up to six command lines behind a labeled button.</summary>
    public sealed class Macro
    {
        public string Label { get; set; } = "";
        public List<string> Lines { get; set; } = new List<string>();
        /// <summary>Screen position of the macro button. Null = not moved off the RECORD toolbar yet.</summary>
        public int? X { get; set; }
        public int? Y { get; set; }
    }

    /// <summary>RECORD toolbar position and state, per profile (the companion's Preference Set).</summary>
    public sealed class RecordSettings
    {
        public int? X { get; set; }
        public int? Y { get; set; }
        public bool Open { get; set; }
        public List<Macro> Macros { get; set; } = new List<Macro>();
    }

    /// <summary>ERAM macro rules (ERAM manual 7.2), kept apart from the UI so they can be tested.</summary>
    public static class Macros
    {
        /// <summary>Most macros at a position; the MSG button then reads MSG MAX.</summary>
        public const int Max = 99;
        /// <summary>Command lines when recording.</summary>
        public const int RecordLines = 6;
        /// <summary>Lines the MACRO EDIT text area grows to.</summary>
        public const int EditLines = 25;
        public const int LabelMax = 7;
        /// <summary>Most flight IDs for a single-command macro.</summary>
        public const int MaxFlights = 15;
        public const int LineMax = 60;

        /// <summary>A command line as it's stored and typed: uppercase, single spaces, no trailing ";".</summary>
        public static string CleanLine(string? s)
        {
            if (s == null) return "";
            s = Regex.Replace(s.ToUpperInvariant(), @"\s+", " ").Trim();
            while (s.EndsWith(";", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 1).TrimEnd();
            return s;
        }

        /// <summary>Non-empty command lines from typed lines; a ";" inside a line also starts a new command.</summary>
        public static List<string> CleanLines(IEnumerable<string> lines) =>
            lines.SelectMany(l => (l ?? "").Split(';')).Select(CleanLine).Where(l => l.Length > 0).ToList();

        public static string CleanLabel(string? s)
        {
            s = Regex.Replace((s ?? "").ToUpperInvariant(), @"\s+", " ").Trim();
            return s.Length > LabelMax ? s.Substring(0, LabelMax) : s;
        }

        /// <summary>With no label typed, ERAM labels the button with the first six characters of the macro.</summary>
        public static string DefaultLabel(IReadOnlyList<string> lines)
        {
            var first = string.Join(" ", lines).Replace(" ", "");
            return first.Length > 6 ? first.Substring(0, 6) : first;
        }

        /// <summary>The MSG button's text: the number of the macro about to be recorded, or MSG MAX.</summary>
        public static string MsgButton(int count) => count >= Max ? "MSG MAX" : "MSG " + (count + 1);

        /// <summary>
        /// The command lines to enter for a macro and the flights picked for it (each line gets the flight IDs at the end).
        /// A macro with several commands takes one flight; a single command takes up to 15.
        /// Error is the ERAM Feedback Area message, or null.
        /// </summary>
        public static List<string> Commands(Macro m, IReadOnlyList<string> flights, out string? error)
        {
            error = null;
            var ids = flights.Select(f => f.Trim().ToUpperInvariant()).Where(f => f.Length > 0).Distinct().ToList();
            if (m.Lines.Count == 0) { error = "EMPTY MACRO"; return new List<string>(); }
            if (ids.Count == 0) return m.Lines.ToList();
            if (m.Lines.Count > 1 && ids.Count > 1) { error = "TOO MANY FLIGHT IDS"; return new List<string>(); }
            if (ids.Count > MaxFlights) { error = "TOO MANY FLIGHT IDS"; return new List<string>(); }
            var tail = " " + string.Join(" ", ids);
            return m.Lines.Select(l => l + tail).ToList();
        }

        /// <summary>Adds a macro; past 99, the oldest that isn't in use is dropped (ERAM: excess deleted oldest first).</summary>
        public static void Add(List<Macro> list, Macro m)
        {
            list.Add(m);
            while (list.Count > Max) list.RemoveAt(0);
        }

        public static void Sanitize(RecordSettings r)
        {
            r.Macros = (r.Macros ?? new List<Macro>()).Where(m => m != null).ToList();
            foreach (var m in r.Macros)
            {
                m.Lines = CleanLines(m.Lines ?? new List<string>());
                m.Label = CleanLabel(m.Label);
                if (m.Label.Length == 0) m.Label = DefaultLabel(m.Lines);
            }
            r.Macros = r.Macros.Where(m => m.Lines.Count > 0).ToList();
            while (r.Macros.Count > Max) r.Macros.RemoveAt(0);
        }
    }
}
