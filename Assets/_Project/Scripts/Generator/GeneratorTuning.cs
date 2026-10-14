using PowerCutCrew.Core;
using UnityEngine;

namespace PowerCutCrew.Generator
{
    /// <summary>Generator numbers. Loaded from Resources/GeneratorTuning.</summary>
    [CreateAssetMenu(menuName = "Power Cut Crew/Config/Generator", fileName = "GeneratorTuning")]
    public class GeneratorTuning : TuningConfig
    {
        private static GeneratorTuning _instance;

        public static GeneratorTuning Instance => _instance != null ? _instance : (_instance = Resources.Load<GeneratorTuning>("GeneratorTuning"));

        [Header("Fuel")]
        public float Capacity = 100f;
        public float IdleBurnPerSecond = 0.2f;
        public float BurnPerAmpPerSecond = 0.05f;

        [Header("Load and heat")]
        [Tooltip("Rated capacity in amps. The brief says 15 A, but two cables can feed at most 13 A (8 + 5) of the four sockets, so 15 A could never overheat; 10 A can.")]
        public float RatedAmps = 10f;
        public float HeatPerAmpOver = 1.5f;
        public float CoolPerSecondRunning = 2f;
        public float CoolPerSecondOff = 1f;
        public float StallHeat = 100f;
        public float RestartBelowHeat = 60f;

        [Header("Pull cord")]
        public float StartChanceCold = 0.35f;
        public float StartChancePerFailedPull = 0.25f;
        public int GuaranteedAtPull = 4;

        [Header("Start state")]
        public float StartFuel = 20f;

        [Header("Network")]
        public float SyncRateHz = 4f;

        public GeneratorSettings ToSettings()
        {
            return new GeneratorSettings
            {
                Capacity = Capacity,
                IdleBurn = IdleBurnPerSecond,
                PerAmpBurn = BurnPerAmpPerSecond,
                RatedAmps = RatedAmps,
                HeatPerAmpOver = HeatPerAmpOver,
                CoolRunning = CoolPerSecondRunning,
                CoolOff = CoolPerSecondOff,
                StallHeat = StallHeat,
                RestartBelowHeat = RestartBelowHeat,
                StartChanceCold = StartChanceCold,
                StartChancePerFail = StartChancePerFailedPull,
                GuaranteedAtPull = GuaranteedAtPull,
            };
        }
    }
}
