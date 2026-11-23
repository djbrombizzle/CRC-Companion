using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrcCompanion.Core
{
    /// <summary>Settings for one ERAM list view (ALTIM SET or WX). Mirrors the view's M menu.</summary>
    public sealed class ViewSettings
    {
        public bool Visible { get; set; }
        /// <summary>Screen position in physical pixels. Null = default placement.</summary>
        public int? X { get; set; }
        public int? Y { get; set; }
        public bool Opaque { get; set; } = true;
        public bool Border { get; set; } = true;
        public bool Tearoff { get; set; } = true;
        public int Lines { get; set; } = 10;
        public int Cols { get; set; } = 1;
        public int Font { get; set; } = 2;
        public int Bright { get; set; } = 80;
        public List<string> Stations { get; set; } = new List<string>();

        // Strip views (Inbound, In Sector, DEPT LIST)
        /// <summary>Route characters shown on each strip.</summary>
        public int RouteChars { get; set; } = 24;
        /// <summary>Inbound look-ahead in minutes.</summary>
        public int Minutes { get; set; } = 20;
        /// <summary>In Sector: also show aircraft working another local controller.</summary>
        public bool ShowOthers { get; set; }
        /// <summary>DEPT LIST: include prefiled flights not yet connected.</summary>
        public bool ShowPrefiles { get; set; } = true;

        /// <summary>STATUS: the text you typed, one entry per line. Null = defaults.</summary>
        public List<string>? TextLines { get; set; }

        // Views added by the companion's full VIEWS grid
        /// <summary>Typed entries (callsigns, codes, airports, SAA names, hold entries), per view.</summary>
        public List<string> Items { get; set; } = new List<string>();
        /// <summary>SAA FILTER: entries switched on.</summary>
        public List<string> Active { get; set; } = new List<string>();
        /// <summary>CRR groups.</summary>
        public List<CrrGroup> Groups { get; set; } = new List<CrrGroup>();
        /// <summary>MRP LIST meter points.</summary>
        public List<NamedPoint> Points { get; set; } = new List<NamedPoint>();
        /// <summary>MRP LIST range in NM.</summary>
        public int RangeNm { get; set; } = 250;
        /// <summary>CONFLCT ALERT lateral minimum in NM.</summary>
        public int SepNm { get; set; } = 5;
        /// <summary>INBND / DEPT sublist order: "TIME" (boundary time), "NAME" (fix name), or "" (list order).</summary>
        public string Sort { get; set; } = "";
        /// <summary>View-specific named values (MRP: sublist colors and DCT format per FCA, COLOR brightness, SPEED).</summary>
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
        /// <summary>HOLD LIST: list flights detected holding.</summary>
        public bool Auto { get; set; } = true;
        /// <summary>CODE: manual (QB) codes first.</summary>
        public bool SortManual { get; set; }
    }

    public sealed class SelectorSettings
    {
        public int? X { get; set; }
        public int? Y { get; set; }
        public bool Open { get; set; } = true;
    }

    public sealed class Profile
    {
        public string Name { get; set; } = "DEFAULT";
        /// <summary>Extra scale on top of Windows DPI scaling, to match CRC's on-screen size.</summary>
        public double UiScale { get; set; } = 1.0;
        public SelectorSettings Selector { get; set; } = new SelectorSettings();
        /// <summary>RECORD toolbar and its macros.</summary>
        public RecordSettings Record { get; set; } = new RecordSettings();
        public CommandMenuSettings Commands { get; set; } = new CommandMenuSettings();
        public ViewSettings Altim { get; set; } = new ViewSettings { Lines = 24, Cols = 1 };
        public ViewSettings Wx { get; set; } = new ViewSettings { Lines = 5, Cols = 1 };
        public ViewSettings Inbound { get; set; } = new ViewSettings { Lines = 20, Tearoff = true };
        public ViewSettings InSector { get; set; } = new ViewSettings { Lines = 30, Tearoff = false };
        public ViewSettings Dept { get; set; } = new ViewSettings { Lines = 20, Tearoff = true };
        public ViewSettings Status { get; set; } = new ViewSettings { Tearoff = false };
        /// <summary>The other VIEWS grid views, keyed by button label (e.g. "CRR").</summary>
        public Dictionary<string, ViewSettings> Extra { get; set; } = new Dictionary<string, ViewSettings>();

        /// <summary>Settings for a grid view, created on first use.</summary>
        public ViewSettings ExtraFor(string key)
        {
            if (!Extra.TryGetValue(key, out var v) || v == null)
            {
                Extra[key] = v = new ViewSettings { Tearoff = false };
                // CPDLC views: message text as wide as in ERAM (36 in HISTORY, 20 in MESSAGE OUT).
                if (key.StartsWith("CPDLC ", System.StringComparison.Ordinal)) v.RouteChars = key == "CPDLC MSGOUT" ? 20 : 36;
            }
            return v;
        }

        /// <summary>A copy with this profile's layout and settings but no station lists.</summary>
        public Profile CloneLayout(string newName)
        {
            var p = Clone(newName);
            p.Altim.Stations.Clear();
            p.Wx.Stations.Clear();
            return p;
        }

        public Profile Clone(string newName)
        {
            var json = JsonSerializer.Serialize(this, ProfileStore.JsonOptions);
            var copy = JsonSerializer.Deserialize<Profile>(json, ProfileStore.JsonOptions)!;
            copy.Name = newName;
            return copy;
        }
    }

    public sealed class AppSettings
    {
        public string ActiveProfile { get; set; } = "DEFAULT";
        /// <summary>VATSIM CID used to find the facility you're controlling.</summary>
        public string Cid { get; set; } = "";
        /// <summary>Switch to (or create) the facility's profile when you log on.</summary>
        public bool AutoSwitchProfile { get; set; } = true;
        /// <summary>Primary METAR source: "REAL" (aviationweather.gov) or "VATSIM". The other is the fallback.</summary>
        public string WeatherSource { get; set; } = WeatherSources.Real;
    }

    public static class WeatherSources
    {
        public const string Real = "REAL";
        public const string Vatsim = "VATSIM";
        public static string Normalize(string? s) => s == Vatsim ? Vatsim : Real;
    }

    /// <summary>Profiles live in %APPDATA%\CRCCompanion\profiles\NAME.json.</summary>
    public sealed class ProfileStore
    {
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        public string Root { get; }
        public string ProfilesDir => Path.Combine(Root, "profiles");
        private string SettingsPath => Path.Combine(Root, "settings.json");

        public ProfileStore(string root)
        {
            Root = root;
            Directory.CreateDirectory(ProfilesDir);
        }

        public static string DefaultRoot() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CRCCompanion");

        /// <summary>Profile names are uppercase letters, digits, '-' and '_' (max 24).</summary>
        public static string? CleanName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var n = new string(name.Trim().ToUpperInvariant()
                .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
            if (n.Length == 0) return null;
            return n.Length > 24 ? n.Substring(0, 24) : n;
        }

        private string PathFor(string name) => Path.Combine(ProfilesDir, name + ".json");

        public List<string> List() =>
            Directory.GetFiles(ProfilesDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

        public bool Exists(string name) => File.Exists(PathFor(name));

        public Profile Load(string name)
        {
            try
            {
                var p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(PathFor(name)), JsonOptions);
                if (p != null) { p.Name = name; Sanitize(p); return p; }
            }
            catch (Exception) { /* corrupt or missing: fall through to a fresh profile */ }
            return new Profile { Name = name };
        }

        public void Save(Profile p)
        {
            var path = PathFor(p.Name);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(p, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }

        public void Delete(string name)
        {
            if (Exists(name)) File.Delete(PathFor(name));
        }

        public void Rename(string oldName, string newName)
        {
            var p = Load(oldName);
            p.Name = newName;
            Save(p);
            Delete(oldName);
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
            }
            catch (Exception) { }
            return new AppSettings();
        }

        public void SaveSettings(AppSettings s) =>
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, JsonOptions));

        public static void Sanitize(Profile p)
        {
            if (p == null) return;
            p.UiScale = Math.Clamp(p.UiScale <= 0 ? 1.0 : p.UiScale, 0.5, 3.0);
            p.Selector ??= new SelectorSettings();
            p.Record ??= new RecordSettings();
            Macros.Sanitize(p.Record);
            p.Commands ??= new CommandMenuSettings();
            p.Altim ??= new ViewSettings { Lines = 24 };
            p.Wx ??= new ViewSettings { Lines = 5 };
            p.Inbound ??= new ViewSettings { Lines = 20, Tearoff = false };
            p.InSector ??= new ViewSettings { Lines = 30, Tearoff = false };
            p.Dept ??= new ViewSettings { Lines = 20, Tearoff = false };
            p.Status ??= new ViewSettings { Tearoff = false };
            p.Extra ??= new Dictionary<string, ViewSettings>();
            foreach (var k in p.Extra.Keys.Where(k => p.Extra[k] == null).ToList()) p.Extra.Remove(k);
            foreach (var v in new[] { p.Altim, p.Wx, p.Inbound, p.InSector, p.Dept, p.Status }.Concat(p.Extra.Values))
            {
                v.Items = (v.Items ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
                v.Active = (v.Active ?? new List<string>()).Distinct().ToList();
                v.Groups = (v.Groups ?? new List<CrrGroup>()).Where(g => g != null && g.Label.Length > 0).ToList();
                foreach (var g in v.Groups) g.Aircraft ??= new List<string>();
                v.Points = (v.Points ?? new List<NamedPoint>()).Where(x => x != null && x.Name.Length > 0).ToList();
                v.RangeNm = Math.Clamp(v.RangeNm, Limits.RangeMin, Limits.RangeMax);
                v.SepNm = Math.Clamp(v.SepNm, Limits.SepMin, Limits.SepMax);
                v.RouteChars = Math.Clamp(v.RouteChars, Limits.RouteMin, Limits.RouteMax);
                v.Sort ??= "";
                v.Values ??= new Dictionary<string, string>();
                v.Minutes = Math.Clamp(v.Minutes, Limits.MinutesMin, Limits.MinutesMax);
                v.Lines = Math.Clamp(v.Lines, Limits.LinesMin, Limits.LinesMax);
                v.Cols = Math.Clamp(v.Cols, Limits.ColsMin, Limits.ColsMax);
                v.Font = Math.Clamp(v.Font, Limits.FontMin, Limits.FontMax);
                v.Bright = Math.Clamp(v.Bright, Limits.BrightMin, Limits.BrightMax);
                v.Stations = (v.Stations ?? new List<string>())
                    .Select(StationIds.Normalize).Where(s => s != null).Select(s => s!)
                    .Distinct().ToList();
            }
        }
    }

    public static class Limits
    {
        public const int LinesMin = 1, LinesMax = 60;
        public const int ColsMin = 1, ColsMax = 6;
        public const int FontMin = 1, FontMax = 5;
        public const int BrightMin = 0, BrightMax = 100, BrightStep = 2;
        public const int RouteMin = 0, RouteMax = 80, RouteStep = 2;
        public const int MinutesMin = 5, MinutesMax = 60, MinutesStep = 5;
        public const int RangeMin = 50, RangeMax = 1000, RangeStep = 50;
        public const int SepMin = 3, SepMax = 10;
    }
}
