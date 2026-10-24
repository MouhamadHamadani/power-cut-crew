using PowerCutCrew.Core;
using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>Player movement, look, interaction and throw numbers. Loaded from Resources/PlayerTuning.</summary>
    [CreateAssetMenu(menuName = "Power Cut Crew/Config/Player", fileName = "PlayerTuning")]
    public class PlayerTuning : TuningConfig
    {
        private static PlayerTuning _instance;

        public static PlayerTuning Instance => _instance != null ? _instance : (_instance = Resources.Load<PlayerTuning>("PlayerTuning"));

        [Header("Movement")]
        public float WalkSpeed = 4f;
        public float SprintSpeed = 6.5f;
        public float CrouchSpeed = 2f;
        public float JumpSpeed = 5.5f;
        public float Gravity = -20f;
        public float StandHeight = 1.8f;
        public float CrouchHeight = 1.1f;
        public float EyeHeightStanding = 1.6f;
        public float EyeHeightCrouching = 0.95f;

        [Header("Look")]
        public float LookSensitivity = 0.12f;
        public float MaxPitch = 80f;

        [Header("Interaction")]
        public float InteractRange = 3.5f;

        [Header("Holding")]
        [Tooltip("Hold point relative to the player root: (0, eye height - drop, forward).")]
        public float HoldForward = 1.0f;
        public float HoldBelowEyes = 0.3f;
        public float ThrowSpeed = 9f;
        public float DropForwardSpeed = 0.5f;

        [Header("Name tags")]
        [Tooltip("The tag looks its authored size at this distance (m).")]
        public float NameTagReferenceDistance = 6f;
        [Tooltip("Smallest and largest size on screen, as a factor of the authored size: a tag next to the camera is not huge, one far away stays readable.")]
        public float NameTagMinScreenFactor = 0.6f;
        public float NameTagMaxScreenFactor = 1.4f;
    }
}
