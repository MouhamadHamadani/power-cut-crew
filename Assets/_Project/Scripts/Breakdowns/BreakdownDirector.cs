using System.Collections.Generic;

namespace PowerCutCrew.Breakdowns
{
    /// <summary>What the director looks at each step. The host fills it from the generator and the shift.</summary>
    public struct DirectorInputs
    {
        public bool ShiftRunning;
        public bool GeneratorRunning;
        /// <summary>Fuel in the tank, 0..1.</summary>
        public float FuelFraction;
        public float Heat;
        public float LoadAmps;
        public float RatedAmps;
        /// <summary>The breakdowns that are active right now (bit per <see cref="BreakdownType"/>).</summary>
        public byte ActiveMask;
    }

    /// <summary>
    /// Decides when the generator breaks (plain C#, host only, unit tested). Only while the shift runs and the generator runs; none in the grace period, a minimum gap
    /// between two new ones, at most <see cref="BreakdownSettings.MaxActive"/> at once and never two of a kind. Every second each type rolls its own chance, which a
    /// low tank, a hot engine or an overloaded generator multiplies. Deterministic for a seed: the same seed and the same inputs give the same breakdowns.
    /// </summary>
    public class BreakdownDirector
    {
        private readonly BreakdownSettings _s;
        private readonly int _seed;
        private System.Random _rng;
        private float _shiftSeconds;
        private float _second;
        private float _lastStart = float.NegativeInfinity;

        public BreakdownDirector(BreakdownSettings settings, int seed)
        {
            _s = settings;
            _seed = seed;
            Reset();
        }

        /// <summary>Seconds of the shift that have run (only counted while it runs).</summary>
        public float ShiftSeconds => _shiftSeconds;

        public int Started { get; private set; }

        /// <summary>A new shift: the grace period starts over and the dice start from the seed again.</summary>
        public void Reset()
        {
            _rng = new System.Random(_seed);
            _shiftSeconds = 0f;
            _second = 0f;
            _lastStart = float.NegativeInfinity;
            Started = 0;
        }

        /// <summary>The chance per second of a type under the current conditions.</summary>
        public static float Chance(BreakdownSettings s, BreakdownType type, in DirectorInputs i)
        {
            switch (type)
            {
                case BreakdownType.Sputter: return s.SputterChancePerSecond * (i.FuelFraction < s.LowFuelFraction ? s.LowFuelMultiplier : 1f);
                case BreakdownType.Belt: return s.BeltChancePerSecond * (i.Heat > s.HotHeat ? s.HotMultiplier : 1f);
                default: return s.BreakerChancePerSecond * (i.LoadAmps > i.RatedAmps ? s.OverloadMultiplier : 1f);
            }
        }

        /// <summary>Advance by dt. Returns the breakdown that starts now, if one does.</summary>
        public BreakdownType? Tick(float dt, in DirectorInputs inputs)
        {
            if (!inputs.ShiftRunning) { return null; }
            _shiftSeconds += dt;
            if (!inputs.GeneratorRunning) { _second = 0f; return null; }
            _second += dt;
            BreakdownType? result = null;
            while (_second >= 1f)
            {
                _second -= 1f;
                BreakdownType? started = RollSecond(inputs, result.HasValue);
                if (started.HasValue && !result.HasValue) { result = started; }
            }
            return result;
        }

        private BreakdownType? RollSecond(in DirectorInputs i, bool alreadyStarted)
        {
            // one roll per type per second, drawn even when nothing can start, so the dice do not depend on how the crew plays
            double r0 = _rng.NextDouble(), r1 = _rng.NextDouble(), r2 = _rng.NextDouble();
            if (alreadyStarted) { return null; }
            if (_shiftSeconds < _s.GraceSeconds) { return null; }
            if (_shiftSeconds - _lastStart < _s.MinGapSeconds) { return null; }
            if (BreakdownTypes.CountOf(i.ActiveMask) >= _s.MaxActive) { return null; }
            for (int t = 0; t < BreakdownTypes.Count; t++)
            {
                var type = (BreakdownType)t;
                if (BreakdownTypes.Has(i.ActiveMask, type)) { continue; }
                double roll = t == 0 ? r0 : t == 1 ? r1 : r2;
                if (roll < Chance(_s, type, i))
                {
                    _lastStart = _shiftSeconds;
                    Started++;
                    return type;
                }
            }
            return null;
        }

        /// <summary>Which of the sweet spots a new sputter uses.</summary>
        public int PickSweetSpot() => _rng.Next(System.Math.Max(1, _s.SweetSpotCount));

        /// <summary>Which outlet trips: one with a cable plugged in when there is one, otherwise any outlet.</summary>
        public int PickOutlet(IReadOnlyList<int> pluggedOutlets, int outletCount)
        {
            if (pluggedOutlets != null && pluggedOutlets.Count > 0) { return pluggedOutlets[_rng.Next(pluggedOutlets.Count)]; }
            return _rng.Next(System.Math.Max(1, outletCount));
        }
    }
}
