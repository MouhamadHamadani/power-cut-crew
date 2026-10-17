using System;
using PowerCutCrew.Core;
using UnityEngine;

namespace PowerCutCrew.Net
{
    [Serializable]
    public struct NetSimProfile
    {
        public string Name;
        public int DelayMs;
        public int JitterMs;
        public int LossPercent;
    }

    /// <summary>Every networking number. Loaded from Resources/NetTuning.</summary>
    [CreateAssetMenu(menuName = "Power Cut Crew/Config/Net", fileName = "NetTuning")]
    public class NetTuning : TuningConfig
    {
        private static NetTuning _instance;

        public static NetTuning Instance => _instance != null ? _instance : (_instance = Resources.Load<NetTuning>("NetTuning"));

        [Header("Session")]
        public int MaxPlayers = 4;
        public ushort Port = 7777;
        public float DisconnectTimeoutSeconds = 5f;
        public float ConnectTimeoutSeconds = 12f;
        public uint TickRate = 30;
        public string GameScene = "Street_Greybox";
        public string MenuScene = "MainMenu";

        [Header("Interpolation")]
        public float InterpolationBufferSeconds = 0.1f;

        [Header("Snapshot streams")]
        public float SnapshotRateHz = 20f;
        public float SnapshotExtentMeters = 32f;
        public float SnapshotHeartbeatSeconds = 1f;
        public float SnapshotMotionThresholdMeters = 0.0005f;
        [Tooltip("Adaptive streams (cables): above this speed (m/s) of any sent point the stream runs at its full rate, below it at CableSlowRateHz, at rest not at all (the heartbeat stays).")]
        public float CableFastSpeed = 0.5f;
        public float CableSlowRateHz = 10f;

        [Header("Held items")]
        public float HeldBlendSeconds = 0.15f;
        [Tooltip("A release (drop, throw, plug-in) carries the holder's local hand pose; the host trusts it when it is this close (m) to where the host knows the holder is.")]
        public float ReleasePoseTolerance = 2.5f;
        public float ThrowSpeedMargin = 1.15f;
        [Tooltip("After a local release the holder's copy keeps simulating until the networked item matches it (m) or this long (s).")]
        public float GhostHandoverDistance = 0.05f;
        public float GhostMinSeconds = 0.25f;
        public float GhostMaxSeconds = 1.5f;
        [Tooltip("Cans tumble less (and so land alike on the host and in the holder's copy): extra damping and a bounce-free grippy material.")]
        public float CanLinearDamping = 0.3f;
        public float CanAngularDamping = 2f;
        public float CanFriction = 0.9f;

        [Header("Hold interactions (pour, fill)")]
        public float HoldKeepAliveInterval = 0.2f;
        public float HoldKeepAliveTimeout = 0.5f;

        [Header("Network simulation")]
        [Tooltip("Scale applied to the Multiplayer Tools simulator delay so the measured RTT matches the profile (the simulator delays each direction twice).")]
        public float NetSimCalibration = 0.55f;

        public NetSimProfile[] NetSimProfiles =
        {
            new NetSimProfile { Name = "bad", DelayMs = 100, JitterMs = 30, LossPercent = 2 },
            new NetSimProfile { Name = "awful", DelayMs = 200, JitterMs = 50, LossPercent = 5 },
        };

        public bool TryGetProfile(string name, out NetSimProfile profile)
        {
            foreach (NetSimProfile p in NetSimProfiles)
            {
                if (p.Name == name)
                {
                    profile = p;
                    return true;
                }
            }
            profile = default;
            return false;
        }
    }
}
