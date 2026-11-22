using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CrcCompanion.Core;

namespace CrcCompanion.Services
{
    /// <summary>
    /// VATFLOW Airport TMU state (ground stops, restrictions, arrival rate programs) from vatflow-hub, the shared
    /// WebSocket the VATFLOW TBFM / Airport TMU page syncs through. The hub sends its whole state to every client on
    /// connect and again after every change; only signed-in TMU editors can push. This client only listens: it
    /// never sends anything. Connected only while the UA view is open; reconnects with backoff if dropped.
    /// </summary>
    public sealed class VatflowTmuService : IDisposable
    {
        public const string DefaultUrl = "wss://vatflow-hub-production.up.railway.app";
        private const int MaxMessageBytes = 8 * 1024 * 1024;

        private readonly Dispatcher _ui;
        private readonly Uri _url;
        private CancellationTokenSource? _run;
        private bool _disposed;

        /// <summary>Every active TMI in VATFLOW (not yet filtered to a facility). Null until the first state.</summary>
        public IReadOnlyList<TmiItem>? Items { get; private set; }
        public string Error { get; private set; } = "";
        public event Action? Updated;

        public VatflowTmuService(Dispatcher ui, string url)
        {
            _ui = ui;
            _url = new Uri(url);
        }

        /// <summary>UI thread: connect while active, disconnect otherwise.</summary>
        public void Configure(bool active)
        {
            if (_disposed) return;
            if (active && _run == null)
            {
                _run = new CancellationTokenSource();
                var token = _run.Token;
                _ = Task.Run(() => RunAsync(token));
            }
            else if (!active && _run != null)
            {
                _run.Cancel();
                _run = null;
            }
        }

        private async Task RunAsync(CancellationToken stop)
        {
            int retry = 0;
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var ws = new ClientWebSocket();
                    ws.Options.SetRequestHeader("User-Agent", "CRCCompanion/0.2");
                    using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop))
                    {
                        connectTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                        await ws.ConnectAsync(_url, connectTimeout.Token).ConfigureAwait(false);
                    }
                    retry = 0;
                    var buffer = new byte[64 * 1024];
                    while (ws.State == WebSocketState.Open && !stop.IsCancellationRequested)
                    {
                        using var ms = new MemoryStream();
                        WebSocketReceiveResult r;
                        do
                        {
                            r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), stop).ConfigureAwait(false);
                            if (r.MessageType == WebSocketMessageType.Close) break;
                            ms.Write(buffer, 0, r.Count);
                            if (ms.Length > MaxMessageBytes) throw new InvalidDataException("message too large");
                        } while (!r.EndOfMessage);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        if (r.MessageType != WebSocketMessageType.Text) continue;
                        List<TmiItem>? items;
                        try { items = VatflowTmu.Parse(Encoding.UTF8.GetString(ms.ToArray())); }
                        catch (Exception) { items = null; }
                        if (items != null) Report(items, "");
                    }
                    if (!stop.IsCancellationRequested)
                        try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).ConfigureAwait(false); }
                        catch (Exception) { }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
                catch (Exception)
                {
                    Report(null, "VATFLOW TMU DOWN");
                }
                if (stop.IsCancellationRequested) return;
                retry = Math.Min(retry + 1, 6);
                try { await Task.Delay(TimeSpan.FromSeconds(5 * retry), stop).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        private void Report(List<TmiItem>? items, string error)
        {
            if (_disposed) return;
            _ = _ui.BeginInvoke(() =>
            {
                if (_disposed) return;
                if (items != null) { Items = items; Error = ""; }
                else Error = error;
                Updated?.Invoke();
            });
        }

        public void Dispose()
        {
            _disposed = true;
            _run?.Cancel();
            _run = null;
        }
    }
}
