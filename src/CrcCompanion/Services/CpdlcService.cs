using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CrcCompanion.Core;

namespace CrcCompanion.Services
{
    /// <summary>
    /// Polls the vUSAlink hub's view-only monitor feed for one airspace every 10 s, only while a CPDLC view
    /// is open. Read-only and unauthenticated: it sends only the airspace (e.g. "KZDC") and never your CID,
    /// so it can't disturb your vUSAlink EDST session. Results are merged into <see cref="Log"/> on the UI thread.
    /// </summary>
    public sealed class CpdlcService : IDisposable
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

        private readonly Dispatcher _ui;
        private readonly HttpClient _http;
        private readonly Timer _timer;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly string _hubUrl;
        private bool _disposed;

        // Written on the UI thread, read by the poller.
        private volatile string _airspace = "";
        private volatile bool _active;

        public CpdlcLog Log { get; } = new CpdlcLog();
        public event Action? Updated;

        public CpdlcService(Dispatcher ui, string hubUrl = CpdlcHub.DefaultUrl)
        {
            _ui = ui;
            _hubUrl = hubUrl.TrimEnd('/');
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CRCCompanion/0.2");
            _timer = new Timer(_ => _ = PollAsync(), null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Call from the UI thread whenever the facility or the CPDLC views' visibility changes. Starts or stops
        /// polling; a new airspace clears the log.
        /// </summary>
        public void Configure(string? fir, bool active)
        {
            var airspace = string.IsNullOrEmpty(fir) ? "" : CpdlcHub.Airspace(fir);
            if (!string.Equals(airspace, Log.Airspace, StringComparison.OrdinalIgnoreCase)) Log.Reset(airspace);
            bool start = active && airspace.Length > 0 && (!_active || airspace != _airspace);
            bool stop = !(active && airspace.Length > 0);
            _airspace = airspace;
            _active = !stop;
            if (_disposed) return;
            if (start) _timer.Change(TimeSpan.Zero, PollInterval);
            else if (stop) _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private async Task PollAsync()
        {
            if (_disposed || !_active || !await _gate.WaitAsync(0).ConfigureAwait(false)) return;
            var airspace = _airspace;
            long since = 0;
            try
            {
                // Read on the UI thread so it matches the log being merged into.
                since = await _ui.InvokeAsync(() => Log.Airspace == airspace ? Log.LastId : 0);
                var body = JsonSerializer.Serialize(new { airspace, since });
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(_hubUrl + "/hub/monitor", content).ConfigureAwait(false);
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) { Report(airspace, null, "HUB " + (int)resp.StatusCode); return; }
                var reply = CpdlcHub.ParseMonitor(json);
                Report(airspace, reply, reply == null ? "HUB ERROR" : "");
            }
            catch (Exception)
            {
                Report(airspace, null, "HUB DOWN");
            }
            finally
            {
                _gate.Release();
            }
        }

        private void Report(string airspace, CpdlcHub.MonitorReply? reply, string error)
        {
            if (_disposed) return;
            _ = _ui.BeginInvoke(() =>
            {
                if (_disposed || Log.Airspace != airspace) return;
                if (reply != null) Log.Merge(reply, DateTime.UtcNow);
                else Log.Fail(error);
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
