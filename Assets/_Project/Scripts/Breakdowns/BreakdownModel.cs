using UnityEngine;

namespace PowerCutCrew.Breakdowns
{
    public enum HitResult
    {
        /// <summary>No sputter to fix: the wrench just clangs.</summary>
        NoBreakdown,
        /// <summary>Not on the sweet spot: a loud clang, the wrench bounces, no progress.</summary>
        Miss,
        /// <summary>On the sweet spot: one hit closer.</summary>
        Progress,
        /// <summary>The last hit: the engine runs smoothly again.</summary>
        Fixed,
    }

    public enum ResetResult
    {
        /// <summary>The breaker is not tripped, or the lever is already up.</summary>
        Nothing,
        /// <summary>Still holding the lever.</summary>
        Holding,
        /// <summary>The lever is up and stays up: fixed.</summary>
        Fixed,
        /// <summary>The lever went up while the street is still overloaded: it falls back after a moment.</summary>
        WillRetrip,
    }

    /// <summary>
    /// The rules of the three breakdowns and their fixes (plain C#, host only, unit tested): which are active and for how long, the sputter's sweet spot and hits, the
    /// fan belt's fitting progress, the breaker's reset and re-trip, and the fix statistics for the summary. It knows nothing about objects: the net layer tells it
    /// what was hit, who holds what and how loaded the generator is, and reads back what to show.
    /// </summary>
    public class BreakdownModel
    {
        private readonly BreakdownSettings _s;
        private readonly float[] _age = new float[BreakdownTypes.Count];
        private byte _active;
        private float _beltProgress;
        private float _resetProgress;
        private float _retripLeft;

        public BreakdownModel(BreakdownSettings settings)
        {
            _s = settings;
            TrippedOutlet = -1;
        }

        public byte ActiveMask => _active;
        public int ActiveCount => BreakdownTypes.CountOf(_active);
        public bool IsActive(BreakdownType t) => BreakdownTypes.Has(_active, t);

        /// <summary>Seconds a breakdown has been active (0 when it is not).</summary>
        public float AgeOf(BreakdownType t) => IsActive(t) ? _age[(int)t] : 0f;

        /// <summary>Which of the sweet spots glows (only meaningful while the engine sputters).</summary>
        public int SweetSpot { get; private set; }

        /// <summary>Good hits so far on the current sputter.</summary>
        public int HitsDone { get; private set; }

        /// <summary>Which outlet the breaker took down (-1 when the breaker is fine).</summary>
        public int TrippedOutlet { get; private set; }

        /// <summary>The lever is up (the reset went through but the street is overloaded): the outlet has power for a moment, then it falls again.</summary>
        public bool LeverUp => IsActive(BreakdownType.Breaker) && _retripLeft > 0f;

        /// <summary>The outlet delivers no power right now.</summary>
        public bool OutletTripped => IsActive(BreakdownType.Breaker) && !LeverUp;

        /// <summary>How far the belt is fitted, 0..1.</summary>
        public float BeltProgress => _s.BeltFitSeconds <= 0f ? 0f : Mathf.Clamp01(_beltProgress / _s.BeltFitSeconds);

        /// <summary>How far the breaker reset is, 0..1.</summary>
        public float ResetProgress => _s.BreakerResetSeconds <= 0f ? 0f : Mathf.Clamp01(_resetProgress / _s.BreakerResetSeconds);

        public int FixedCount { get; private set; }

        /// <summary>How long the breakdown that was fixed last had been active (s).</summary>
        public float LastFixSeconds { get; private set; }
        public float TotalFixSeconds { get; private set; }
        public int RetripCount { get; private set; }
        public float AverageFixSeconds => FixedCount == 0 ? 0f : TotalFixSeconds / FixedCount;

        /// <summary>The engine is in the dead part of its cough cycle right now.</summary>
        public bool Dipping => IsActive(BreakdownType.Sputter) && SputterPattern.Dipping(_age[(int)BreakdownType.Sputter], _s.DipEverySeconds, _s.DipSeconds);

        /// <summary>Start a breakdown. False when this kind is already active. <paramref name="sweetSpot"/> is used by Sputter, <paramref name="outlet"/> by Breaker.</summary>
        public bool Start(BreakdownType type, int sweetSpot = 0, int outlet = 0)
        {
            if (IsActive(type)) { return false; }
            _active |= BreakdownTypes.Bit(type);
            _age[(int)type] = 0f;
            switch (type)
            {
                case BreakdownType.Sputter:
                    SweetSpot = Mathf.Clamp(sweetSpot, 0, Mathf.Max(0, _s.SweetSpotCount - 1));
                    HitsDone = 0;
                    break;
                case BreakdownType.Belt:
                    _beltProgress = 0f;
                    break;
                default:
                    TrippedOutlet = outlet;
                    _resetProgress = 0f;
                    _retripLeft = 0f;
                    break;
            }
            return true;
        }

        /// <summary>Drop every breakdown (the shift is over or the street is reset). Nothing counts as fixed.</summary>
        public void ClearAll()
        {
            _active = 0;
            HitsDone = 0;
            _beltProgress = 0f;
            _resetProgress = 0f;
            _retripLeft = 0f;
            TrippedOutlet = -1;
            for (int i = 0; i < _age.Length; i++) { _age[i] = 0f; }
        }

        /// <summary>Forget the fix statistics as well (a new shift).</summary>
        public void ResetStats()
        {
            FixedCount = 0;
            TotalFixSeconds = 0f;
            RetripCount = 0;
        }

