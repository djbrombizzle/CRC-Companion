using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CrcCompanion.Core;
using CrcCompanion.Services;
using CrcCompanion.Tray;
using CrcCompanion.Ui;

namespace CrcCompanion
{
    /// <summary>
    /// Owns the active profile, the windows, the METAR and VATSIM data services and the tray icon.
    /// Finds the facility you're controlling from your CID and keeps the strip lists current.
    /// </summary>
    public sealed class AppController : IViewHost, ITrayHost, ILiveSource, IDisposable
    {
        private readonly Application _app;
        private readonly ProfileStore _store;
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _clockTimer;
        private readonly LiveDataService _live;
        private readonly CpdlcService _cpdlc;
        private readonly FcaService _fca;
        private readonly VatflowTmuService _tmi;
        private readonly UaTracker _tmiTracker = new UaTracker("TMU");
        private readonly UaTracker _fcaTracker = new UaTracker("FLOW");
        private string _uaFir = "";
        private UaQueue _ua = new UaQueue();
        private readonly EdstCidBook _edstCids = new EdstCidBook();
        private DateTime _edstCidsChecked = DateTime.MinValue, _edstCidsStamp = DateTime.MinValue;
        private readonly DispatcherTimer _qfTimer;
        /// <summary>QFs after this go to CFR; null while CFR is hidden.</summary>
        private DateTime? _qfSince;
        private readonly FcaWorld _fcaWorld;
        private List<FcaPilot> _fcaPilots = new List<FcaPilot>();
        private VatflowNav? _fcaNav;
        private readonly TrayIcon _tray;

        private Profile _profile = new Profile();
        private ViewsSelectorWindow? _selector;
        private MacroManager? _macros;
        private AltimViewWindow? _altim;
        private WxViewWindow? _wx;
        private StripViewWindow? _inbound;
        private StripViewWindow? _inSector;
        private StripViewWindow? _dept;
        private StatusViewWindow? _status;
        /// <summary>The other VIEWS grid views, by button label.</summary>
        private readonly Dictionary<string, ListViewWindow> _grid = new Dictionary<string, ListViewWindow>();

        // ILiveSource
        public FeedSnapshot? Snapshot { get; private set; }
        public FlightTracker Tracker { get; } = new FlightTracker();
        private List<ConflictPair> _conflicts = new List<ConflictPair>();
        public IReadOnlyList<ConflictPair> Conflicts => _conflicts;
        private Dictionary<string, Pilot> _pilots = new Dictionary<string, Pilot>(StringComparer.OrdinalIgnoreCase);
        private NavDatabase? _nav;

        /// <summary>VIEWS grid buttons with a companion view.</summary>
        public static readonly string[] GridKeys =
        {
            "AUTO HO INHIB", "CFR", "CODE", "CONFLCT ALERT", "CRR", "FLIGHT EVENT", "GROUP SUP",
            "HOLD LIST", "MRP LIST", "SAA FILTER", "UA",
            "CPDLC ADV", "CPDLC HIST", "CPDLC MSGOUT", "CPDLC TOC SET",
        };

        private static bool IsCpdlcKey(string key) => key.StartsWith("CPDLC ", StringComparison.Ordinal);

        public CpdlcLog Cpdlc => _cpdlc.Log;

        // UA (Update Area): GI from the vUSAlink EDST (hub); Airport TMU and FCA programs from VATFLOW.
        public UaQueue UpdateArea => _ua;
        public string UaStatus
        {
            get
            {
                var parts = new List<string>();
                if (Facility == null) return "NO FACILITY";
                if (Cpdlc.Error.Length > 0) parts.Add(Cpdlc.Error);
                else if (Cpdlc.LastPollUtc != null && !Cpdlc.GiSupported) parts.Add("NO GI FROM HUB");
                if (_tmi.Error.Length > 0) parts.Add(_tmi.Error);
                if (_fca.Error.Length > 0) parts.Add(_fca.Error);
                return string.Join("  ", parts);
            }
        }

