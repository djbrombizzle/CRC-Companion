using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CrcCompanion.Core;

namespace CrcCompanion.Services
{
    /// <summary>
    /// VATFLOW's FCA programs for one ARTCC, read from VATFLOW's Supabase table <c>fcas</c> with the public
    /// read key VATFLOW's own pages use (shared/fca-store.js). VATFLOW pages subscribe to realtime changes;
    /// the companion polls every 20 s instead, filtered server-side to the ARTCC. Read-only: nothing is written back.
    /// Polls only while the MRP view is open.
    /// </summary>
    public sealed class FcaService : IDisposable
    {
        public const string SupabaseUrl = "https://qoaipsfcidpymboojfwa.supabase.co";
        public const string AnonKey = "sb_publishable_6Pj7jeRN0AQBcjl44MoCNA_zjsvFs79";
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);

        private readonly Dispatcher _ui;
        private readonly HttpClient _http;
        private readonly Timer _timer;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private volatile string _artcc = "";
        private volatile bool _active;
        private bool _disposed;

        /// <summary>The ARTCC the programs below belong to ("ZDC").</summary>
        public string Artcc { get; private set; } = "";
        public IReadOnlyList<FcaProgram> Programs { get; private set; } = Array.Empty<FcaProgram>();
        public DateTime? LastLoadUtc { get; private set; }
        public string Error { get; private set; } = "";
        /// <summary>VATFLOW's route data (null until loaded, or if it can't be fetched and isn't cached).</summary>
        public VatflowNav? Nav { get; private set; }
        public event Action? Updated;

        private const string NavBase = "https://vatflow.io/data/nav/";
        private static readonly string[] NavFiles = { "meta", "fixes", "navaids", "airways", "procedures", "preferred" };
        private static readonly TimeSpan NavMaxAge = TimeSpan.FromDays(7);
        private readonly string _cacheDir;
        private bool _navStarted;

        public FcaService(Dispatcher ui, string cacheDir)
        {
            _ui = ui;
            _cacheDir = Path.Combine(cacheDir, "vatflow-nav");
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CRCCompanion/0.2");
            _http.DefaultRequestHeaders.Add("apikey", AnonKey);
            _http.DefaultRequestHeaders.Add("Authorization", "Bearer " + AnonKey);
            _timer = new Timer(_ => _ = PollAsync(), null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>UI thread: the facility's ARTCC ("ZDC" or "KZDC") and whether the MRP view is shown.</summary>
        public void Configure(string? fir, bool active)
        {
            var artcc = (fir ?? "").Trim().ToUpperInvariant();
            if (artcc.Length == 4 && artcc[0] == 'K' && artcc[1] == 'Z') artcc = artcc.Substring(1);
            if (artcc != Artcc)
            {
                Artcc = artcc;
                Programs = Array.Empty<FcaProgram>();
                LastLoadUtc = null;
                Error = "";
            }
            bool run = active && artcc.Length > 0;
            if (run && !_navStarted) { _navStarted = true; _ = Task.Run(LoadNavAsync); }
            bool start = run && (!_active || artcc != _artcc);
            _artcc = artcc;
            _active = run;
            if (_disposed) return;
            if (start) _timer.Change(TimeSpan.Zero, PollInterval);
            else if (!run) _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public Task RefreshNow() => PollAsync();

        /// <summary>
        /// The same route data VATFLOW expands routes with (~5 MB), cached in the app's cache\vatflow-nav folder
        /// and refreshed weekly. A stale copy is used when offline.
        /// </summary>
        private async Task LoadNavAsync()
        {
            try
            {
                Directory.CreateDirectory(_cacheDir);
                var texts = new Dictionary<string, string>();
                foreach (var name in NavFiles)
                {
                    var path = Path.Combine(_cacheDir, name + ".json");
                    bool fresh = File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < NavMaxAge;
                    if (!fresh)
                    {
                        try
                        {
                            using var plain = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                            plain.DefaultRequestHeaders.UserAgent.ParseAdd("CRCCompanion/0.2");
                            var text = await plain.GetStringAsync(NavBase + name + ".json").ConfigureAwait(false);
                            var tmp = path + ".tmp";
                            await File.WriteAllTextAsync(tmp, text).ConfigureAwait(false);
                            File.Move(tmp, path, overwrite: true);
                        }
                        catch (Exception) { /* use the stale copy below */ }
                    }
                    if (!File.Exists(path)) return;
                    texts[name] = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                }
                var nav = VatflowNav.Parse(texts["meta"], texts["fixes"], texts["navaids"], texts["airways"], texts["procedures"], texts["preferred"]);
                _ = _ui.BeginInvoke(() => { if (_disposed) return; Nav = nav; Updated?.Invoke(); });
            }
            catch (Exception ex)
            {
                Program.LogError(ex);
            }
        }

        private async Task PollAsync()
        {
            if (_disposed || !_active || !await _gate.WaitAsync(0).ConfigureAwait(false)) return;
            var artcc = _artcc;
            try
            {
                var url = SupabaseUrl + "/rest/v1/fcas?select=id,data,updated_at&data->>artcc=eq." + Uri.EscapeDataString(artcc);
                var json = await _http.GetStringAsync(url).ConfigureAwait(false);
                var list = FcaProgram.ParseRows(json);
                Report(artcc, list, "");
            }
            catch (Exception)
            {
                Report(artcc, null, "VATFLOW DOWN");
            }
            finally
            {
                _gate.Release();
            }
        }

        private void Report(string artcc, List<FcaProgram>? list, string error)
        {
            if (_disposed) return;
            _ = _ui.BeginInvoke(() =>
            {
                if (_disposed || artcc != Artcc) return;
                if (list != null) { Programs = list; LastLoadUtc = DateTime.UtcNow; Error = ""; }
                else Error = error;
                Updated?.Invoke();
            });
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
            _http.Dispose();
        }
    }
}
