using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// Borderless, topmost, see-through window that never takes keyboard focus, so typing
    /// stays in CRC's MCA while views are clicked. Supports ERAM-style repositioning:
    /// click the reposition area, move the mouse, click again to place (Esc cancels).
    /// </summary>
    public class EramWindow : Window
    {
        protected readonly EramSurface Surface = new EramSurface();
        private readonly DispatcherTimer _moveTimer;
        private Native.POINT _grabOffset;
        private Native.RECT _origin;
        private bool _waitRelease;
        private DateTime _suppressClicksUntil = DateTime.MinValue;

        public IntPtr Hwnd { get; private set; }
        public bool IsRepositioning => _moveTimer.IsEnabled;

        /// <summary>Raised after the user places the window at a new position.</summary>
        public event Action<EramWindow>? Repositioned;

        /// <summary>Raised when a move is cancelled (Esc or right-click); the window is back where it was.</summary>
        public event Action<EramWindow>? RepositionCancelled;

        /// <summary>Raised when typing mode ends (focus left the window, or EndKeyboard was called).</summary>
        public event Action? KeyboardEnded;

        private IntPtr _returnFocus;
        public bool KeyboardActive { get; private set; }

        /// <summary>
        /// Typing mode: lets this window take keyboard focus (normally it never does), remembering
        /// which window had focus (normally CRC) to hand it back afterwards.
        /// </summary>
        public void BeginKeyboard()
        {
            if (KeyboardActive || Hwnd == IntPtr.Zero) return;
            _returnFocus = Native.GetForegroundWindow();
            Native.SetNoActivate(Hwnd, false);
            KeyboardActive = true;
            Surface.Focusable = true;
            Activate();
            Surface.Focus();
            System.Windows.Input.Keyboard.Focus(Surface);
        }

        /// <summary>Leaves typing mode and returns focus to the window that had it.</summary>
        public void EndKeyboard()
        {
            if (!KeyboardActive) return;
            KeyboardActive = false;
            Surface.Focusable = false;
            Native.SetNoActivate(Hwnd, true);
            KeyboardEnded?.Invoke();
            if (_returnFocus != IntPtr.Zero && _returnFocus != Hwnd) Native.SetForegroundWindow(_returnFocus);
            _returnFocus = IntPtr.Zero;
        }

        public EramWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Content = Surface;
            Surface.ClicksSuppressed = () => DateTime.UtcNow < _suppressClicksUntil || IsRepositioning;

            SourceInitialized += (_, _) =>
            {
                Hwnd = new WindowInteropHelper(this).Handle;
                Native.MakeNoActivate(Hwnd);
                ApplyTransform();
            };

            Deactivated += (_, _) => EndKeyboard();

            _moveTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
            _moveTimer.Tick += OnMoveTick;
        }

        private double _uiScale = 1.0;

        /// <summary>
        /// Sets the size multiplier. 1.0 = one drawing unit per screen pixel, which is how CRC
        /// draws regardless of Windows display scaling.
        /// </summary>
        public void SetScale(double scale)
        {
            _uiScale = scale;
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            double dpi = Hwnd != IntPtr.Zero ? VisualTreeHelper.GetDpi(this).DpiScaleX : 1.0;
            if (dpi <= 0) dpi = 1.0;
            double s = _uiScale / dpi;
            Surface.LayoutTransform = Math.Abs(s - 1.0) < 0.0001 ? Transform.Identity : new ScaleTransform(s, s);
            Surface.Refresh();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            ApplyTransform();
        }

        /// <summary>Shows the window at a screen position in physical pixels.</summary>
        public void ShowAt(int x, int y)
        {
            Hwnd = new WindowInteropHelper(this).EnsureHandle();
            Native.MoveWindow(Hwnd, x, y);
            Show();
            Native.MoveWindow(Hwnd, x, y);
        }

        public (int X, int Y) ScreenPosition
        {
            get
            {
                if (Hwnd == IntPtr.Zero || !Native.GetWindowRect(Hwnd, out var r)) return (0, 0);
                return (r.Left, r.Top);
            }
        }

        internal Native.RECT ScreenRect
        {
            get
            {
                Native.GetWindowRect(Hwnd, out var r);
                return r;
            }
        }

        public void MoveTo(int x, int y)
        {
            if (Hwnd != IntPtr.Zero) Native.MoveWindow(Hwnd, x, y);
        }

        public void BeginReposition()
        {
            if (IsRepositioning || Hwnd == IntPtr.Zero) return;
            Native.GetCursorPos(out var p);
            Native.GetWindowRect(Hwnd, out _origin);
            _grabOffset = new Native.POINT { X = p.X - _origin.Left, Y = p.Y - _origin.Top };
            _waitRelease = true;
            _moveTimer.Start();
        }

        private void OnMoveTick(object? sender, EventArgs e)
        {
            Native.GetCursorPos(out var p);
            Native.MoveWindow(Hwnd, p.X - _grabOffset.X, p.Y - _grabOffset.Y);

            if (Native.IsDown(Native.VK_ESCAPE) || Native.IsDown(Native.VK_RBUTTON))
            {
                Native.MoveWindow(Hwnd, _origin.Left, _origin.Top);
                EndReposition(false);
                return;
            }

            bool down = Native.IsDown(Native.VK_LBUTTON) || Native.IsDown(Native.VK_MBUTTON);
            if (_waitRelease)
            {
                if (!down) _waitRelease = false;
            }
            else if (down)
            {
                EndReposition(true);
            }
        }

        private void EndReposition(bool commit)
        {
            _moveTimer.Stop();
            // The placing click lands on this window; don't let it act as a new click.
            _suppressClicksUntil = DateTime.UtcNow.AddMilliseconds(400);
            if (commit) Repositioned?.Invoke(this);
            else RepositionCancelled?.Invoke(this);
        }
    }
}