        /// <summary>
        /// The CRC CID (and owning sector) the vUSALink EDST app learned for a callsign from a QF readout, or null.
        /// Reads %LOCALAPPDATA%\vUSALinkApp\cids.json when it changes (checked at most every 5 s).
        /// </summary>
        public EdstCidBook.Entry? EdstCid(string callsign)
        {
            if (DateTime.UtcNow - _edstCidsChecked > TimeSpan.FromSeconds(5)) ReloadEdstCids();
            return _edstCids.Find(callsign);
        }

        /// <summary>The aircraft the vUSALink EDST app learned this CRC CID for, or null.</summary>
        public EdstCidBook.Entry? EdstCidByCid(string cid)
        {
            if (DateTime.UtcNow - _edstCidsChecked > TimeSpan.FromSeconds(5)) ReloadEdstCids();
            return _edstCids.FindByCid(cid);
        }

        /// <summary>Re-reads the vUSALink app's cids.json if it changed. A half-written file is retried next time.</summary>
        private void ReloadEdstCids()
        {
            _edstCidsChecked = DateTime.UtcNow;
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vUSALinkApp", "cids.json");
                var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (stamp == _edstCidsStamp) return;
                _edstCids.Replace(stamp == DateTime.MinValue ? new Dictionary<string, EdstCidBook.Entry>() : EdstCidBook.Parse(File.ReadAllText(path)));
                _edstCidsStamp = stamp;
            }
            catch (Exception) { /* being written: try again next tick */ }
        }

        /// <summary>
        /// While CFR is open, every aircraft QF'd in CRC (typed, or a middle-click on its data block) is added to it,
        /// as in ERAM. The vUSALink App or Lite reads CRC's QF readouts and renews the aircraft's time in cids.json;
        /// this checks the file every second and adds what's newer than the last one seen.
        /// </summary>
        private void CheckQfs()
        {
            if (_macros?.WantsQfs == true)
            {
                ReloadEdstCids();
                _macros.CheckQfs(_edstCids);
            }
            if (!(_grid.TryGetValue("CFR", out var v) && v.Settings.Visible && v is CfrView cfr)) { _qfSince = null; return; }
            ReloadEdstCids();
            if (_qfSince == null) { _qfSince = DateTime.UtcNow; return; }   // only QFs from now on
            var fresh = _edstCids.QfSince(_qfSince.Value);
            if (fresh.Count == 0) return;
            _qfSince = fresh[^1].At;
            cfr.Add(string.Join(" ", fresh.Select(e => e.Callsign)));
        }

        /// <summary>Middle-click in INBND / DEPT / IN SECTOR: the flight goes to CFR, which opens if hidden.</summary>
        public void AddToCfr(string callsign)
        {
            if (_macros?.TryApply(callsign) == true) return;   // a macro waiting for a flight takes it
            if (!(_grid.TryGetValue("CFR", out var v) && v is CfrView cfr)) return;
            if (!v.Settings.Visible) SetViewVisible(v, true);
            cfr.Add(callsign);
        }

        // MRP (VATFLOW FCAs)
        public IReadOnlyList<FcaProgram> FcaPrograms => _fca.Programs;
        public string FcaArtcc => _fca.Artcc;
        public string FcaStatus => _fca.Error.Length > 0 ? _fca.Error : _fca.LastLoadUtc == null ? "LOADING VATFLOW" : "";
        public int MrpVersion { get; private set; }

