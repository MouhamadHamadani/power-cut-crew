using PowerCutCrew.Core;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Shift
{
    /// <summary>Shift numbers. Loaded from Resources/ShiftTuning.</summary>
    [CreateAssetMenu(menuName = "Power Cut Crew/Config/Shift", fileName = "ShiftTuning")]
    public class ShiftTuning : TuningConfig
    {
        private static ShiftTuning _instance;

        public static ShiftTuning Instance => _instance != null ? _instance : (_instance = Resources.Load<ShiftTuning>("ShiftTuning"));

        [Header("Timing")]
        [Tooltip("Length of the power cut in seconds (10 minutes). -shiftseconds <n> overrides it for bot runs.")]
        public float DurationSeconds = 600f;
        public float CountdownSeconds = 3f;

        [Header("Verdict (uptime %)")]
        public float LovedFrom = 90f;
        public float GoodFrom = 70f;
        public float MehFrom = 40f;

        [Header("Mood of the street")]
        public Color DayAmbient = new Color(0.55f, 0.6f, 0.7f);
        public Color NightAmbient = new Color(0.08f, 0.1f, 0.18f);
        [Range(0f, 1f)] public float NightSunIntensity = 0.12f;
        public float MoodSeconds = 1.5f;

        [Header("Network")]
        public float SyncRateHz = 4f;

        public float Duration => LaunchArgs.Has("-shiftseconds") ? float.Parse(LaunchArgs.Get("-shiftseconds"), System.Globalization.CultureInfo.InvariantCulture) : DurationSeconds;
    }
}
