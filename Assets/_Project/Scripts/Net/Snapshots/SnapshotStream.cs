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
    /// With a stride above 1 only every Nth body is sent and the bodies in between are interpolated on the client.
    /// </summary>
    public class SnapshotStream : NetworkBehaviour
    {
        public const string MessageName = "pcc.snap";

        private static readonly Dictionary<ulong, SnapshotStream> _streams = new Dictionary<ulong, SnapshotStream>();
        private static bool _handlerRegistered;

        [SerializeField] private Transform[] _bodies;
        [SerializeField] private bool _syncRotation = true;
        [SerializeField] private byte _streamIndex;
        [Tooltip("Send every Nth body (the first and last are always sent); clients interpolate the bodies in between. Cuts the bandwidth of long chains and ropes.")]
        [SerializeField] private int _stride = 1;
        [Tooltip("Overrides NetTuning.SnapshotRateHz when above 0.")]
        [SerializeField] private float _rateHz;
        [SerializeField] private bool _verifyTarget;

        /// <summary>The stream the late-join assertion checks (the chain).</summary>
        public static SnapshotStream VerifyTarget { get; private set; }

        public static IReadOnlyCollection<SnapshotStream> Streams => _streams.Values;

        private int[] _sendIdx;
        private Vector3[] _pos;
        private Quaternion[] _rot;
        private Vector3[] _lastSent;
        private Vector3[] _fullPos;
        private Quaternion[] _fullRot;
        private ushort _seq;
        private float _nextSend;
        private float _lastSendTime;
        private SnapshotInterpolator _interp;

        // verification (late join assertion): host remembers what it sent
        private readonly Dictionary<ushort, Vector3[]> _history = new Dictionary<ushort, Vector3[]>();

        public ulong StreamKey => (NetworkObjectId << 8) | _streamIndex;
        public int BodyCount => _bodies.Length;

        /// <summary>The bodies this stream drives. On clients their transforms are set from snapshots.</summary>
        public Transform[] Bodies => _bodies;

        /// <summary>Set the bodies at runtime (before the object spawns), for streams whose bodies are created in code.</summary>
        public void SetBodies(Transform[] bodies)
        {
            _bodies = bodies;
        }

        /// <summary>Client: sequence number of the newest snapshot received.</summary>
        public ushort ReceivedSeq => _interp != null ? _interp.LatestSeq : (ushort)0;

        /// <summary>Client: positions of the newest snapshot (only the sent bodies when the stride is above 1).</summary>
        public Vector3[] ReceivedPositions => _interp?.LatestPositions;

        public bool TryGetSentPositions(ushort seq, out Vector3[] positions) => _history.TryGetValue(seq, out positions);

        public ushort SentSeq => _seq;

        /// <summary>Client: true once a snapshot has been applied.</summary>
        public bool HasData => _interp != null && _interp.HasData;

        public override void OnNetworkSpawn()
        {
            int n = _bodies.Length;
            int stride = Mathf.Max(1, _stride);
            var idx = new List<int>();
            for (int i = 0; i < n; i += stride) { idx.Add(i); }
            if (idx[idx.Count - 1] != n - 1) { idx.Add(n - 1); }
            _sendIdx = idx.ToArray();
            int m = _sendIdx.Length;
            _pos = new Vector3[m];
            _rot = new Quaternion[m];
            _lastSent = new Vector3[m];
            _fullPos = new Vector3[n];
            _fullRot = new Quaternion[n];
            _streams[StreamKey] = this;
            if (_verifyTarget) { VerifyTarget = this; }
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
            if (VerifyTarget == this) { VerifyTarget = null; }
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
            _nextSend = Time.unscaledTime + 1f / (_rateHz > 0f ? _rateHz : t.SnapshotRateHz);
            bool moved = false;
            for (int k = 0; k < _sendIdx.Length; k++)
            {
                Vector3 p = _bodies[_sendIdx[k]].position;
                if ((p - _lastSent[k]).sqrMagnitude > t.SnapshotMotionThresholdMeters * t.SnapshotMotionThresholdMeters) { moved = true; }
            }
            if (!moved && Time.unscaledTime - _lastSendTime < t.SnapshotHeartbeatSeconds) { return; }
            _lastSendTime = Time.unscaledTime;
            byte[] data = Capture(false);
            Send(data, NetworkDelivery.UnreliableSequenced, ulong.MaxValue);
        }

        private byte[] Capture(bool full)
        {
            NetTuning t = NetTuning.Instance;
            for (int k = 0; k < _sendIdx.Length; k++)
            {
                Transform b = _bodies[_sendIdx[k]];
                _pos[k] = b.position;
                _rot[k] = b.rotation;
                _lastSent[k] = _pos[k];
            }
            _seq++;
            if (LaunchArgs.Verify)
            {
                _history[_seq] = (Vector3[])_pos.Clone();
                _history.Remove((ushort)(_seq - 400));
            }
            return SnapshotCodec.Encode(StreamKey, _seq, Time.unscaledTime, full, _pos, _syncRotation ? _rot : null, _sendIdx.Length,
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
            if (_interp == null || header.Count != _sendIdx.Length) { return; }
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
            Expand();
            for (int i = 0; i < _bodies.Length; i++)
            {
                _bodies[i].position = _fullPos[i];
                if (_syncRotation) { _bodies[i].rotation = _fullRot[i]; }
            }
        }

        /// <summary>Fill the bodies that were not sent by interpolating between the neighbours that were.</summary>
        private void Expand()
        {
            for (int k = 0; k < _sendIdx.Length - 1; k++)
            {
                int a = _sendIdx[k];
                int b = _sendIdx[k + 1];
                for (int i = a; i < b; i++)
                {
                    float t = (float)(i - a) / (b - a);
                    _fullPos[i] = Vector3.Lerp(_pos[k], _pos[k + 1], t);
                    if (_syncRotation) { _fullRot[i] = Quaternion.Slerp(_rot[k], _rot[k + 1], t); }
                }
            }
            int last = _sendIdx.Length - 1;
            _fullPos[_bodies.Length - 1] = _pos[last];
            if (_syncRotation) { _fullRot[_bodies.Length - 1] = _rot[last]; }
        }
    }
}
