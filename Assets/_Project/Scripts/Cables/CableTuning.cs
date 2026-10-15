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

        [Header("Rope physics (settling)")]
        [Tooltip("Rigidbody solver iterations of every rope body (at least 20) and velocity iterations (at least 10).")]
        public int SolverIterations = 24;
        public int SolverVelocityIterations = 12;
        public float LinearDamping = 1.5f;
        public float AngularDamping = 3f;
        [Tooltip("Damping of the bending between neighbouring segments (N m s/rad): the rope end stops swinging like a pendulum.")]
        public float JointAngularDamper = 0.05f;
        [Tooltip("Friction of the rope on the ground (no bounce); combined with the ground by taking the lower value, so the rope slides smoothly instead of sticking and slipping.")]
        public float SegmentFriction = 0.3f;
        [Tooltip("Rigidbody sleep threshold of the rope bodies. 0 = never sleep: sleeping bodies wake each other with small kicks, which showed as bursts of motion after the rope had settled.")]
        public float SleepThreshold = 0.01f;
        [Tooltip("Rope bodies that overlap something (the coil lies 2 cm in the floor) are pushed out at most this fast (m/s); the default 10 kicks the rope about.")]
        public float MaxDepenetrationVelocity = 0.5f;
        [Tooltip("The plug is never heavier than this many segments (a heavy plug on light segments makes the chain jitter).")]
        public float MaxPlugToSegmentMassRatio = 10f;
        [Tooltip("Where the rope is attached to the plug: this far behind the plug's pivot (m).")]
        public float PlugBackOffset = 0.15f;
        [Tooltip("When plugged, the rope is at least this much longer than the straight line from the outlet to the socket, so it sags (0.05 = 5 %).")]
        public float SlackFraction = 0.05f;
        [Tooltip("The rope can be lengthened to this many times its rest length to make that slack.")]
        public float MaxSlackScale = 1.25f;
        [Tooltip("Speed (m/s) at which the released plug slides into the socket; it is pinned when it arrives.")]
        public float PlugSlideSpeed = 1.5f;
        [Tooltip("The tension rules (snap / pop-out) ignore the rope while the plug slides in and for this long (s) after it is seated: the slide itself tugs the rope.")]
        public float SeatGraceSeconds = 1f;

        [Header("Settle check")]
        [Tooltip("Total kinetic energy of the rope segments (J) below which the rope counts as at rest.")]
        public float SettleEnergy = 0.01f;
        [Tooltip("The rope must be at rest this long (s) after plug-in ...")]
        public float SettleWithinSeconds = 2f;
        [Tooltip("... and stay at rest for this long (s) with nobody touching it.")]
        public float SettleStaySeconds = 10f;

        [Header("Cable mesh")]
        public float TubeRadius = 0.025f;
        public int TubeSides = 6;
        [Tooltip("Catmull-Rom samples per rope segment.")]
        public int SplineSubdivisions = 4;
        public Color32 CableColor = new Color32(0x05, 0x07, 0x0F, 0xFF);

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
