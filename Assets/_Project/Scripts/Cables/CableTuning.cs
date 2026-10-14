using PowerCutCrew.Core;
using UnityEngine;

namespace PowerCutCrew.Cables
{
    /// <summary>Cable numbers. Loaded from Resources/CableTuning.</summary>
    [CreateAssetMenu(menuName = "Power Cut Crew/Config/Cable", fileName = "CableTuning")]
    public class CableTuning : TuningConfig
    {
        private static CableTuning _instance;

        public static CableTuning Instance => _instance != null ? _instance : (_instance = Resources.Load<CableTuning>("CableTuning"));

        [Header("Rope")]
        [Tooltip("Rest length of the whole cable (m). The path length of the segments is measured against this.")]
        public float RestLength = 12f;
        [Tooltip("Number of rope segments; the plug is one more body at the end. 30 x 0.4 m = 12 m.")]
        public int SegmentCount = 30;
        public float SegmentMass = 0.3f;
        public float PlugMass = 1f;
        public float SegmentThickness = 0.06f;
        [Tooltip("Linear spring between neighbouring segments (N/m): the cable stretches under load, which is what the tension rules measure.")]
        public float SegmentSpring = 2500f;
        public float SegmentDamper = 20f;
        public float AngularLimitDegrees = 80f;

        [Header("Tension")]
        public float SnapStretch = 0.15f;
        public float SnapSeconds = 0.5f;
        public float PopOutStretch = 0.10f;
        public float PopOutSeconds = 0.5f;

        [Header("Snap and respawn")]
        [Tooltip("Seconds after a snap until a fresh cable is on the reel.")]
        public float ReelReadySeconds = 3f;
        [Tooltip("Seconds after a snap until the broken pieces despawn.")]
        public float PiecesDespawnSeconds = 8f;

        [Header("Plug-in")]
        public float PlugReach = 1.5f;

        public CableSettings ToSettings()
        {
            return new CableSettings
            {
                SnapStretch = SnapStretch,
                SnapSeconds = SnapSeconds,
                PopOutStretch = PopOutStretch,
                PopOutSeconds = PopOutSeconds,
                ReelReadySeconds = ReelReadySeconds,
                PiecesDespawnSeconds = PiecesDespawnSeconds,
            };
        }
    }
}
