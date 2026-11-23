using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    /// <summary>A menu whose buttons can be torn off (VIEWS, RECORD, COMMAND MENUS, DRAW).</summary>
    public interface ITearOffSource
    {
        /// <summary>The menu's name, saved with each tear-off.</summary>
        string Menu { get; }
        /// <summary>Set by the tear-off manager: called when a button's gold bar is clicked in the menu.</summary>
        Action<string>? TearOff { get; set; }
        bool HasTearOff(string id);
        /// <summary>
        /// Draws the button at (0, 0) with its tear-off bar, plus anything attached to it (a pop-up list), and
        /// registers its body clicks; the window adds the bar's clicks. Returns the size drawn.
        /// </summary>
        Size PaintTearOff(string id, Painter p, TearOffWindow w);
    }

    /// <summary>
    /// A torn-off toolbar button. Left-click its gold bar to move it; middle-click the bar to delete it
    /// (CRC's DELETE TEAROFF doesn't reach the companion's windows).
    /// </summary>
    public sealed class TearOffWindow : EramWindow
    {
        public TearOffSettings Item { get; }
        public ITearOffSource Source { get; }
        /// <summary>For buttons that open a list (RNG BRG): whether this copy's list is open.</summary>
        public bool Expanded { get; set; }
        public event Action<TearOffWindow>? DeleteRequested;

        public TearOffWindow(TearOffSettings item, ITearOffSource source)
        {
            Item = item;
            Source = source;
            Surface.Paint = p =>
            {
                var size = Source.PaintTearOff(Item.Id, p, this);
                p.Hit(0, 0, TbCell.BodyX, TbCell.H, b =>
                {
                    if (b == MouseButton.Middle) DeleteRequested?.Invoke(this);
                    else BeginReposition();
                });
                return size;
            };
        }

        public void Refresh() => Surface.Refresh();
    }

    /// <summary>Opens, saves and refreshes the torn-off buttons of every registered menu.</summary>
    public sealed class TearOffManager
    {
        private readonly List<TearOffSettings> _items;
        private readonly Func<double> _scale;
        private readonly Action _dirty;
        private readonly Dictionary<string, ITearOffSource> _sources = new Dictionary<string, ITearOffSource>();
        private readonly List<TearOffWindow> _windows = new List<TearOffWindow>();
        private readonly DispatcherTimer _refresh;
        private bool _tearing;

        public TearOffManager(List<TearOffSettings> items, Func<double> scale, Action dirty)
        {
            _items = items;
            _scale = scale;
            _dirty = dirty;
            // Copies follow their menu's state (a view turning on, a tool going active, a new MSG number).
            _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _refresh.Tick += (_, _) => { foreach (var w in _windows) w.Refresh(); };
            _refresh.Start();
        }

        public void Register(ITearOffSource source)
        {
            _sources[source.Menu] = source;
            source.TearOff = id => Begin(source, id);
        }

        /// <summary>Opens the saved tear-offs whose menu and button still exist.</summary>
        public void ShowSaved(Func<int?, int?, int, int, (int X, int Y)> place)
        {
            foreach (var t in _items.ToList())
            {
                if (!_sources.TryGetValue(t.Menu, out var src) || !src.HasTearOff(t.Id) || t.X == null || t.Y == null) continue;
                var (x, y) = place(t.X, t.Y, t.X.Value, t.Y.Value);
                t.X = x; t.Y = y;
                Open(t, src).ShowAt(x, y);
            }
        }

        private TearOffWindow Open(TearOffSettings t, ITearOffSource src)
        {
            var w = new TearOffWindow(t, src);
            w.SetScale(_scale());
            w.Repositioned += _ => Placed(w);
            w.RepositionCancelled += _ => { if (!_items.Contains(w.Item)) { Remove(w); } _tearing = false; };
            w.DeleteRequested += win => { Remove(win); _items.Remove(win.Item); _dirty(); };
            _windows.Add(w);
            return w;
        }

        /// <summary>Starts a tear-off: a copy appears under the mouse and follows it until clicked into place.</summary>
        private void Begin(ITearOffSource src, string id)
        {
            if (_tearing) return;
            _tearing = true;
            Native.GetCursorPos(out var c);
            double s = _scale();
            var w = Open(new TearOffSettings { Menu = src.Menu, Id = id }, src);
            w.ShowAt(c.X - (int)Math.Round(6 * s), c.Y - (int)Math.Round(TbCell.H / 2.0 * s));
            w.BeginReposition();
        }

        private void Placed(TearOffWindow w)
        {
            var (x, y) = w.ScreenPosition;
            w.Item.X = x; w.Item.Y = y;
            if (!_items.Contains(w.Item)) _items.Add(w.Item);
            _tearing = false;
            _dirty();
        }

        private void Remove(TearOffWindow w)
        {
            _windows.Remove(w);
            w.Close();
        }

        public void SetScale(double s)
        {
            foreach (var w in _windows) w.SetScale(s);
        }

        public void Close()
        {
            _refresh.Stop();
            foreach (var w in _windows.ToList()) w.Close();
            _windows.Clear();
        }
    }
}
