using PowerCutCrew.Core;
using UnityEngine;

namespace PowerCutCrew.Shift
{
    /// <summary>Numbers of the first-shift tips and the corner hint. Loaded from Resources/HelpTuning.</summary>
    [CreateAssetMenu(menuName = "Power Cut Crew/Config/Help", fileName = "HelpTuning")]
    public class HelpTuning : TuningConfig
    {
        private static HelpTuning _instance;

        public static HelpTuning Instance => _instance != null ? _instance : (_instance = Resources.Load<HelpTuning>("HelpTuning"));

        [Header("Tips")]
        [Tooltip("A tip stays on screen this long (s).")]
        public float TipSeconds = 7f;
        [Tooltip("Pause between two tips (s).")]
        public float TipGapSeconds = 1.5f;
        [Tooltip("The plug tip appears when this player is within this distance (m) of a free plug.")]
        public float PlugNearMeters = 4f;
        [Tooltip("The fuel tip appears below this fraction of the tank.")]
        [Range(0f, 1f)] public float FuelWarnFraction = 0.25f;
        [Tooltip("The overheat tip appears above this heat (the generator stalls at 100).")]
        public float HeatWarn = 70f;

        public TipSettings ToSettings()
        {
            return new TipSettings { TipSeconds = TipSeconds, GapSeconds = TipGapSeconds, FuelWarnFraction = FuelWarnFraction, HeatWarn = HeatWarn };
        }
    }
}
