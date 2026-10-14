using PowerCutCrew.Core;
using UnityEngine;

namespace PowerCutCrew.Fuel
{
    /// <summary>Fuel numbers: jerrycan capacity and the pour / fill rates. Loaded from Resources/FuelTuning.</summary>
    [CreateAssetMenu(menuName = "Power Cut Crew/Config/Fuel", fileName = "FuelTuning")]
    public class FuelTuning : TuningConfig
    {
        private static FuelTuning _instance;

        public static FuelTuning Instance => _instance != null ? _instance : (_instance = Resources.Load<FuelTuning>("FuelTuning"));

        [Header("Jerrycan")]
        public float CanCapacity = 35f;

        [Header("Transfer (units per second)")]
        [Tooltip("Pouring from a can into the generator tank while E is held.")]
        public float PourRate = 10f;
        [Tooltip("Filling a can at the fuel drum while E is held.")]
        public float DrumFillRate = 15f;

        [Header("Network")]
        [Tooltip("Fuel amounts are sent in steps of this many units, at most this often (s).")]
        public float SyncStep = 0.25f;
        public float SyncInterval = 0.1f;
    }
}
