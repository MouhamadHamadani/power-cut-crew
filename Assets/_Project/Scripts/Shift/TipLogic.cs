using System.Collections.Generic;

namespace PowerCutCrew.Shift
{
    public enum Tip : byte { Plug = 0, Unpowered = 1, Fuel = 2, Overheat = 3, Sputter = 4, Belt = 5, Breaker = 6 }

    public struct TipSettings
    {
        public float TipSeconds;
        public float GapSeconds;
        public float FuelWarnFraction;
        public float HeatWarn;

        public static TipSettings Defaults => new TipSettings { TipSeconds = 7f, GapSeconds = 1.5f, FuelWarnFraction = 0.25f, HeatWarn = 70f };
    }

    /// <summary>What this player's machine sees right now.</summary>
    public struct TipInputs
    {
        /// <summary>Tips only happen while the shift runs.</summary>
        public bool Active;
        public bool NearFreePlug;
        public bool AnyUnpowered;
        public float FuelFraction;
        public float Heat;
        /// <summary>The breakdowns that are active right now.</summary>
        public bool SputterActive;
        public bool BeltActive;
        public bool BreakerActive;
    }

    /// <summary>
    /// Which first-shift tip is on screen (plain C#, unit tested): each tip appears once per game session, only when its trigger happens
    /// (this player near a free plug, a building in the dark, fuel low, generator hot, a breakdown of each kind), one at a time with a pause between them.
    /// A new session means a new instance; "Another shift" keeps this one.
    /// </summary>
    public class TipLogic
    {
        private readonly TipSettings _s;
        private readonly bool[] _seen = new bool[7];
        private readonly Queue<Tip> _waiting = new Queue<Tip>();
        private float _left;
        private float _gap;

        public TipLogic(TipSettings settings)
        {
            _s = settings;
        }

        public Tip? Current { get; private set; }
        public float SecondsLeft => _left;
        public bool HasShown(Tip t) => _seen[(int)t];

        public static string Key(Tip t)
        {
            switch (t)
            {
                case Tip.Plug: return "tip.plug";
                case Tip.Unpowered: return "tip.unpowered";
                case Tip.Fuel: return "tip.fuel";
                case Tip.Sputter: return "tip.sputter";
                case Tip.Belt: return "tip.belt";
                case Tip.Breaker: return "tip.breaker";
                default: return "tip.overheat";
            }
        }

        /// <summary>Advance by a step. Returns the tip that has just started, if one did.</summary>
        public Tip? Update(float deltaTime, TipInputs i)
        {
            if (!i.Active)
            {
                Current = null;
                _left = 0f;
                return null;
            }
            Notice(Tip.Plug, i.NearFreePlug);
            Notice(Tip.Unpowered, i.AnyUnpowered);
            Notice(Tip.Fuel, i.FuelFraction < _s.FuelWarnFraction);
            Notice(Tip.Overheat, i.Heat > _s.HeatWarn);
            Notice(Tip.Sputter, i.SputterActive);
            Notice(Tip.Belt, i.BeltActive);
            Notice(Tip.Breaker, i.BreakerActive);

            if (Current.HasValue)
            {
                _left -= deltaTime;
                if (_left <= 0f)
                {
                    Current = null;
                    _gap = _s.GapSeconds;
                }
                return null;
            }
            _gap -= deltaTime;
            if (_gap <= 0f && _waiting.Count > 0)
            {
                Tip next = _waiting.Dequeue();
                Current = next;
                _left = _s.TipSeconds;
                return next;
            }
            return null;
        }

        private void Notice(Tip t, bool triggered)
        {
            if (!triggered || _seen[(int)t]) { return; }
            _seen[(int)t] = true;   // counted as shown the moment it is queued: it will appear once, whatever happens to the trigger meanwhile
            _waiting.Enqueue(t);
        }
    }
}
