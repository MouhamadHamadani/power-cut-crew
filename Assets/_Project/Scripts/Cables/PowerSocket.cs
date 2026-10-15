using System.Collections.Generic;
using PowerCutCrew.Generator;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Cables
{
    /// <summary>
    /// A wall socket with a lamp (placeholder for the buildings prompt). The host powers it while a cable from a running
    /// generator is plugged in (via the <see cref="PowerGrid"/>); the lamp state is a NetworkVariable that changes only
    /// when the powered state changes, so clients never flicker.
    /// </summary>
    public class PowerSocket : NetworkBehaviour
    {
        private static readonly List<PowerSocket> _all = new List<PowerSocket>();

        public static IReadOnlyList<PowerSocket> All => _all;

        [SerializeField] private Light _lamp;
        [SerializeField] private Renderer _lampRenderer;
        [SerializeField] private Transform _plugPoint;

        private readonly NetworkVariable<int> _id = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<bool> _powered = new NetworkVariable<bool>(false);
        private readonly NetworkVariable<bool> _occupied = new NetworkVariable<bool>(false);

        private CableNet _cable;

        public int Id => _id.Value;
        public bool Powered => _powered.Value;
        public bool Occupied => _occupied.Value;
        public Transform PlugPoint => _plugPoint;

        private Rigidbody _anchorBody;

        /// <summary>Where a seated plug sits: its tip points into the wall, its back (where the cable is attached) faces out.</summary>
        public Pose SeatedPose => new Pose(_plugPoint.position, Quaternion.LookRotation(-_plugPoint.forward, _plugPoint.up));

        /// <summary>Host: a kinematic body at the seated plug's back; the rope's last joint attaches to it once the plug has slid in.</summary>
        public Rigidbody AnchorBody => _anchorBody;

        /// <summary>World position of the rope's attachment point on a seated plug.</summary>
        public Vector3 AnchorPoint => _plugPoint.position + _plugPoint.forward * CableTuning.Instance.PlugBackOffset;

        /// <summary>Host: the cable plugged in right now.</summary>
        public CableNet PluggedCable => _cable;

        public static PowerSocket ById(int id)
        {
            PowerSocket found = null;
            foreach (PowerSocket s in _all)
            {
                if (s.Id == id && (found == null || s.IsServer)) { found = s; }   // prefer the host's instance
            }
            return found;
        }

        /// <summary>Host: call right after Spawn().</summary>
        public void ServerInit(int id, float ampDraw)
        {
            _id.Value = id;
            GeneratorNet.Instance.Grid.RegisterSocket(id, ampDraw);
        }

        public override void OnNetworkSpawn()
        {
            _all.Add(this);
            if (IsServer && _anchorBody == null)
            {
                var anchor = new GameObject("CableAnchor");
                anchor.transform.SetParent(transform, true);
                anchor.transform.position = AnchorPoint;
                _anchorBody = anchor.AddComponent<Rigidbody>();
                _anchorBody.isKinematic = true;
            }
            _powered.OnValueChanged += (p, n) => ApplyLamp();
            ApplyLamp();
        }

        public override void OnNetworkDespawn()
        {
            _all.Remove(this);
        }

        public void HostSetOccupied(bool occupied, CableNet cable)
        {
            _occupied.Value = occupied;
            _cable = occupied ? cable : null;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer) { return; }
            GeneratorNet gen = GeneratorNet.Instance;
            bool powered = gen != null && gen.Grid.IsPowered(Id, gen.IsRunning);
            if (powered != _powered.Value)
            {
                _powered.Value = powered;
                Debug.Log($"[Socket] {Id} {(powered ? "powered" : "dark")}");
            }
        }

        private void ApplyLamp()
        {
            if (_lamp != null) { _lamp.enabled = _powered.Value; }
            if (_lampRenderer != null)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", _powered.Value ? new Color(1f, 0.9f, 0.5f) : new Color(0.15f, 0.15f, 0.15f));
                block.SetColor("_EmissionColor", _powered.Value ? new Color(1f, 0.8f, 0.3f) * 2f : Color.black);
                _lampRenderer.SetPropertyBlock(block);
            }
        }
    }
}
