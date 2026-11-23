using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CrcCompanion.Services
{
    /// <summary>
    /// Types command lines into CRC's ERAM MCA, the way the vUSALink App's CRC echo does (its CrcType):
    /// CRC has no command interface and only takes keys while its window has focus, so the line is typed with
    /// SendInput after CRC's ERAM window is confirmed in front.
    ///
    /// Safety, as in vUSALink: it types only into a CRC window whose title shows an ERAM display
    /// ("CRC : 1 : ERAM : ZDC"), never the Messages or Controllers windows, and only once that window is in front.
    /// The keys carry no marker, so the vUSALink app sees them like keys you typed (its MCA tracking and EDST
    /// mirroring stay right).
    /// </summary>
    public static class CrcTyper
    {
        public static string ProcessName =
            Environment.GetEnvironmentVariable("CRCCOMPANION_CRC_PROCESS") is { Length: > 0 } p ? p : "CRC";

        public static bool IsEramWindow(IntPtr h) =>
            h != IntPtr.Zero && IsWindow(h) && IsWindowVisible(h) && Title(h).Contains(": ERAM");

        /// <summary>The window in front if it's a CRC ERAM window (or a CRC popup owned by one), else the CRC ERAM window highest in the z-order.</summary>
        public static IntPtr FindTarget()
        {
            var pids = Process.GetProcessesByName(ProcessName).Select(p => (uint)p.Id).ToList();
            if (pids.Count == 0) return IntPtr.Zero;
            var fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && GetWindowThreadProcessId(fg, out uint fp) != 0 && pids.Contains(fp))
            {
                if (IsEramWindow(fg)) return fg;
                foreach (var a in new[] { GetAncestor(fg, 2), GetAncestor(fg, 3), GetWindow(fg, 4) })   // GA_ROOT, GA_ROOTOWNER, GW_OWNER
                    if (IsEramWindow(a)) return a;
            }
            IntPtr front = IntPtr.Zero, minimized = IntPtr.Zero;
            EnumWindows((h, l) =>   // top of the z-order first
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (!pids.Contains(pid) || !IsEramWindow(h)) return true;
                if (IsIconic(h)) { if (minimized == IntPtr.Zero) minimized = h; return true; }
                front = h; return false;
            }, IntPtr.Zero);
            return front != IntPtr.Zero ? front : minimized;
        }

        public static bool CrcRunning => Process.GetProcessesByName(ProcessName).Length > 0;

        public static string Title(IntPtr h)
        {
            var t = new StringBuilder(256);
            GetWindowText(h, t, 256);
            return t.ToString();
        }

        /// <summary>True when every character can be typed on this keyboard layout without Ctrl or Alt.</summary>
        public static bool CanType(string line) => line.All(c => KeyFor(c, out _, out _));

        /// <summary>
        /// Types each line into CRC's MCA, pressing Esc first (clears whatever is in the MCA, so a macro never mixes
        /// with half-typed text) and Enter after every line except, when enterLast is false, the last one (left in
        /// the MCA for you to add a flight by clicking its track). Focus stays in CRC. Returns null when typed, or why not.
        /// </summary>
        public static async Task<string?> Type(IReadOnlyList<string> lines, bool enterLast)
        {
            if (lines.Count == 0) return "NOTHING TO TYPE";
            foreach (var l in lines) if (!CanType(l)) return "CAN'T TYPE " + l;
            if (!CrcRunning) return "CRC NOT RUNNING";
            var crc = FindTarget();
            if (crc == IntPtr.Zero) return "NO ERAM WINDOW";
            if (GetForegroundWindow() != crc) BringToFront(crc);
            // Wait (up to 0.5 s) until CRC really is in front; keys sent before then would go elsewhere.
            for (int i = 0; i < 25 && GetForegroundWindow() != crc; i++) await Task.Delay(20);
            if (GetForegroundWindow() != crc || !IsEramWindow(crc)) return "COULDN'T FOCUS CRC";
            await Task.Delay(60);   // let CRC give its MCA keyboard focus

            for (int n = 0; n < lines.Count; n++)
            {
                if (GetForegroundWindow() != crc) return "CRC LOST FOCUS";
                var inputs = new List<INPUT> { Key(0x1B, false), Key(0x1B, true) };
                foreach (var ch in lines[n])
                {
                    KeyFor(ch, out ushort vk, out bool shift);
                    if (shift) inputs.Add(Key(0x10, false));
                    inputs.Add(Key(vk, false));
                    inputs.Add(Key(vk, true));
                    if (shift) inputs.Add(Key(0x10, true));
                }
                bool last = n == lines.Count - 1;
                if (!last || enterLast) { inputs.Add(Key(0x0D, false)); inputs.Add(Key(0x0D, true)); }
                uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
                if (sent != inputs.Count) return "ONLY " + sent + "/" + inputs.Count + " KEYS SENT";
                await Task.Delay(last ? 20 : 150);   // let CRC process one command before the next
            }
            return null;
        }

        private static bool KeyFor(char ch, out ushort vk, out bool shift)
        {
            vk = 0; shift = false;
            if (ch >= 'A' && ch <= 'Z' || ch >= '0' && ch <= '9') { vk = ch; return true; }
            if (ch == ' ') { vk = 0x20; return true; }
            if (char.IsLower(ch) || ch < 0x20) return false;
            short r = VkKeyScanW(ch);
            if (r == -1) return false;
            int mods = (r >> 8) & 0xFF;
            if ((mods & ~1) != 0) return false;   // needs Ctrl or Alt
            vk = (ushort)(r & 0xFF);
            shift = (mods & 1) != 0;
            return true;
        }

        // SetForegroundWindow is only allowed for the foreground process; attaching to the foreground thread's
        // input lets it hand focus on.
        private static void BringToFront(IntPtr h)
        {
            if (IsIconic(h)) ShowWindow(h, 9);   // SW_RESTORE
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            uint me = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            SetForegroundWindow(h);
            if (attached) AttachThreadInput(me, fgThread, false);
        }

        private static INPUT Key(ushort vk, bool up) => new INPUT
        {
            type = 1,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0u } },
        };

        [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion u; }
        private delegate bool EnumWindowsProc(IntPtr h, IntPtr l);

        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] private static extern short VkKeyScanW(char ch);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    }
}
