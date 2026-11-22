using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    // Views for the rest of the VIEWS grid. CRC doesn't simulate most of these, so each one shows
    // what the companion can work out from VATSIM's live data (or what you type), in ERAM chrome.

    /// <summary>AUTO HO INHIB: flights you've marked to inhibit automatic handoff, with where they are now.</summary>
    public sealed class AutoHoInhibitView : DataListView
    {
        public AutoHoInhibitView(IViewHost h, ViewSettings s, ILiveSource l) : base(h, s, "AUTO HO INHIBIT", l) { }
        protected override string MenuTitle => "AH";

        protected override IEnumerable<MenuRow> ExtraMenuRows()
        {
            yield return EntryRow("ENTRY", "AUTO HO INHIBIT  ACID / -ACID", () => Settings.Items,
                t => EntryList.Apply(Settings.Items, t, EntryList.Callsign));
        }

        protected override List<DataLine> BuildLines()
        {
            var lines = new List<DataLine>();
            var f = Live.Facility;
            foreach (var cs in Settings.Items)
            {
                var p = Live.FindPilot(cs);
                string where = p == null ? "OFF" : f != null && f.Boundary.Contains(p.Pos) ? "IN" : "OUT";
                var text = Fit(cs, StripFormat.CallsignChars) + " " + Fit(p?.Plan?.Type ?? "", 4) + " " +
                           (p == null ? "   " : Alt((int)Math.Round(p.Altitude / 100.0))) + " " + where;
                var id = cs;
                lines.Add(new DataLine { Text = text.TrimEnd(), Key = cs, Click = _ => DeletePopup(id, () => Settings.Items.Remove(id)) });
            }
            return lines;
        }
    }

    /// <summary>CODE (Beacon Code View): codes of tracked targets in your facility, plus QB codes shown with a period.</summary>
    public sealed class CodeView : DataListView
    {
        public CodeView(IViewHost h, ViewSettings s, ILiveSource l) : base(h, s, "CODE", l) { }
        protected override string MenuTitle => "CD";
        protected override bool UseColumns => true;

        protected override IEnumerable<MenuRow> ExtraMenuRows()
        {
            yield return new ToggleRow("SORT MAN", () => Settings.SortManual, () => { Settings.SortManual = !Settings.SortManual; Changed(); });
            yield return EntryRow("QB", "QB  CODE CODE ... (ADD / REMOVE)", () => Settings.Items, ApplyQb);
        }

        /// <summary>QB adds a code that isn't listed and removes one that is.</summary>
        private string ApplyQb(string text)
        {
            var add = new List<string>(); var del = new List<string>(); var bad = new List<string>();
            foreach (var tok in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!BeaconCodes.IsCode(tok)) { bad.Add(tok); continue; }
                if (Settings.Items.Remove(tok)) del.Add(tok); else { Settings.Items.Add(tok); add.Add(tok); }
            }
            return string.Join("  ", new[] { add.Count > 0 ? "ADD " + string.Join(" ", add) : "", del.Count > 0 ? "DEL " + string.Join(" ", del) : "",
                bad.Count > 0 ? "INVALID " + string.Join(" ", bad) : "" }.Where(x => x.Length > 0));
        }

        protected override List<DataLine> BuildLines()
        {
            var t = Live.Traffic;
            var sup = Live.SuppressedGroups;
            var tracked = t?.InSector.Select(i => i.Squawk.Trim()).Where(c => !BeaconCodes.Suppressed(sup, c)) ?? Enumerable.Empty<string>();
            var entries = BeaconCodes.CodeViewEntries(tracked, Settings.Items, Settings.SortManual);
            var lines = new List<DataLine>();
            foreach (var e in entries)
            {
                var code = e.TrimEnd('.');
                bool manual = e.EndsWith(".", StringComparison.Ordinal);
                lines.Add(new DataLine { Text = e.PadRight(5), Key = code, Click = b => CodePopup(code, manual) });
            }
            if (lines.Count == 0 && t == null) lines.Add(new DataLine { Text = Live.StatusText });
            return lines;
        }

        /// <summary>Who is squawking the code, and DELETE for a QB code.</summary>
        private void CodePopup(string code, bool manual)
        {
            var rows = new List<MenuRow>();
            var who = Live.Snapshot?.Pilots.Where(p => p.Squawk.Trim() == code).Select(p => p.Callsign).OrderBy(c => c, StringComparer.Ordinal).ToList()
                      ?? new List<string>();
            foreach (var cs in who.Take(12)) rows.Add(new CommandRow(cs, null));
            if (who.Count == 0) rows.Add(new CommandRow("NO TARGET", null));
            if (manual) rows.Add(new CommandRow("DELETE", _ => { Settings.Items.Remove(code); ClosePopup(); Changed(); }));
            OpenPopup(code, rows);
        }
    }

    /// <summary>CONFLCT ALERT: pairs losing separation now or within 4 minutes.</summary>
    public sealed class ConflictAlertView : DataListView
    {
        public ConflictAlertView(IViewHost h, ViewSettings s, ILiveSource l) : base(h, s, "CONFLICT ALERT", l) { }
        protected override string MenuTitle => "CA";
        protected override int MinChars => 32;

        protected override IEnumerable<MenuRow> ExtraMenuRows()
        {
            yield return new StepRow("NM", () => Settings.SepNm, v => { Settings.SepNm = v; Live.RecomputeConflicts(); Changed(); }, Limits.SepMin, Limits.SepMax);
        }

        protected override List<DataLine> BuildLines()
        {
            var nd = NoDataLine();
            if (nd != null) return new List<DataLine> { nd };
            return Live.Conflicts.Select(c => new DataLine { Text = c.Text, Key = c.A + "|" + c.B }).ToList();
        }
    }

    /// <summary>CRR (Continuous Range Readout): groups with each aircraft's distance to the group location.</summary>
    public sealed class CrrView : DataListView
    {
        public CrrView(IViewHost h, ViewSettings s, ILiveSource l) : base(h, s, "CRR", l) { }
        protected override string MenuTitle => "CR";
        protected override int MinChars => 18;

        private const string EntryTitle = "LF  LOC [LABEL] [ACID..] / LABEL ACID / -LABEL";

        private string ApplyLf(string text) =>
            CrrCommands.Apply(Settings.Groups, text, Live.ResolveLocation, cs => Live.FindPilot(cs) != null);

        private void OpenLf() => OpenEntry(EntryTitle, () => Settings.Groups.Select(g => g.Label).ToList(), t => { var r = ApplyLf(t); Changed(); return r; });

        protected override IEnumerable<MenuRow> ExtraMenuRows()
        {
            yield return new CommandRow("LF", _ => OpenLf());
        }

        protected override List<DataLine> BuildLines()
        {
            var lines = new List<DataLine>();
            if (Settings.Groups.Count == 0) { lines.Add(new DataLine { Text = "M > LF TO ADD A GROUP", Click = _ => OpenLf() }); return lines; }
            foreach (var g in Settings.Groups)
            {
                var grp = g;
                lines.Add(new DataLine
                {
                    Text = g.Label + (g.Location != g.Label ? " " + g.Location : ""),
                    Heading = lines.Count > 0,
                    // Left: LF; middle: delete all aircraft, or the group when it's empty (as in CRC).
                    Click = b =>
                    {
                        if (b == MouseButton.Middle) GroupPopup(grp);
                        else OpenLf();
                    },
                });
                var rows = g.Aircraft.Select(cs =>
                {
                    var p = Live.FindPilot(cs);
                    double? d = p == null ? null : Geo.DistanceNm(p.Pos, g.Pos);
                    return (cs, d, p);
                }).OrderBy(x => x.d ?? double.MaxValue);
                foreach (var (cs, d, p) in rows)
                {
                    var id = cs;
                    var dist = d == null ? " --" : ((int)Math.Round(d.Value)).ToString(CultureInfo.InvariantCulture).PadLeft(3);
                    lines.Add(new DataLine
                    {
                        Text = " " + Fit(cs, StripFormat.CallsignChars) + " " + dist + (p == null ? "" : " " + Alt((int)Math.Round(p.Altitude / 100.0))),
                        Key = g.Label + "|" + cs,
                        Click = _ => DeletePopup(id, () => grp.Aircraft.Remove(id)),
                    });
                }
            }
            return lines;
        }

        private void GroupPopup(CrrGroup g)
        {
            var row = g.Aircraft.Count > 0
                ? new CommandRow("DELETE ALL", _ => { g.Aircraft.Clear(); ClosePopup(); Changed(); })
                : new CommandRow("DELETE GROUP", _ => { Settings.Groups.Remove(g); ClosePopup(); Changed(); });
            OpenPopup(g.Label, new List<MenuRow> { row });
        }
    }

    /// <summary>FLIGHT EVENT: departures, arrivals, boundary crossings, amendments, code changes and logons, newest first.</summary>
    public sealed class FlightEventView : DataListView
    {
        public FlightEventView(IViewHost h, ViewSettings s, ILiveSource l) : base(h, s, "FLIGHT EVENT", l) { }
        protected override string MenuTitle => "FE";
        protected override int MinChars => 28;

        protected override IEnumerable<MenuRow> ExtraMenuRows()
        {
            yield return new CommandRow("CLEAR", _ => { Live.Tracker.ClearEvents(); Changed(); });
        }

        protected override List<DataLine> BuildLines()
        {
            if (Live.Facility == null) return new List<DataLine> { new DataLine { Text = Live.StatusText } };
            return Live.Tracker.Events.Select((e, i) => new DataLine { Text = e.Text, Key = e.Utc.Ticks + e.Callsign + e.Type + i }).ToList();
        }
    }

    /// <summary>GROUP SUP: beacon code groups to suppress (hidden from CODE and UA), with how many targets match now.</summary>
    public sealed class GroupSupView : DataListView
    {
        public GroupSupView(IViewHost h, ViewSettings s, ILiveSource l) : base(h, s, "GROUP SUP", l) { }
        protected override string MenuTitle => "GS";
        protected override int MinChars => 16;

        protected override IEnumerable<MenuRow> ExtraMenuRows()
        {
            yield return EntryRow("ENTRY", "GROUP SUP  1200 12 4000-4077 / -12", () => Settings.Items,
                t => EntryList.Apply(Settings.Items, t, x => BeaconCodes.IsGroup(x) ? x : null));
        }

        protected override List<DataLine> BuildLines()
        {
            var f = Live.Facility;
            var inside = Live.Snapshot?.Pilots.Where(p => f != null && p.Groundspeed >= Traffic.AirborneKt && f.Boundary.Contains(p.Pos)).ToList();
            return Settings.Items.Select(g =>
            {
                var id = g;
                string label = g.Length == 2 ? g + "XX" : g;
                string n = inside == null ? "" : inside.Count(p => BeaconCodes.Matches(g, p.Squawk)).ToString(CultureInfo.InvariantCulture).PadLeft(3);
                return new DataLine { Text = Fit(label, 9) + " " + n, Key = g, Click = _ => DeletePopup(label, () => Settings.Items.Remove(id)) };
            }).ToList();
        }
    }

    /// <summary>
    /// HOLD LIST: flights detected holding (a full turn within 10 NM over 8 minutes) and holds you enter
    /// as "ACID [FIX] [EFC]". Shows altitude, fix, EFC and minutes in the hold.
    /// </summary>
    public sealed class HoldListView : DataListView
    {
        public HoldListView(IViewHost h, ViewSettings s, ILiveSource l) : base(h, s, "HOLD LIST", l) { }
        protected override string MenuTitle => "HL";
        protected override int MinChars => 30;

        protected override IEnumerable<MenuRow> ExtraMenuRows()
        {
            yield return new ToggleRow("AUTO", () => Settings.Auto, () => { Settings.Auto = !Settings.Auto; Changed(); });
            yield return EntryRow("ENTRY", "HOLD  ACID [FIX] [EFC HHMM] / -ACID", () => Settings.Items, ApplyHold);
        }

        public static (string Callsign, string Fix, string Efc)? ParseEntry(string entry)
        {
            var t = (entry ?? "").ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0 || EntryList.Callsign(t[0]) == null) return null;
            string fix = "", efc = "";
            foreach (var x in t.Skip(1))
            {
                if (x.Length == 4 && x.All(char.IsDigit) && int.Parse(x, CultureInfo.InvariantCulture) % 100 < 60 && int.Parse(x, CultureInfo.InvariantCulture) < 2400) efc = x;
                else if (x.Length <= 12 && x.All(char.IsLetterOrDigit)) fix = x;
                else return null;
            }
            return (t[0], fix, efc);
        }

        private string ApplyHold(string text)
        {
            var t = text.Trim().ToUpperInvariant();
            if (t.StartsWith("-", StringComparison.Ordinal))
            {
                var cs = t.Substring(1).Trim();
                return Settings.Items.RemoveAll(i => ParseEntry(i)?.Callsign == cs) > 0 ? "DEL " + cs : "NOT IN LIST " + cs;
            }
            var e = ParseEntry(t);
            if (e == null) return "INVALID " + t;
            Settings.Items.RemoveAll(i => ParseEntry(i)?.Callsign == e.Value.Callsign);
            Settings.Items.Add(string.Join(" ", new[] { e.Value.Callsign, e.Value.Fix, e.Value.Efc }.Where(x => x.Length > 0)));
            return "HOLD " + e.Value.Callsign;
        }

        protected override List<DataLine> BuildLines()
        {
            var now = DateTime.UtcNow;
            var manual = Settings.Items.Select(ParseEntry).Where(x => x != null).Select(x => x!.Value)
                .ToDictionary(x => x.Callsign, StringComparer.OrdinalIgnoreCase);
            var f = Live.Facility;
            var auto = Settings.Auto && f != null
                ? Live.Tracker.Holds.Where(hh => f.Boundary.Contains(hh.Center)).ToDictionary(hh => hh.Callsign, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, HoldInfo>(StringComparer.OrdinalIgnoreCase);

            var lines = new List<DataLine>();
            foreach (var cs in manual.Keys.Union(auto.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.Ordinal))
            {
                auto.TryGetValue(cs, out var hold);
                bool isManual = manual.TryGetValue(cs, out var m);
                var p = Live.FindPilot(cs);
                string fix = isManual && m.Fix.Length > 0 ? m.Fix : hold != null ? Live.NearestFix(hold.Center, 10) ?? "" : "";
                string efc = isManual && m.Efc.Length > 0 ? "EFC " + m.Efc : "        ";
                string mins = hold != null ? ((int)(now - hold.SinceUtc).TotalMinutes).ToString(CultureInfo.InvariantCulture).PadLeft(2) + "M" : "   ";
                string alt = p == null ? "OFF" : Alt((int)Math.Round(p.Altitude / 100.0));
                string text = (hold != null ? "H " : "  ") + Fit(cs, StripFormat.CallsignChars) + " " + alt + " " + Fit(fix, 5) + " " + efc + " " + mins;
                var id = cs;
                lines.Add(new DataLine
                {
                    Text = text.TrimEnd(),
                    Key = cs,
                    Click = isManual ? _ => DeletePopup(id, () => Settings.Items.RemoveAll(i => ParseEntry(i)?.Callsign == id)) : null,
                });
            }
            if (lines.Count == 0 && f == null) lines.Add(new DataLine { Text = Live.StatusText });
            return lines;
        }
    }

    /// <summary>SAA FILTER: special activity airspaces you list; click one to switch it on or off.</summary>
    public sealed class SaaFilterView : DataListView
    {
        public SaaFilterView(IViewHost h, ViewSettings s, ILiveSource l) : base(h, s, "SAA FILTER", l) { }
        protected override string MenuTitle => "SF";
        protected override int MinChars => 18;

        protected override IEnumerable<MenuRow> ExtraMenuRows()
        {
            yield return EntryRow("ENTRY", "SAA  R4001A W386 / -W386", () => Settings.Items, t =>
            {
                var r = EntryList.Apply(Settings.Items, t, EntryList.Name);
                Settings.Active.RemoveAll(a => !Settings.Items.Contains(a));
                return r;
            });
            yield return new CommandRow("ALL ON", _ => { Settings.Active = Settings.Items.ToList(); Changed(); });
            yield return new CommandRow("ALL OFF", _ => { Settings.Active.Clear(); Changed(); });
        }

        protected override List<DataLine> BuildLines() =>
            Settings.Items.Select(n =>
            {
                var id = n;
                bool on = Settings.Active.Contains(n);
                return new DataLine
                {
                    Text = Fit(n, 12) + " " + (on ? "ON" : "OFF"),
                    Click = b =>
                    {
                        if (b == MouseButton.Middle) { DeletePopup(id, () => { Settings.Items.Remove(id); Settings.Active.Remove(id); }); return; }
                        if (!Settings.Active.Remove(id)) Settings.Active.Add(id);
                        Changed();
                    },
                };
            }).ToList();
    }

}
