using PowerCutCrew.Core;
using UnityEngine;

namespace PowerCutCrew.Breakdowns
{
    /// <summary>Breakdown numbers. Loaded from Resources/BreakdownTuning.</summary>
    [CreateAssetMenu(menuName = "Power Cut Crew/Config/Breakdowns", fileName = "BreakdownTuning")]
    public class BreakdownTuning : TuningConfig
    {
        private static BreakdownTuning _instance;

        public static BreakdownTuning Instance => _instance != null ? _instance : (_instance = Resources.Load<BreakdownTuning>("BreakdownTuning"));

        [Header("When they happen")]
        [Tooltip("No breakdown in the first seconds of a shift.")]
        public float GraceSeconds = 60f;
        [Tooltip("At least this long between two new breakdowns.")]
        public float MinGapSeconds = 40f;
        public int MaxActive = 2;
        [Tooltip("Chance per second of a sputtering engine; times LowFuelMultiplier while the tank is below LowFuelFraction.")]
        public float SputterChancePerSecond = 0.004f;
        public float LowFuelMultiplier = 3f;
        [Range(0f, 1f)] public float LowFuelFraction = 0.25f;
        [Tooltip("Chance per second of a broken fan belt; times HotMultiplier while the heat is above HotHeat.")]
        public float BeltChancePerSecond = 0.003f;
        public float HotMultiplier = 3f;
        public float HotHeat = 70f;
        [Tooltip("Chance per second of a tripped breaker; times OverloadMultiplier while the load is above the rated amps.")]
        public float BreakerChancePerSecond = 0.002f;
        public float OverloadMultiplier = 6f;

        [Header("Sputtering engine")]
        [Tooltip("The output is dead this long (s) ...")]
        public float DipSeconds = 0.6f;
        [Tooltip("... once every this many seconds.")]
        public float DipEverySeconds = 2.5f;
        public int HitsToFix = 3;
        [Tooltip("The sweet spot is one of this many points on the housing (the generator prefab has them).")]
        public int SweetSpotCount = 4;
        [Tooltip("A wrench hit counts when it lands this close (m) to the sweet spot.")]
        public float SweetSpotRadius = 0.25f;
        [Tooltip("The wrench reaches this far (m) from the eye.")]
        public float HitRange = 2f;
        [Tooltip("The host accepts a hit from this much further away (m): the holder's view is a moment old.")]
        public float HitRangeSlack = 0.75f;
        public float SwingCooldown = 0.4f;
        [Tooltip("Dropping the wrench (Q) tosses it forward at this speed (m/s): using it takes the throw button.")]
        public float WrenchTossSpeed = 5f;

        [Header("Broken fan belt")]
        [Tooltip("Extra heat per second while the generator runs with the belt gone (cooling stops too).")]
        public float BeltExtraHeatPerSecond = 3f;
        [Tooltip("Hold Interact this long at the belt slot with a spare belt in hand.")]
        public float BeltFitSeconds = 2f;
        [Tooltip("The old belt flies out and is gone after this long (s).")]
        public float OldPartSeconds = 20f;
        [Tooltip("At most this many spare belts lie around at once.")]
        public int MaxLooseBelts = 2;

        [Header("Tripped breaker")]
        public float BreakerResetSeconds = 1.5f;
        [Tooltip("A reset while the load that would come back is above this times the rated amps trips it again.")]
        public float BreakerHoldAmpsRatio = 1f;
        [Tooltip("... after the lever stayed up this long (s).")]
        public float BreakerRetripSeconds = 1f;

        [Header("Wrench")]
        [Tooltip("A wrench that is not held and lost (below the floor or this far from its hook) goes back to the hook after this long (s).")]
        public float ItemRespawnSeconds = 30f;
        public float LostDistanceMeters = 20f;
        public float FallBelowY = -2f;

        [Header("Neighbours")]
        [Tooltip("A neighbour shouts when a breakdown starts, this long after it (s).")]
        public float NeighbourLineDelay = 1f;

        [Header("Network")]
        [Tooltip("Fix progress of a hold fix goes out in steps of this many percent.")]
        public int ProgressStepPercent = 5;

        public BreakdownSettings ToSettings()
        {
            return new BreakdownSettings
            {
                GraceSeconds = GraceSeconds,
                MinGapSeconds = MinGapSeconds,
                MaxActive = MaxActive,
                SputterChancePerSecond = SputterChancePerSecond,
                LowFuelMultiplier = LowFuelMultiplier,
                LowFuelFraction = LowFuelFraction,
                BeltChancePerSecond = BeltChancePerSecond,
                HotMultiplier = HotMultiplier,
                HotHeat = HotHeat,
                BreakerChancePerSecond = BreakerChancePerSecond,
                OverloadMultiplier = OverloadMultiplier,
                DipSeconds = DipSeconds,
                DipEverySeconds = DipEverySeconds,
                HitsToFix = HitsToFix,
                SweetSpotCount = SweetSpotCount,
                BeltFitSeconds = BeltFitSeconds,
                BreakerResetSeconds = BreakerResetSeconds,
                BreakerHoldAmpsRatio = BreakerHoldAmpsRatio,
                BreakerRetripSeconds = BreakerRetripSeconds,
            };
        }
    }
}
