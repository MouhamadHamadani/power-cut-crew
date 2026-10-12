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

        [Header("Held items")]
        public float HeldBlendSeconds = 0.15f;

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
