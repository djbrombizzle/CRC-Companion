using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CrcCompanion.Core
{
    public sealed class FlightEvent
    {
        public DateTime Utc { get; init; }
        /// <summary>DEP, ARR, ENTR, EXIT, AMND, CODE, LGON, LGOF.</summary>
        public string Type { get; init; } = "";
        public string Callsign { get; init; } = "";
        public string Info { get; init; } = "";

        /// <summary>"1532 DEP  AAL1272  IAD".</summary>
        public string Text =>
            (Utc.ToString("HHmm", CultureInfo.InvariantCulture) + " " + Type.PadRight(4) + " " +
             Callsign.PadRight(StripFormat.CallsignChars) + " " + Info).TrimEnd();
    }

    public sealed class HoldInfo
    {
        public string Callsign { get; init; } = "";
        public LatLon Center { get; init; }
        public DateTime SinceUtc { get; init; }
        public int AltitudeHundreds { get; init; }
        public string Type { get; init; } = "";
    }

    /// <summary>
    /// Remembers each flight across VATSIM polls (every 15 s) to work out what one snapshot can't show:
    /// vertical rate, holding (a full turn while staying in a small area), and flight events
    /// (departures, arrivals, boundary crossings, amendments, code changes, logons) for your facility.
    /// </summary>
    public sealed class FlightTracker
    {
        public const int MaxEvents = 200;
        /// <summary>History kept per flight.</summary>
        public static readonly TimeSpan HistoryWindow = TimeSpan.FromMinutes(10);
        /// <summary>Holding: the turn is measured over this window.</summary>
        public static readonly TimeSpan HoldWindow = TimeSpan.FromMinutes(8);
        public const double HoldMinTurnDeg = 360;
        public const double HoldMaxRadiusNm = 10;

        private sealed class Sample
        {
            public DateTime T;
            public LatLon Pos;
            public int Alt;
            public int Gs;
            public int Hdg;
        }

        private sealed class State
        {
            public bool Inside;
            public bool Airborne;
            /// <summary>On the ground at one of your facility's airports (display id), else null.</summary>
            public string? GroundAt;
            public string Route = "";
            public int FiledAlt;
            public string Squawk = "";
            public bool Relevant;   // inside, or on the ground at one of your airports
        }

        private readonly Dictionary<string, List<Sample>> _hist = new Dictionary<string, List<Sample>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, State> _state = new Dictionary<string, State>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _holdSince = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly List<FlightEvent> _events = new List<FlightEvent>();
        private readonly Dictionary<string, HoldInfo> _holds = new Dictionary<string, HoldInfo>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastSnapshot = DateTime.MinValue;
        private string _fir = "";

        /// <summary>Newest first.</summary>
        public IReadOnlyList<FlightEvent> Events => _events;
        public IReadOnlyCollection<HoldInfo> Holds => _holds.Values;

        public void ClearEvents() => _events.Clear();

        /// <summary>Vertical rate in feet per minute from the last two samples a minute or more apart, else null.</summary>
        public double? VerticalRateFpm(string callsign)
        {
            if (!_hist.TryGetValue(callsign, out var h) || h.Count < 2) return null;
            var last = h[^1];
            for (int i = h.Count - 2; i >= 0; i--)
            {
                var dt = (last.T - h[i].T).TotalMinutes;
                if (dt >= 0.2 && (dt >= 1 || i == 0)) return (last.Alt - h[i].Alt) / dt;
            }
            return null;
        }

        public bool IsHolding(string callsign) => _holds.ContainsKey(callsign);

        public void Update(FeedSnapshot s, FacilityContext? ctx)
        {
            if (s.UpdatedUtc <= _lastSnapshot) return;   // same poll seen twice
            _lastSnapshot = s.UpdatedUtc;
            var now = s.UpdatedUtc;

            // A new facility starts fresh: no events from the switch itself.
            var fir = ctx?.Fir ?? "";
            bool baseline = fir != _fir;
            if (baseline) { _state.Clear(); _fir = fir; }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in s.Pilots)
            {
                seen.Add(p.Callsign);
                AddSample(p, now);
                UpdateHold(p, now);
                if (ctx != null) Observe(p, ctx, now, baseline);
            }

            // Logoffs, then forget flights that are gone.
            foreach (var cs in _state.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                if (_state[cs].Relevant) Add(now, "LGOF", cs, "");
                _state.Remove(cs);
            }
            foreach (var cs in _hist.Keys.Where(k => !seen.Contains(k)).ToList()) _hist.Remove(cs);
            foreach (var cs in _holds.Keys.Where(k => !seen.Contains(k)).ToList()) { _holds.Remove(cs); _holdSince.Remove(cs); }
        }

        private void AddSample(Pilot p, DateTime now)
        {
            if (!_hist.TryGetValue(p.Callsign, out var h)) _hist[p.Callsign] = h = new List<Sample>();
            h.Add(new Sample { T = now, Pos = p.Pos, Alt = p.Altitude, Gs = p.Groundspeed, Hdg = p.Heading });
            int drop = 0;
            while (drop < h.Count && now - h[drop].T > HistoryWindow) drop++;
            if (drop > 0) h.RemoveRange(0, drop);
        }

        private void UpdateHold(Pilot p, DateTime now)
        {
            var h = _hist[p.Callsign];
            bool holding = DetectHold(h.Where(x => now - x.T <= HoldWindow).ToList(), out var center);
            if (holding)
            {
                if (!_holdSince.TryGetValue(p.Callsign, out var since))
                {
                    // The hold began roughly when the window began.
                    since = h.First(x => now - x.T <= HoldWindow).T;
                    _holdSince[p.Callsign] = since;
                }
                _holds[p.Callsign] = new HoldInfo
                {
                    Callsign = p.Callsign,
                    Center = center,
                    SinceUtc = since,
                    AltitudeHundreds = (int)Math.Round(p.Altitude / 100.0),
                    Type = p.Plan?.Type ?? "",
                };
            }
            else
            {
                _holds.Remove(p.Callsign);
                _holdSince.Remove(p.Callsign);
            }
        }

        /// <summary>
        /// Holding = airborne for the whole window, at least one full turn of heading change,
        /// and every position within HoldMaxRadiusNm of the average position.
        /// </summary>
        private static bool DetectHold(List<Sample> w, out LatLon center)
        {
            center = default;
            if (w.Count < 6 || (w[^1].T - w[0].T).TotalMinutes < 3) return false;
            if (w.Any(x => x.Gs < Traffic.AirborneKt)) return false;
            double turn = 0;
            for (int i = 1; i < w.Count; i++) turn += AngleDiff(w[i - 1].Hdg, w[i].Hdg);
            if (Math.Abs(turn) < HoldMinTurnDeg) return false;
            center = new LatLon(w.Average(x => x.Pos.Lat), w.Average(x => x.Pos.Lon));
            var c = center;
            return w.All(x => Geo.DistanceNm(x.Pos, c) <= HoldMaxRadiusNm);
        }

        /// <summary>Signed smallest change from a to b, -180..180.</summary>
        public static double AngleDiff(double a, double b)
        {
            double d = ((b - a) % 360 + 540) % 360 - 180;
            return d;
        }

        private void Observe(Pilot p, FacilityContext ctx, DateTime now, bool baseline)
        {
            bool airborne = p.Groundspeed >= Traffic.AirborneKt;
            bool onGround = p.Groundspeed < Traffic.OnGroundKt;
            bool inside = ctx.Boundary.Contains(p.Pos);
            string? groundAt = null;
            if (onGround)
            {
                foreach (var a in ctx.Airports.Values)
                    if (Math.Abs(a.Pos.Lat - p.Pos.Lat) < 0.2 && Geo.DistanceNm(a.Pos, p.Pos) <= Traffic.DepartureRadiusNm)
                    { groundAt = a.Display; break; }
            }
            var st = new State
            {
                Inside = inside,
                Airborne = airborne,
                GroundAt = groundAt,
                Route = p.Plan?.Route ?? "",
                FiledAlt = p.Plan?.AltitudeHundreds ?? 0,
                Squawk = p.Squawk,
                Relevant = (inside && airborne) || groundAt != null,
            };

            if (!_state.TryGetValue(p.Callsign, out var old))
            {
                _state[p.Callsign] = st;
                if (!baseline && st.Relevant) Add(now, "LGON", p.Callsign, groundAt ?? "");
                return;
            }
            _state[p.Callsign] = st;
            if (baseline) return;

            // Departures and arrivals at your airports.
            if (old.GroundAt != null && airborne && !old.Airborne)
                Add(now, "DEP", p.Callsign, old.GroundAt + (p.Plan != null ? " " + ctx.DisplayAirport(p.Plan.Arrival) : ""));
            else if (old.Airborne && groundAt != null)
                Add(now, "ARR", p.Callsign, groundAt);

            // Boundary crossings while airborne.
            if (airborne && old.Airborne && inside != old.Inside)
                Add(now, inside ? "ENTR" : "EXIT", p.Callsign, AltText(p.Altitude));

            bool relevant = st.Relevant || old.Relevant;
            if (relevant && old.FiledAlt != st.FiledAlt && st.FiledAlt > 0)
                Add(now, "AMND", p.Callsign, "ALT " + st.FiledAlt.ToString("000", CultureInfo.InvariantCulture));
            if (relevant && old.Route.Length > 0 && Norm(old.Route) != Norm(st.Route))
                Add(now, "AMND", p.Callsign, "RTE");
            if (relevant && airborne && old.Squawk.Length > 0 && old.Squawk != st.Squawk)
                Add(now, "CODE", p.Callsign, old.Squawk + "-" + st.Squawk);
        }

        private static string AltText(int feet) => Math.Clamp((int)Math.Round(feet / 100.0), 0, 999).ToString("000", CultureInfo.InvariantCulture);

        private static string Norm(string r) => string.Join(" ", (r ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));

        private void Add(DateTime t, string type, string cs, string info)
        {
            _events.Insert(0, new FlightEvent { Utc = t, Type = type, Callsign = cs, Info = info });
            if (_events.Count > MaxEvents) _events.RemoveRange(MaxEvents, _events.Count - MaxEvents);
        }
    }
}
