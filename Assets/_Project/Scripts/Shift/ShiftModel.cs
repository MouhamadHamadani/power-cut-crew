using System;

namespace PowerCutCrew.Shift
{
    public enum ShiftState : byte { Lobby = 0, Countdown = 1, Running = 2, Summary = 3 }

    [Flags]
    public enum ShiftEvent
    {
        None = 0,
        CountdownTick = 1,   // the displayed countdown number changed
        Started = 2,         // the power cut begins
        Ended = 4,           // the timer hit 0
    }

    /// <summary>The verdict tier picked from the uptime for the summary screen.</summary>
    public enum ShiftVerdict : byte { Loved = 0, Good = 1, Meh = 2, Bad = 3 }

    /// <summary>What the summary screen shows (everyone sees the same numbers).</summary>
    public struct ShiftSummary
    {
        public float UptimePercent;
        public int Money;
        public int AngryBuildings;
        public int Overheats;
        public int Snaps;
        public int FailedPulls;
        public int BreakdownsFixed;
        public float AvgFixSeconds;
    }

    /// <summary>
    /// The shift's state machine (plain C#, unit tested): Lobby, then a 3-2-1 countdown, then Running for the shift length, then Summary.
    /// The host ticks it; clients only read what it publishes.
    /// </summary>
    public class ShiftModel
    {
        private readonly float _duration;
        private readonly float _countdownSeconds;
        private float _countdownLeft;
        private int _countdownShown;

        public ShiftModel(float durationSeconds, float countdownSeconds)
        {
            _duration = durationSeconds;
            _countdownSeconds = countdownSeconds;
            Reset();
        }

        public ShiftState State { get; private set; }
        public float TimeLeft { get; private set; }

        /// <summary>The number to show during the countdown (3, 2, 1); 0 outside it.</summary>
        public int CountdownNumber => State == ShiftState.Countdown ? _countdownShown : 0;

        public float Duration => _duration;

        /// <summary>Lobby to Countdown. False when a shift is already under way.</summary>
        public bool Start()
        {
            if (State != ShiftState.Lobby) { return false; }
            State = ShiftState.Countdown;
            _countdownLeft = _countdownSeconds;
            _countdownShown = Math.Max(1, (int)Math.Ceiling(_countdownLeft));
            return true;
        }

        /// <summary>Back to the lobby (the host pressed "Another shift"). Always allowed.</summary>
        public void Reset()
        {
            State = ShiftState.Lobby;
            TimeLeft = _duration;
            _countdownLeft = 0f;
            _countdownShown = 0;
        }

        public ShiftEvent Tick(float dt)
        {
            ShiftEvent events = ShiftEvent.None;
            if (State == ShiftState.Countdown)
            {
                _countdownLeft -= dt;
                int shown = (int)Math.Ceiling(Math.Max(0f, _countdownLeft));
                if (_countdownLeft > 0f && shown != _countdownShown)
                {
                    _countdownShown = shown;
                    events |= ShiftEvent.CountdownTick;
                }
                if (_countdownLeft <= 0f)
                {
                    State = ShiftState.Running;
                    TimeLeft = _duration;
                    events |= ShiftEvent.Started;
                }
            }
            else if (State == ShiftState.Running)
            {
                TimeLeft -= dt;
                if (TimeLeft <= 0f)
                {
                    TimeLeft = 0f;
                    State = ShiftState.Summary;
                    events |= ShiftEvent.Ended;
                }
            }
            return events;
        }

        /// <summary>Powered Requesting time over total Requesting time, as a percentage (100 when nothing requested).</summary>
        public static float Uptime(float poweredRequestingSeconds, float requestingSeconds)
        {
            return requestingSeconds <= 0f ? 100f : Math.Max(0f, Math.Min(100f, poweredRequestingSeconds / requestingSeconds * 100f));
        }

        public static ShiftVerdict Verdict(float uptimePercent, float lovedFrom, float goodFrom, float mehFrom)
        {
            if (uptimePercent >= lovedFrom) { return ShiftVerdict.Loved; }
            if (uptimePercent >= goodFrom) { return ShiftVerdict.Good; }
            if (uptimePercent >= mehFrom) { return ShiftVerdict.Meh; }
            return ShiftVerdict.Bad;
        }
    }
}