        /// <summary>Time passes: ages grow and the lever of a reset breaker falls back when its time is up. Returns true when the breaker tripped again.</summary>
        public bool Tick(float dt)
        {
            for (int i = 0; i < _age.Length; i++)
            {
                if ((_active & (1 << i)) != 0) { _age[i] += dt; }
            }
            if (IsActive(BreakdownType.Breaker) && _retripLeft > 0f)
            {
                _retripLeft -= dt;
                if (_retripLeft <= 0f)
                {
                    _retripLeft = 0f;
                    _resetProgress = 0f;
                    RetripCount++;
                    return true;
                }
            }
            return false;
        }

        private void Fix(BreakdownType type)
        {
            FixedCount++;
            LastFixSeconds = _age[(int)type];
            TotalFixSeconds += LastFixSeconds;
            _active = (byte)(_active & ~BreakdownTypes.Bit(type));
            _age[(int)type] = 0f;
            switch (type)
            {
                case BreakdownType.Sputter: HitsDone = 0; break;
                case BreakdownType.Belt: _beltProgress = 0f; break;
                default: TrippedOutlet = -1; _resetProgress = 0f; _retripLeft = 0f; break;
            }
        }

        // ---------- sputtering engine ----------

        /// <summary>A wrench hit landed. <paramref name="spotHit"/> is the sweet spot the hit point is close to, or -1 for a hit elsewhere on the housing.</summary>
        public HitResult Hit(int spotHit)
        {
            if (!IsActive(BreakdownType.Sputter)) { return HitResult.NoBreakdown; }
            if (spotHit != SweetSpot) { return HitResult.Miss; }
            HitsDone++;
            if (HitsDone >= _s.HitsToFix)
            {
                Fix(BreakdownType.Sputter);
                return HitResult.Fixed;
            }
            return HitResult.Progress;
        }

        // ---------- fan belt ----------

        /// <summary>Somebody holds the fit button with a belt at the slot for dt seconds. True when the belt is on.</summary>
        public bool FitBelt(float dt)
        {
            if (!IsActive(BreakdownType.Belt)) { return false; }
            _beltProgress += dt;
            if (_beltProgress < _s.BeltFitSeconds) { return false; }
            Fix(BreakdownType.Belt);
            return true;
        }

        /// <summary>The hold ended early (the player let go, walked off or dropped the belt): the fitting starts over.</summary>
        public void CancelFit() => _beltProgress = 0f;

        // ---------- breaker ----------

        /// <summary>
        /// Somebody holds the lever for dt seconds. <paramref name="loadIfRestored"/> is the load the generator would carry with the tripped outlet back on;
        /// above <see cref="BreakdownSettings.BreakerHoldAmpsRatio"/> times <paramref name="ratedAmps"/> the lever falls back after a moment.
        /// </summary>
        public ResetResult ResetBreaker(float dt, float loadIfRestored, float ratedAmps)
        {
            if (!IsActive(BreakdownType.Breaker) || _retripLeft > 0f) { return ResetResult.Nothing; }
            _resetProgress += dt;
            if (_resetProgress < _s.BreakerResetSeconds) { return ResetResult.Holding; }
            if (loadIfRestored > _s.BreakerHoldAmpsRatio * ratedAmps)
            {
                _retripLeft = Mathf.Max(0.01f, _s.BreakerRetripSeconds);
                _resetProgress = _s.BreakerResetSeconds;
                return ResetResult.WillRetrip;
            }
            Fix(BreakdownType.Breaker);
            return ResetResult.Fixed;
        }

        /// <summary>The hold ended early: the lever slips back and the reset starts over.</summary>
        public void CancelReset()
        {
            if (_retripLeft <= 0f) { _resetProgress = 0f; }
        }
    }

    /// <summary>The cough cycle of a sputtering engine (plain C#, unit tested).</summary>
    public static class SputterPattern
    {
        /// <summary>Dead for <paramref name="dip"/> seconds at the end of every <paramref name="every"/> seconds, so the first cough comes a moment after the breakdown starts.</summary>
        public static bool Dipping(float age, float every, float dip)
        {
            if (every <= 0f || dip <= 0f || age < 0f) { return false; }
            return age % every >= every - dip;
        }
    }

    /// <summary>Which sweet spot a wrench hit landed on (plain C#, unit tested).</summary>
    public static class SweetSpotRule
    {
        /// <summary>The index of the spot closest to the point within the radius, or -1 when the hit is nowhere near any of them.</summary>
        public static int Nearest(Vector3 point, System.Collections.Generic.IList<Vector3> spots, float radius)
        {
            int best = -1;
            float bestD = radius;
            for (int i = 0; i < spots.Count; i++)
            {
                float d = Vector3.Distance(point, spots[i]);
                if (d <= bestD) { bestD = d; best = i; }
            }
            return best;
        }
    }

    /// <summary>When a lost item goes back to its hook (plain C#, unit tested): not held, and below the floor or far from home, for a continuous stretch.</summary>
    public class ItemRespawnRule
    {
        private readonly float _seconds;
        private readonly float _lostDistance;
        private readonly float _fallBelowY;
        private float _lostFor;

        public ItemRespawnRule(float respawnSeconds, float lostDistanceMeters, float fallBelowY)
        {
            _seconds = respawnSeconds;
            _lostDistance = lostDistanceMeters;
            _fallBelowY = fallBelowY;
        }

        public bool IsLost(bool held, Vector3 position, Vector3 home)
        {
            return !held && (position.y < _fallBelowY || Vector3.Distance(position, home) > _lostDistance);
        }

        /// <summary>Advance by dt; true once the item has been lost for long enough (the timer then starts over).</summary>
        public bool Tick(float dt, bool held, Vector3 position, Vector3 home)
        {
            if (!IsLost(held, position, home)) { _lostFor = 0f; return false; }
            _lostFor += dt;
            if (_lostFor < _seconds) { return false; }
            _lostFor = 0f;
            return true;
        }

        public void Reset() => _lostFor = 0f;
    }
}
