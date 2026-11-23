using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using CrcCompanion.Core;

namespace CrcCompanion.Services
{
    /// <summary>
    /// Watches the keys you type while CRC's ERAM window is in front, so a macro can use a flight ID you typed into
    /// the MCA first (tray option "Macros: use FLID typed in CRC"). Runs only while that option is on.
    /// Keys typed into other programs are ignored, as are the companion's own (injected) keys. Nothing is saved or
    /// sent: the tracker keeps only the current MCA line, in memory.
    /// </summary>
    public sealed class McaWatcher : IDisposable
    {
        private readonly McaTracker _tracker = new McaTracker();
        private readonly HookProc _keyProc, _mouseProc;
        private IntPtr _keyHook, _mouseHook;
        private uint[] _crcPids = Array.Empty<uint>();
        private DateTime _pidsAt = DateTime.MinValue;

        public McaWatcher()
        {
            _keyProc = OnKey;
            _mouseProc = OnMouse;
            var mod = GetModuleHandle(null);
            _keyHook = SetWindowsHookEx(13, _keyProc, mod, 0);     // WH_KEYBOARD_LL
            _mouseHook = SetWindowsHookEx(14, _mouseProc, mod, 0); // WH_MOUSE_LL
        }

        public bool Active => _keyHook != IntPtr.Zero;

        /// <summary>The flight ID typed into CRC's MCA, if that's all the line holds (see <see cref="McaTracker"/>).</summary>
        public string? Flid() => _tracker.Flid(DateTime.UtcNow);

        /// <summary>The companion just typed into CRC and sent the line, so the MCA is empty.</summary>
        public void Cleared() => _tracker.Clear();

        private uint[] CrcPids()
        {
            if (DateTime.UtcNow - _pidsAt > TimeSpan.FromSeconds(5))
            {
                _crcPids = Process.GetProcessesByName(CrcTyper.ProcessName).Select(p => (uint)p.Id).ToArray();
                _pidsAt = DateTime.UtcNow;
            }
            return _crcPids;
        }

        private bool IsCrc(IntPtr h) => h != IntPtr.Zero && GetWindowThreadProcessId(h, out uint pid) != 0 && CrcPids().Contains(pid);

        /// <summary>CRC's ERAM window (or a CRC window it owns) is in front, so keys go to its MCA.</summary>
        private bool EramInFront()
        {
            var fg = GetForegroundWindow();
            if (!IsCrc(fg)) return false;
            return CrcTyper.IsEramWindow(fg) || CrcTyper.IsEramWindow(GetAncestor(fg, 3));   // GA_ROOTOWNER
        }

        private IntPtr OnKey(int code, IntPtr msg, IntPtr data)
        {
            try
            {
                int m = msg.ToInt32();
                if (code >= 0 && (m == 0x100 || m == 0x104))   // WM_KEYDOWN, WM_SYSKEYDOWN
                {
                    var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(data);
                    bool injected = (k.flags & 0x10) != 0;
                    if (!injected && EramInFront())
                    {
                        bool shift = (GetKeyState(0x10) & 0x8000) != 0;
                        bool ctrlAlt = (GetKeyState(0x11) & 0x8000) != 0 || (k.flags & 0x20) != 0;
                        _tracker.Key((int)k.vkCode, shift, ctrlAlt, DateTime.UtcNow);
                    }
                }
            }
            catch (Exception) { }
            return CallNextHookEx(IntPtr.Zero, code, msg, data);
        }

        private IntPtr OnMouse(int code, IntPtr msg, IntPtr data)
        {
            try
            {
                int m = msg.ToInt32();
                if (code >= 0 && (m == 0x201 || m == 0x204 || m == 0x207))   // left, right, middle button down
                {
                    var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(data);
                    if ((ms.flags & 0x01) == 0 && IsCrc(WindowFromPoint(ms.pt))) _tracker.Clicked();
                }
            }
            catch (Exception) { }
            return CallNextHookEx(IntPtr.Zero, code, msg, data);
        }

        public void Dispose()
        {
            if (_keyHook != IntPtr.Zero) { UnhookWindowsHookEx(_keyHook); _keyHook = IntPtr.Zero; }
            if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        }

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] private static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
    }
}
