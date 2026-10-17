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
