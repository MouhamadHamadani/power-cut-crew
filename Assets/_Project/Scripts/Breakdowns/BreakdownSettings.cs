namespace PowerCutCrew.Breakdowns
{
    /// <summary>Every breakdown number as plain values (built from the BreakdownTuning asset, or by tests).</summary>
    public struct BreakdownSettings
    {
        // when they happen
        public float GraceSeconds;
        public float MinGapSeconds;
        public int MaxActive;
        public float SputterChancePerSecond;
        public float LowFuelMultiplier;
        public float LowFuelFraction;
        public float BeltChancePerSecond;
        public float HotMultiplier;
        public float HotHeat;
        public float BreakerChancePerSecond;
        public float OverloadMultiplier;

        // sputtering engine
        public float DipSeconds;
        public float DipEverySeconds;
        public int HitsToFix;
        public int SweetSpotCount;

        // fan belt
        public float BeltFitSeconds;

        // breaker
        public float BreakerResetSeconds;
        public float BreakerHoldAmpsRatio;
        public float BreakerRetripSeconds;

        public static BreakdownSettings Defaults => new BreakdownSettings
        {
            GraceSeconds = 60f,
            MinGapSeconds = 40f,
            MaxActive = 2,
            SputterChancePerSecond = 0.004f,
            LowFuelMultiplier = 3f,
            LowFuelFraction = 0.25f,
            BeltChancePerSecond = 0.003f,
            HotMultiplier = 3f,
            HotHeat = 70f,
            BreakerChancePerSecond = 0.002f,
            OverloadMultiplier = 6f,
            DipSeconds = 0.6f,
            DipEverySeconds = 2.5f,
            HitsToFix = 3,
            SweetSpotCount = 4,
            BeltFitSeconds = 2f,
            BreakerResetSeconds = 1.5f,
            BreakerHoldAmpsRatio = 1f,
            BreakerRetripSeconds = 1f,
        };
    }
}
