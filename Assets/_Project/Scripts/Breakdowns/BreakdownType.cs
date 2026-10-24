using System;

namespace PowerCutCrew.Breakdowns
{
    /// <summary>The three ways the generator breaks. The number is the bit in the active mask.</summary>
    public enum BreakdownType : byte
    {
        /// <summary>The engine coughs: output dies for a moment now and then. Fix: smack the glowing sweet spot with the wrench.</summary>
        Sputter = 0,
        /// <summary>The fan belt is gone: no cooling, extra heat. Fix: fit a spare belt from the crate.</summary>
        Belt = 1,
        /// <summary>The breaker tripped: one outlet is dead. Fix: reset the lever (not while the street is overloaded).</summary>
        Breaker = 2,
    }

    public static class BreakdownTypes
    {
        public const int Count = 3;

        public static byte Bit(BreakdownType t) => (byte)(1 << (int)t);

        public static bool Has(byte mask, BreakdownType t) => (mask & Bit(t)) != 0;

        public static int CountOf(byte mask)
        {
            int n = 0;
            for (int i = 0; i < Count; i++) { if ((mask & (1 << i)) != 0) { n++; } }
            return n;
        }

        /// <summary>The key part of the strings: "sputter", "belt", "breaker".</summary>
        public static string Name(BreakdownType t)
        {
            switch (t)
            {
                case BreakdownType.Sputter: return "sputter";
                case BreakdownType.Belt: return "belt";
                default: return "breaker";
            }
        }
    }
}
