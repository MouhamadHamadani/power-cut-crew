using System;
using PowerCutCrew.Core;
using UnityEngine;

namespace PowerCutCrew.Buildings
{
    /// <summary>Per-building overrides: the (localized) name, the subscription in amps and an offset on the schedule seed.</summary>
    [Serializable]
    public struct BuildingEntry
    {
        [Tooltip("String key of the building's display name (UI table).")]
        public string NameKey;
        [Tooltip("Amps the building has subscribed to (and pays for). Lebanese subscriptions are sold by the amp.")]
        public float SubscriptionAmps;
        [Tooltip("Added to the run's seed to make this building's schedule different from the others.")]
        public int SeedOffset;
        [Tooltip("Height of the grey-box building (m): a mixed skyline.")]
        public float HeightMeters;
    }

    /// <summary>Building numbers. Loaded from Resources/BuildingTuning.</summary>
    [CreateAssetMenu(menuName = "Power Cut Crew/Config/Building", fileName = "BuildingTuning")]
    public class BuildingTuning : TuningConfig
    {
        private static BuildingTuning _instance;

        public static BuildingTuning Instance => _instance != null ? _instance : (_instance = Resources.Load<BuildingTuning>("BuildingTuning"));

        [Header("Demand windows (seconds)")]
        public float RequestMin = 60f;
        public float RequestMax = 120f;
        public float QuietMin = 30f;
        public float QuietMax = 60f;
        [Tooltip("The first Quiet window of a shift (so the buildings do not all request in the first second).")]
        public float InitialDelayMin = 3f;
        public float InitialDelayMax = 25f;
        [Tooltip("At most this many buildings request at the same time.")]
        public int MaxRequesting = 3;

        [Header("Peak spikes (the ACs come on)")]
        [Range(0f, 1f)] public float SpikeChance = 0.2f;
        public float SpikeMultiplier = 1.5f;
        public float SpikeSeconds = 20f;

        [Header("Happiness (0..100)")]
        public float StartHappiness = 70f;
        public float RisePerSecond = 2f;
        public float FallPerSecond = 3f;

        [Header("Payment")]
        [Tooltip("Placeholder until the economy prompt: money per amp per minute while Requesting and powered.")]
        public float RatePerAmpPerMinute = 1f;

        [Header("Neighbor lines")]
        [Tooltip("A building that has been unpowered this long (s) while Requesting shouts.")]
        public float UnpoweredLineSeconds = 10f;
        public float BubbleSeconds = 3f;

        [Header("Screen-space markers")]
        [Tooltip("The marker anchor floats this high above the roof (m).")]
        public float MarkerAnchorAboveRoof = 1.2f;
        [Tooltip("The anchor sits this far in from the middle towards the front edge of the roof (m); the buildings are 4 m deep.")]
        public float MarkerAnchorFront = 2f;
        [Tooltip("Full size within this distance (m).")]
        public float NearDistance = 14f;
        [Tooltip("The marker has shrunk to FarScale at this distance (m) and stays there beyond it.")]
        public float FarDistance = 55f;
        [Range(0.3f, 1f)] public float FarScale = 0.85f;
        [Tooltip("Quiet buildings are drawn this transparent, so the eye goes to the ones that need power.")]
        [Range(0.1f, 1f)] public float QuietAlpha = 0.75f;
        [Tooltip("Reference pixels kept free along every screen edge; a building closer to the edge than this counts as off screen.")]
        public float ScreenMargin = 70f;
        [Tooltip("Off-screen icons sit this much further in from the edge (reference pixels), so their arrow stays on screen.")]
        public float CompactInset = 90f;
        [Tooltip("Off-screen icons keep this far (reference pixels) from the top and bottom reserved bands: the icon plus its arrow.")]
        public float CompactReach = 115f;
        [Tooltip("Reference pixels kept free along the bottom edge for the tip panel, the interaction prompt and the corner hint.")]
        public float BottomReserved = 330f;
        [Tooltip("Beyond this distance (m) a marker is compact: icon and amps only, and a small dot for a Quiet building.")]
        public float CompactDistance = 40f;
        [Tooltip("A marker nudged more than this many reference pixels away from its building gets a thin leader line to it.")]
        public float LeaderMinPixels = 36f;
        [Tooltip("Reference pixels kept free along the top edge for the HUD strip.")]
        public float TopReserved = 110f;
        [Tooltip("Gap (reference pixels) between two markers that were pushed apart.")]
        public float NudgeGap = 8f;
        [Tooltip("Speed of the flashing of an Unpowered marker.")]
        public float FlashSpeed = 12f;

        [Header("Network")]
        public float SyncRateHz = 4f;

        [Header("The street's buildings")]
        public BuildingEntry[] Buildings =
        {
            new BuildingEntry { NameKey = "bld.name.0", SubscriptionAmps = 5f, SeedOffset = 11, HeightMeters = 3.6f },
            new BuildingEntry { NameKey = "bld.name.1", SubscriptionAmps = 5f, SeedOffset = 23, HeightMeters = 6.4f },
            new BuildingEntry { NameKey = "bld.name.2", SubscriptionAmps = 5f, SeedOffset = 37, HeightMeters = 4.8f },
            new BuildingEntry { NameKey = "bld.name.3", SubscriptionAmps = 10f, SeedOffset = 53, HeightMeters = 8.0f },
        };

        public BuildingSettings ToSettings()
        {
            return new BuildingSettings
            {
                RequestMin = RequestMin, RequestMax = RequestMax,
                QuietMin = QuietMin, QuietMax = QuietMax,
                InitialDelayMin = InitialDelayMin, InitialDelayMax = InitialDelayMax,
                SpikeChance = SpikeChance, SpikeMultiplier = SpikeMultiplier, SpikeSeconds = SpikeSeconds,
                StartHappiness = StartHappiness, HappinessRise = RisePerSecond, HappinessFall = FallPerSecond,
                RatePerAmpPerMinute = RatePerAmpPerMinute,
                UnpoweredLineSeconds = UnpoweredLineSeconds,
            };
        }
    }
}
