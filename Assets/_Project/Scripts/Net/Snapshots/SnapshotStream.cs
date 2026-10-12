using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// Reusable many-body physics sync (cables, chains). The host simulates the bodies and sends their quantised
    /// poses as one unreliable message at a fixed rate (only when something moved, plus a heartbeat). Clients buffer
    /// and interpolate onto visual-only transforms. Late joiners ask for a full snapshot on spawn.
    /// Many-body physics must sync through this, not through per-object NetworkTransforms.
    /// </summary>
    public class SnapshotStream : NetworkBehaviour
    {
        public const string MessageName = "pcc.snap";

        private static readonly Dictionary<ulong, SnapshotStream> _streams = new Dictionary<ulong, SnapshotStream>();
        private static bool _handlerRegistered;

        [SerializeField] private Transform[] _bodies;
        [SerializeField] private bool _syncRotation = true;
        [SerializeField] private byte _streamIndex;

        private Vector3[] _pos;
        private Quaternion[] _rot;
        private Vector3[] _lastSent;
        private ushort _seq;
        private float _nextSend;
        private float _lastSendTime;
        private SnapshotInterpolator _interp;

        // verification (late join assertion): host remembers what it sent
        private readonly Dictionary<ushort, Vector3[]> _history = new Dictionary<ushort, Vector3[]>();

        public ulong StreamKey => (NetworkObjectId << 8) | _streamIndex;
        public int BodyCount => _bodies.Length;

        /// <summary>Client: sequence number of the newest snapshot received.</summary>
        public ushort ReceivedSeq => _interp != null ? _interp.LatestSeq : (ushort)0;

        public Vector3[] ReceivedPositions => _interp?.LatestPositions;

        public bool TryGetSentPositions(ushort seq, out Vector3[] positions) => _history.TryGetValue(seq, out positions);

        public ushort SentSeq => _seq;

        public override void OnNetworkSpawn()
        {
            int n = _bodies.Length;
            _pos = new Vector3[n];
            _rot = new Quaternion[n];
            _lastSent = new Vector3[n];
            _streams[StreamKey] = this;
            if (IsServer)
            {
                return;
            }
            foreach (Transform b in _bodies)
            {
                var rb = b.GetComponent<Rigidbody>();
                if (rb != null) { rb.isKinematic = true; }
                foreach (Joint j in b.GetComponents<Joint>()) { Destroy(j); }
            }
            _interp = new SnapshotInterpolator();
            if (!_handlerRegistered)
            {
                NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, OnMessage);
                _handlerRegistered = true;
            }
            RequestFullSnapshotRpc();
        }

        public override void OnNetworkDespawn()
        {
            _streams.Remove(StreamKey);
            if (_streams.Count == 0 && _handlerRegistered && NetworkManager != null && NetworkManager.CustomMessagingManager != null)
            {
                NetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(MessageName);
                _handlerRegistered = false;
            }
        }

        // ---------- host ----------

        private void Update()
        {
            if (!IsSpawned || !IsServer || Time.unscaledTime < _nextSend) { return; }
            NetTuning t = NetTuning.Instance;
            _nextSend = Time.unscaledTime + 1f / t.SnapshotRateHz;
            bool moved = false;
            for (int i = 0; i < _bodies.Length; i++)
            {
                Vector3 p = _bodies[i].position;
                if ((p - _lastSent[i]).sqrMagnitude > t.SnapshotMotionThresholdMeters * t.SnapshotMotionThresholdMeters) { moved = true; }
            }
            if (!moved && Time.unscaledTime - _lastSendTime < t.SnapshotHeartbeatSeconds) { return; }
            _lastSendTime = Time.unscaledTime;
            byte[] data = Capture(false);
            Send(data, NetworkDelivery.UnreliableSequenced, ulong.MaxValue);
        }

        private byte[] Capture(bool full)
        {
            NetTuning t = NetTuning.Instance;
            for (int i = 0; i < _bodies.Length; i++)
            {
                _pos[i] = _bodies[i].position;
                _rot[i] = _bodies[i].rotation;
                _lastSent[i] = _pos[i];
            }
            _seq++;
            if (LaunchArgs.Verify)
            {
                _history[_seq] = (Vector3[])_pos.Clone();
                _history.Remove((ushort)(_seq - 400));
            }
            return SnapshotCodec.Encode(StreamKey, _seq, Time.unscaledTime, full, _pos, _syncRotation ? _rot : null, _bodies.Length,
                Vector3.zero, t.SnapshotExtentMeters);
        }

        private void Send(byte[] data, NetworkDelivery delivery, ulong clientId)
        {
            using (var writer = new FastBufferWriter(data.Length + 8, Allocator.Temp))
            {
                writer.WriteValueSafe(data);
                if (clientId == ulong.MaxValue)
                {
                    NetworkManager.CustomMessagingManager.SendNamedMessageToAll(MessageName, writer, delivery);
                }
                else
                {
                    NetworkManager.CustomMessagingManager.SendNamedMessage(MessageName, clientId, writer, delivery);
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestFullSnapshotRpc(RpcParams rpcParams = default)
        {
            Send(Capture(true), NetworkDelivery.ReliableSequenced, rpcParams.Receive.SenderClientId);
            NetMetrics.Count("snapshotFullSent");
        }

        // ---------- clients ----------

        private static void OnMessage(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte[] data);
            if (!SnapshotCodec.TryReadHeader(data, out SnapshotHeader header)) { return; }
            if (_streams.TryGetValue(header.StreamKey, out SnapshotStream stream)) { stream.Receive(data, header); }
        }

        private void Receive(byte[] data, SnapshotHeader header)
        {
            if (_interp == null || header.Count != _bodies.Length) { return; }
            SnapshotCodec.DecodeBodies(data, header, _pos, _rot, Vector3.zero, NetTuning.Instance.SnapshotExtentMeters);
            if (_interp.Push(Time.unscaledTime, header.Seq, header.HostTime, _pos, header.HasRotation ? _rot : null))
            {
                NetMetrics.Count("snapshotRx");
                if (header.Full) { NetMetrics.Count("snapshotFullRx"); }
            }
            else
            {
                NetMetrics.Count("snapshotDropped");
            }
        }

        private void LateUpdate()
        {
            if (_interp == null || !_interp.HasData) { return; }
            if (!_interp.Sample(Time.unscaledTime, NetTuning.Instance.InterpolationBufferSeconds, _pos, _syncRotation ? _rot : null)) { return; }
            for (int i = 0; i < _bodies.Length; i++)
            {
                _bodies[i].position = _pos[i];
                if (_syncRotation) { _bodies[i].rotation = _rot[i]; }
            }
        }
    }
}
