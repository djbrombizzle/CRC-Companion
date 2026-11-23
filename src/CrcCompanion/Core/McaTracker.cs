using System;
using System.Linq;

namespace CrcCompanion.Core
{
    /// <summary>
    /// Follows what you type into CRC's MCA, from the keys alone, so a macro can use a flight ID typed before it's
    /// clicked (a workaround, not ERAM behavior). It only trusts the text while every key since the MCA was last
    /// cleared (Esc or Enter) is one it understands; a click in CRC or any other key makes the content unknown.
    /// </summary>
    public sealed class McaTracker
    {
        /// <summary>A typed flight ID older than this isn't used.</summary>
        public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);
        /// <summary>CRC CIDs are 3 characters and callsigns at least 3, so shorter text is never taken as a flight ID.</summary>
        public const int MinFlid = 3, MaxFlid = 7;

        private string _text = "";
        private bool _known = true;
        private DateTime _at = DateTime.MinValue;

        public string Text => _text;
        public bool Known => _known;

        /// <summary>A key pressed while CRC's ERAM window was in front (Windows virtual-key code).</summary>
        public void Key(int vk, bool shift, bool ctrlOrAlt, DateTime now)
        {
            if (vk is 0x10 or 0x11 or 0x12 or 0x14 or (>= 0xA0 and <= 0xA5)) return;   // Shift, Ctrl, Alt, Caps Lock
            _at = now;
            if (vk == 0x1B || vk == 0x0D) { Clear(); return; }                           // Esc, Enter: MCA cleared
            if (ctrlOrAlt) { _known = false; return; }
            char? c = vk switch
            {
                >= 0x41 and <= 0x5A => (char)vk,                                         // A-Z
                >= 0x30 and <= 0x39 when !shift => (char)vk,                             // 0-9
                >= 0x60 and <= 0x69 => (char)('0' + vk - 0x60),                          // numpad 0-9
                0x20 => ' ',
                _ => null,
            };
            if (vk == 0x08) { if (_text.Length > 0) _text = _text.Substring(0, _text.Length - 1); return; }   // Backspace
            if (c == null) { _known = false; return; }
            _text += c.Value;
        }

        /// <summary>
        /// A mouse click in CRC. It may have sent the line or added a flight ID, so tracking starts over from an empty
        /// line: only what's typed after the click counts. (A macro clears the MCA with Esc before typing, so text
        /// already there before the click doesn't matter.)
        /// </summary>
        public void Clicked() => Clear();

        /// <summary>The MCA is empty (Esc or Enter, or the companion just typed and sent a line).</summary>
        public void Clear()
        {
            _text = "";
            _known = true;
        }

        /// <summary>
        /// The flight ID in the MCA: one word of 3-7 letters and digits (a CRC CID like 151, or a callsign), typed in
        /// the last minute and nothing else on the line. Null otherwise.
        /// </summary>
        public string? Flid(DateTime now)
        {
            if (!_known || now - _at > MaxAge) return null;
            var t = _text.Trim();
            if (t.Length < MinFlid || t.Length > MaxFlid || t.Any(ch => !char.IsLetterOrDigit(ch))) return null;
            return t;
        }
    }
}