        public FcaSequence Sequence(FcaProgram fca, IReadOnlyList<string>? localOrder) =>
            FcaEngine.Compute(fca, _fcaPilots, _fcaWorld, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), localOrder);

        /// <summary>Airports, fixes and ARTCC boundaries for the FCA engine.</summary>
        private sealed class FcaWorld : IFcaWorld
        {
            private readonly AppController _c;
            public FcaWorld(AppController c) { _c = c; }

            public VatflowNav? Nav => _c._fcaNav;

            /// <summary>Exact ICAO lookup, like VATFLOW's airport table (VATSpy airports; CRC's nav data for 4-letter codes it lacks).</summary>
            public LatLon? Airport(string icao)
            {
                icao = (icao ?? "").Trim().ToUpperInvariant();
                if (icao.Length < 3) return null;
                var a = _c._live?.Map?.Airport(icao);
                if (a != null) return a.Pos;
                return icao.Length == 4 && _c._nav != null && _c._nav.IsAirport(icao) ? _c._nav.Find(icao, _c.ReferencePoint) : null;
            }

            public LatLon? Fix(string name, LatLon near) => _c._nav?.Find(name, near);

            public bool? InArtcc(string artcc, LatLon p)
            {
                var map = _c._live?.Map;
                if (map == null) return null;
                var id = (artcc ?? "").Trim().ToUpperInvariant();
                var b = map.BoundaryForFir(id) ?? (id.Length == 3 ? map.BoundaryForFir("K" + id) : null);
                return b?.Contains(p);
            }
        }

        /// <summary>Callsign seen on the last poll, to auto-switch only when you log on or change position.</summary>
        private string _lastSessionCallsign = "";

        public MetarService Metar { get; }
        public double UiScale => _profile.UiScale;
        public string ActiveProfile => _profile.Name;

        // IStripSource
        public TrafficResult? Traffic { get; private set; }
        public FacilityContext? Facility { get; private set; }
        public bool Connected { get; private set; }
        /// <summary>Logged on, but not working a position with a frequency (e.g. an observer).</summary>
        public bool Observing { get; private set; }
        public string StatusText { get; private set; } = "LOADING";

        public AppController(Application app)
        {
            _app = app;
            _store = new ProfileStore(ProfileStore.DefaultRoot());
            _settings = _store.LoadSettings();

            Metar = new MetarService(app.Dispatcher, _settings.WeatherSource);
            Metar.Updated += RefreshViews;

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };

            // Re-evaluate stale/expired marks as time passes.
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _clockTimer.Tick += (_, _) => { RefreshViews(); RefreshGrid(); };
            _clockTimer.Start();

            // QF readouts from CRC (through the vUSALink app) go to CFR while it's open.
            _qfTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _qfTimer.Tick += (_, _) => CheckQfs();
            _qfTimer.Start();

            // vUSAlink hub (CPDLC views): read-only, polled only while a CPDLC view is open.
            // CRCCOMPANION_HUB_URL points at another hub (e.g. a local test hub); default is the shared KUSA hub.
            var hubUrl = Environment.GetEnvironmentVariable("CRCCOMPANION_HUB_URL");
            _cpdlc = new CpdlcService(app.Dispatcher, string.IsNullOrWhiteSpace(hubUrl) ? CpdlcHub.DefaultUrl : hubUrl);
            _cpdlc.Updated += () => { FeedUa(); RefreshCpdlc(); };
            // VATFLOW Airport TMU (ground stops, restrictions, arrival rates): read-only, only while UA is open.
            // CRCCOMPANION_VATFLOW_HUB_URL points at another vatflow-hub (e.g. a local test hub).
            var vfHub = Environment.GetEnvironmentVariable("CRCCOMPANION_VATFLOW_HUB_URL");
            _tmi = new VatflowTmuService(app.Dispatcher, string.IsNullOrWhiteSpace(vfHub) ? VatflowTmuService.DefaultUrl : vfHub);
            _tmi.Updated += () => { FeedUa(); RefreshUa(); };

            // VATFLOW FCA programs (MRP view): read-only, polled only while the MRP view is open.
            _fca = new FcaService(app.Dispatcher, Path.Combine(ProfileStore.DefaultRoot(), "cache"));
            _fca.Updated += () => { _fcaNav = _fca.Nav; MrpVersion++; FeedUa(); RefreshGrid(); };
            _fcaWorld = new FcaWorld(this);

            if (_store.List().Count == 0) _store.Save(new Profile { Name = "DEFAULT" });
            var start = _store.Exists(_settings.ActiveProfile) ? _settings.ActiveProfile : _store.List()[0];
            LoadProfile(start);

            _live = new LiveDataService(app.Dispatcher, ProfileStore.DefaultRoot());
            _live.MapReady += OnLiveData;
            _live.FeedUpdated += OnLiveData;

            _tray = new TrayIcon(this);

            // CRC's own nav data (fixes for CRR, MRP LIST and HOLD LIST), loaded off the UI thread.
            System.Threading.Tasks.Task.Run(() =>
            {
                var nav = NavDatabase.TryLoad(NavDatabase.DefaultPath());
                if (nav != null) _ = app.Dispatcher.BeginInvoke(() => { _nav = nav; RefreshGrid(); });
            });
        }

        // ---------------- Profiles ----------------

        private static System.Drawing.Rectangle WorkArea =>
            System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

        private void LoadProfile(string name)
        {
            CloseWindows();
            _profile = _store.Load(name);
            LoadUa();
            _settings.ActiveProfile = name;
            _store.SaveSettings(_settings);

            var wa = WorkArea;
            _selector = new ViewsSelectorWindow(_profile.Selector, IsViewOn, ToggleView, key => ViewFor(key) != null);
            _selector.Changed += MarkDirty;
            _selector.SetScale(UiScale);
            var (sx, sy) = Place(_profile.Selector.X, _profile.Selector.Y, wa.Left + 40, wa.Top + 40);
            _selector.ShowAt(sx, sy);

            _macros = new MacroManager(_profile.Record, () => UiScale, MarkDirty);
            var (rx, ry) = Place(_profile.Record.X, _profile.Record.Y, wa.Left + 40, wa.Top + 40 + (int)(130 * UiScale));
            _macros.Show(rx, ry, Place);

            _altim = new AltimViewWindow(this, _profile.Altim);
            _wx = new WxViewWindow(this, _profile.Wx);
            _inbound = new StripViewWindow(this, _profile.Inbound, StripKind.Inbound, this);
            _inSector = new StripViewWindow(this, _profile.InSector, StripKind.InSector, this);
            _dept = new StripViewWindow(this, _profile.Dept, StripKind.Departures, this);
            _status = new StatusViewWindow(this, _profile.Status);
            foreach (var key in GridKeys) _grid[key] = CreateGridView(key, _profile.ExtraFor(key));
            foreach (var v in Views) { v.SetScale(UiScale); if (v.Settings.Visible) ShowView(v); }

            UpdateMetarStations();
            Recompute();
            _tray?.UpdateText();
        }

        private IEnumerable<ListViewWindow> Views =>
            new ListViewWindow?[] { _altim, _wx, _inbound, _inSector, _dept, _status }.Where(v => v != null).Select(v => v!).Concat(_grid.Values);

        private ListViewWindow CreateGridView(string key, ViewSettings s) => key switch
        {
            "AUTO HO INHIB" => new AutoHoInhibitView(this, s, this),
            "CFR" => new CfrView(this, s, this),
            "CODE" => new CodeView(this, s, this),
            "CONFLCT ALERT" => new ConflictAlertView(this, s, this),
            "CRR" => new CrrView(this, s, this),
            "FLIGHT EVENT" => new FlightEventView(this, s, this),
            "GROUP SUP" => new GroupSupView(this, s, this),
            "HOLD LIST" => new HoldListView(this, s, this),
            "MRP LIST" => new MrpView(this, s, this),
            "SAA FILTER" => new SaaFilterView(this, s, this),
            "UA" => new UpdateAreaView(this, s, this),
            "CPDLC ADV" => new CpdlcAdvView(this, s, this),
            "CPDLC HIST" => new CpdlcHistView(this, s, this),
            "CPDLC MSGOUT" => new CpdlcMsgoutView(this, s, this),
            "CPDLC TOC SET" => new CpdlcTocView(this, s, this),
            _ => throw new ArgumentException(key),
        };

        private static (int X, int Y) Place(int? x, int? y, int defX, int defY)
        {
            if (x == null || y == null) return (defX, defY);
            var pt = new System.Drawing.Point(x.Value + 10, y.Value + 10);
            bool onScreen = System.Windows.Forms.Screen.AllScreens.Any(s => s.Bounds.Contains(pt));
            return onScreen ? (x.Value, y.Value) : (defX, defY);
        }

        private (int X, int Y) DefaultPosition(ListViewWindow v)
        {
            var wa = WorkArea;
            if (v == _altim) return (wa.Left + 40, wa.Top + 160);
            if (v == _wx) return (wa.Left + 360, wa.Top + 160);
            if (v == _inbound) return (wa.Left + 760, wa.Top + 160);
            if (v == _inSector) return (wa.Left + 760, wa.Top + 520);
            if (v == _status) return (wa.Right - 360, wa.Top + 160);
            int gi = Array.FindIndex(GridKeys, k => _grid.TryGetValue(k, out var g) && g == v);
            if (gi >= 0) return (wa.Left + 120 + (gi % 4) * 380, wa.Top + 220 + (gi / 4) * 260);
            return (wa.Left + 40, wa.Top + 520);
        }

        private void ShowView(ListViewWindow v)
        {
            if (v.IsVisible) return;
            var (defX, defY) = DefaultPosition(v);
            var (x, y) = Place(v.Settings.X, v.Settings.Y, defX, defY);
            v.ShowAt(x, y);
            if (v.Settings.X == null) v.SavePosition();
        }

        private void CloseWindows()
        {
            foreach (var v in Views) v.CloseChildren();
            _macros?.Close();
            _macros = null;
            foreach (var w in new Window?[] { _altim, _wx, _inbound, _inSector, _dept, _status, _selector }.Concat(_grid.Values)) w?.Close();
            _grid.Clear();
            _altim = null; _wx = null; _inbound = null; _inSector = null; _dept = null; _status = null; _selector = null;
        }

        public IReadOnlyList<string> Profiles() => _store.List();

        public void SwitchProfile(string name)
        {
            if (name == _profile.Name) return;
            SaveNow();
            LoadProfile(name);
        }

        public void NewProfile()
        {
            var name = AskName("New profile", "Facility name (e.g. ZBW):", "");
            if (name == null) return;
            SaveNow();
            _store.Save(new Profile { Name = name, UiScale = _profile.UiScale });
            LoadProfile(name);
        }

        public void DuplicateProfile()
        {
            var name = AskName("Duplicate profile", $"Copy {_profile.Name} as:", _profile.Name + "-2");
            if (name == null) return;
            SaveNow();
            _store.Save(_profile.Clone(name));
            LoadProfile(name);
        }

        public void RenameProfile()
        {
            var name = AskName("Rename profile", $"Rename {_profile.Name} to:", _profile.Name);
            if (name == null || name == _profile.Name) return;
            SaveNow();
            _store.Rename(_profile.Name, name);
            LoadProfile(name);
        }

        public void DeleteProfile()
        {
            var all = _store.List();
            if (all.Count <= 1) return;
            var r = MessageBox.Show($"Delete profile {_profile.Name}? This can't be undone.", "CRC Companion",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            var doomed = _profile.Name;
            _saveTimer.Stop();
            CloseWindows();
            _store.Delete(doomed);
            LoadProfile(_store.List()[0]);
        }

        private string? AskName(string title, string prompt, string initial)
        {
            var raw = InputDialog.Ask(title, prompt, initial);
            if (raw == null) return null;
            var name = ProfileStore.CleanName(raw);
            if (name == null)
            {
                MessageBox.Show("Use letters, numbers, '-' or '_'.", "CRC Companion");
                return null;
            }
            if (_store.Exists(name) && name != _profile.Name)
            {
                MessageBox.Show($"A profile named {name} already exists.", "CRC Companion");
                return null;
            }
            return name;
        }

        public string WeatherSource => Metar.Source;

        public void SetWeatherSource(string source)
        {
            _settings.WeatherSource = WeatherSources.Normalize(source);
            _store.SaveSettings(_settings);
            Metar.SetSource(_settings.WeatherSource);
        }

        public void SetScale(double scale)
        {
            _profile.UiScale = scale;
            _selector?.SetScale(scale);
            _macros?.SetScale(scale);
            foreach (var v in Views) v.SetScale(scale);
            MarkDirty();
        }

        public void ResetPositions()
        {
            _profile.Selector.X = _profile.Selector.Y = null;
            _profile.Record.X = _profile.Record.Y = null;
            foreach (var s in new[] { _profile.Altim, _profile.Wx, _profile.Inbound, _profile.InSector, _profile.Dept, _profile.Status }.Concat(_profile.Extra.Values))
                s.X = s.Y = null;
            SaveNow();
            LoadProfile(_profile.Name);
        }

        public void OpenProfilesFolder()
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", _store.ProfilesDir) { UseShellExecute = true }); }
            catch (Exception) { }
        }

        public void Exit()
        {
            SaveNow();
            Dispose();
            _app.Shutdown();
        }

        // ---------------- VATSIM session and traffic ----------------

        public string Cid => _settings.Cid;
        public bool AutoSwitchProfile => _settings.AutoSwitchProfile;

        public void SetCid()
        {
            var raw = InputDialog.Ask("VATSIM CID", "Your VATSIM CID (used to find the facility you're controlling):", _settings.Cid);
            if (raw == null) return;
            var cid = new string(raw.Where(char.IsDigit).ToArray());
            _settings.Cid = cid;
            _store.SaveSettings(_settings);
            _lastSessionCallsign = "";
            _ = _live.RefreshNow();
        }

        public void ToggleAutoSwitch()
        {
            _settings.AutoSwitchProfile = !_settings.AutoSwitchProfile;
            _store.SaveSettings(_settings);
        }

        /// <summary>One line for the tray menu describing the session.</summary>
        public string SessionStatus
        {
            get
            {
                if (_live.Map == null) return "Loading VATSIM map data...";
                if (_settings.Cid.Length == 0) return Facility == null ? "Set your VATSIM CID" : $"Facility {ShortFir}: from profile (CID not set)";
                if (Connected && Facility != null)
                    return $"Connected: {Facility.MyCallsign} {Facility.MyFrequencyHz / 1e6:0.000} ({ShortFir})";
                if (Observing && Facility != null)
                    return $"Observing: {Facility.MyCallsign} ({ShortFir}) - showing all traffic";
                if (Facility != null) return $"Not connected - using {ShortFir} from profile";
                return _live.LastError.Length > 0 ? _live.LastError : "Not connected";
            }
        }

        private string ShortFir => Facility == null ? "" : MapData.ShortFacility(Facility.Fir);

        private void OnLiveData()
        {
            var map = _live.Map;
            var snap = _live.Latest;
            if (map != null && snap != null && int.TryParse(_settings.Cid, out var cid) && cid > 0)
            {
                var me = snap.Controllers.FirstOrDefault(c => c.Cid == cid);
                var callsign = me?.Callsign ?? "";
                if (callsign != _lastSessionCallsign)
                {
                    _lastSessionCallsign = callsign;
                    var fir = me == null ? null : map.FirForCallsign(me.Callsign);
                    if (fir != null && _settings.AutoSwitchProfile) AutoSwitchTo(fir);
                }
            }
            Recompute();
        }

        /// <summary>Switch to the facility's profile, creating it from the current layout (without stations) if needed.</summary>
        private void AutoSwitchTo(string fir)
        {
            var name = MapData.ShortFacility(fir);
            var current = _live.Map?.FirForFacility(_profile.Name);
            if (string.Equals(current, fir, StringComparison.OrdinalIgnoreCase)) return;
            SaveNow();
            if (!_store.Exists(name)) _store.Save(_profile.CloneLayout(name));
            LoadProfile(name);
        }

        public void Recompute()
        {
            var map = _live?.Map;
            var snap = _live?.Latest;
            Snapshot = snap;
            if (snap != null)
            {
                _pilots = new Dictionary<string, Pilot>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in snap.Pilots) _pilots[p.Callsign] = p;
                _fcaPilots = snap.Pilots.Where(p => p.Plan != null).Select(p => new FcaPilot
                {
                    Callsign = p.Callsign, Pos = p.Pos, Alt = p.Altitude, Gs = p.Groundspeed, Hdg = p.Heading,
                    Dep = p.Plan!.Departure, Arr = p.Plan.Arrival, Route = p.Plan.Route,
                    FpAlt = p.Plan.AltitudeHundreds * 100, Tas = p.Plan.CruiseTas, DepTime = p.Plan.DepTime,
                }).ToList();
            }
            MrpVersion++;
            Traffic = null;
            Facility = null;
            Connected = false;
            Observing = false;

            if (map == null) { StatusText = "LOADING MAP DATA"; RefreshStrips(); return; }

            Controller? me = null;
            if (snap != null && int.TryParse(_settings.Cid, out var cid) && cid > 0)
                me = snap.Controllers.FirstOrDefault(c => c.Cid == cid);   // includes observers

            // Facility: the position you're logged on as, else the profile name (e.g. "ZDC").
            var fir = me != null ? map.FirForCallsign(me.Callsign) : null;
            fir ??= map.FirForFacility(_profile.Name);
            var boundary = fir == null ? null : map.BoundaryForFir(fir);
            if (fir == null || boundary == null)
            {
                StatusText = me != null ? "UNKNOWN FACILITY " + me.Callsign : "NO FACILITY";
                RefreshStrips();
                return;
            }

            // Connected = working a position with a frequency. Observers (facility 0, 199.998) aren't.
            Connected = me != null && me.Facility > VatsimFeed.FacObs && me.FrequencyHz > 0 &&
                        string.Equals(map.FirForCallsign(me.Callsign), fir, StringComparison.OrdinalIgnoreCase);
            Observing = me != null && !Connected;
            Facility = new FacilityContext
            {
                Fir = fir,
                Boundary = boundary,
                Airports = map.AirportsInFir(fir).ToDictionary(a => a.Icao, StringComparer.OrdinalIgnoreCase),
                // Always exclude your own logon from "other controllers".
                MyCallsign = me?.Callsign ?? "",
                MyFrequencyHz = Connected ? me!.FrequencyHz : 0,
                FirForCallsign = map.FirForCallsign,
            };

            if (snap == null) { StatusText = "WAITING FOR VATSIM DATA"; RefreshStrips(); return; }
            Traffic = Core.Traffic.Compute(snap, Facility, _profile.Inbound.Minutes, DateTime.UtcNow);
            Tracker.Update(snap, Facility);   // ignores a poll it has already seen
            ComputeConflicts();
            StatusText = "";
            RefreshStrips();
            _tray?.UpdateText();
        }

        private void RefreshStrips()
        {
            UpdateCpdlc();
            _inbound?.Refresh();
            _inSector?.Refresh();
            _dept?.Refresh();
            RefreshGrid();
        }

        /// <summary>Points the hub poller at your ARTCC, and runs it only while a CPDLC view is shown.</summary>
        private void UpdateCpdlc()
        {
            bool ua = _grid.TryGetValue("UA", out var uav) && uav.Settings.Visible;
            bool shown = ua || _grid.Any(kv => IsCpdlcKey(kv.Key) && kv.Value.Settings.Visible);
            _cpdlc?.Configure(Facility?.Fir, shown);
            _fca?.Configure(Facility?.Fir, ua || (_grid.TryGetValue("MRP LIST", out var mrp) && mrp.Settings.Visible));
            _tmi?.Configure(ua && Facility != null);
        }

        // ---------- UA ----------

        private const string UaAckKey = "ack";

        private void LoadUa()
        {
            var v = _profile.ExtraFor("UA").Values;
            _ua = new UaQueue(v.TryGetValue(UaAckKey, out var s) ? s.Split('|', StringSplitOptions.RemoveEmptyEntries) : null);
            _ua.Changed += () =>
            {
                _profile.ExtraFor("UA").Values[UaAckKey] = string.Join("|", _ua.AckedKeys);
                MarkDirty();
                RefreshUa();
            };
            _tmiTracker.Reset();
            _fcaTracker.Reset();
        }

        /// <summary>Turns new GI notes, VATFLOW TMU changes and FCA changes into UA messages.</summary>
        private void FeedUa()
        {
            if (_cpdlc == null || _tmi == null || _fca == null) return;
            var fir = Facility?.Fir ?? "";
            if (fir != _uaFir) { _uaFir = fir; _tmiTracker.Reset(); _fcaTracker.Reset(); }
            var now = DateTime.UtcNow;

            var gi = Cpdlc.TakeNewGi(out bool backlog);
            if (gi.Count > 0) _ua.Add(gi.Select(g => g.ToUa()), silent: backlog);

            var f = Facility;
            if (f == null) return;
            var artcc = _fca.Artcc;
            if (_tmi.Items != null && artcc.Length > 0)
            {
                var mine = _tmi.Items.Where(t => VatflowTmu.Concerns(t, artcc, a => f.Airports.ContainsKey(a)));
                _ua.Add(_tmiTracker.Update(UaTracker.TmiItems(mine), now));
            }
            if (_fca.LastLoadUtc != null && artcc.Length > 0)
                _ua.Add(_fcaTracker.Update(UaTracker.FcaItems(_fca.Programs), now));
        }

        private void RefreshUa()
        {
            if (_grid.TryGetValue("UA", out var v)) v.Refresh();
        }

        private void RefreshCpdlc()
        {
            foreach (var kv in _grid)
                if (IsCpdlcKey(kv.Key)) kv.Value.Refresh();
        }

        private void RefreshGrid()
        {
            foreach (var v in _grid.Values) v.Refresh();
        }

        private void ComputeConflicts()
        {
            if (Snapshot == null || Facility == null) { _conflicts = new List<ConflictPair>(); return; }
            _conflicts = Core.Conflicts.Find(Snapshot, Facility, Tracker.VerticalRateFpm, _profile.ExtraFor("CONFLCT ALERT").SepNm);
        }

        public void RecomputeConflicts()
        {
            ComputeConflicts();
            RefreshGrid();
        }

        public Pilot? FindPilot(string callsign) => _pilots.TryGetValue(callsign ?? "", out var p) ? p : null;

        public IReadOnlyList<string> SuppressedGroups => _profile.ExtraFor("GROUP SUP").Items;

        private LatLon ReferencePoint => Facility?.Boundary.Center ?? new LatLon(39.5, -98.35);

        public LatLon? FixPosition(string id) => LookupPoint(id);

        private LatLon? LookupPoint(string id)
        {
            var near = ReferencePoint;
            var hit = _nav?.Find(id, near);
            if (hit != null) return hit;
            var map = _live?.Map;
            var a = map?.Airport(id) ?? (id.Length == 3 ? map?.Airport("K" + id) : null);
            return a?.Pos;
        }

        public (LatLon Pos, Locations.Kind Kind, string Name)? ResolveLocation(string text) => Locations.Parse(text, LookupPoint);

        public string? NearestFix(LatLon p, double maxNm) => _nav?.NearestFix(p, maxNm)?.Id;

        public bool IsAirportId(string id)
        {
            if (_nav != null && (_nav.IsAirport(id) || (id.Length == 3 && _nav.IsAirport("K" + id)))) return true;
            var map = _live?.Map;
            return map?.Airport(id) != null || (id.Length == 3 && map?.Airport("K" + id) != null);
        }

        // ---------------- Views ----------------

        private bool IsViewOn(string key) => ViewFor(key)?.Settings.Visible ?? false;

        private ListViewWindow? ViewFor(string key) => key switch
        {
            ViewsSelectorWindow.AltimKey => _altim,
            ViewsSelectorWindow.WxKey => _wx,
            ViewsSelectorWindow.InboundKey => _inbound,
            ViewsSelectorWindow.DeptKey => _dept,
            ViewsSelectorWindow.InSectorKey => _inSector,
            ViewsSelectorWindow.StatusKey => _status,
            _ => _grid.TryGetValue(key, out var g) ? g : null,
        };

        private void ToggleView(string key)
        {
            var v = ViewFor(key);
            if (v != null) SetViewVisible(v, !v.Settings.Visible);
        }

        public void SetViewVisible(ListViewWindow view, bool visible)
        {
            view.Settings.Visible = visible;
            if (visible) ShowView(view);
            else
            {
                if (view.IsVisible) view.SavePosition();
                view.CloseChildren();
                view.Hide();
            }
            _selector?.Refresh();
            UpdateCpdlc();
            MarkDirty();
        }

        public void StationsChanged(ListViewWindow view)
        {
            UpdateMetarStations();
            view.Refresh();
            MarkDirty();
        }

        private void UpdateMetarStations() =>
            Metar.SetStations(_profile.Altim.Stations.Concat(_profile.Wx.Stations).Distinct());

        private void RefreshViews()
        {
            _altim?.Refresh();
            _wx?.Refresh();
        }

        // ---------------- Saving ----------------

        public void MarkDirty()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveNow()
        {
            _saveTimer.Stop();
            try { _store.Save(_profile); }
            catch (Exception ex) { Program.LogError(ex); }
        }

        public void Dispose()
        {
            _clockTimer.Stop();
            _qfTimer.Stop();
            _saveTimer.Stop();
            Metar.Dispose();
            _live.Dispose();
            _cpdlc.Dispose();
            _fca.Dispose();
            _tmi.Dispose();
            _tray.Dispose();
        }
    }
}
