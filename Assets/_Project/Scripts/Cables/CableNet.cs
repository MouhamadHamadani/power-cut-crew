using System;
using System.Collections.Generic;
using PowerCutCrew.Generator;
using PowerCutCrew.Net;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Cables
{
    /// <summary>
    /// One power cable. The prefab root is the plug (a spring-held NetworkPhysicsItem); the rope is a chain of rigidbody
    /// segments created at runtime (host: physics and joints, clients: visual-only transforms driven by the
    /// SnapshotStream together with the plug). The host runs the <see cref="CableModel"/> (tension, snap, pop-out,
    /// respawn timers); state, socket and snap index are NetworkVariables so late joiners see everything.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(NetworkPhysicsItem), typeof(SnapshotStream))]
    public class CableNet : NetworkBehaviour, IGrabGate
    {
        public static readonly List<CableNet> All = new List<CableNet>();

        /// <summary>Host: a snapped cable asks for a fresh one on this outlet (raised after the reel delay).</summary>
        public static event Action<int> RespawnRequested;

        [SerializeField] private Mesh _segmentMesh;
        [SerializeField] private Material _segmentMaterial;

        private readonly NetworkVariable<int> _outlet = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<byte> _state = new NetworkVariable<byte>((byte)CableState.Fresh);
        private readonly NetworkVariable<int> _socket = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<int> _snapIndex = new NetworkVariable<int>(-1);

        private Transform[] _bodies;        // segments 0..N-1, then the plug (this transform)
        private Rigidbody[] _rbs;
        private ConfigurableJoint[] _joints;
        private Renderer[] _segmentRenderers;
        private GameObject _segmentRoot;
        private LineRenderer _handRope;
        private NetworkPhysicsItem _item;
        private SnapshotStream _stream;
        private CableModel _model;
        private PowerSocket _socketRef;
        private float _stretch;
        private readonly bool _debug = LaunchArgs.Has("-botlog");
        private float _nextLog;

        /// <summary>A plugged cable is only freed through its socket (unplug); a snapped one is dead.</summary>
        public bool CanGrab => State != CableState.Plugged && State != CableState.Snapped;

        public int Outlet => _outlet.Value;
        public int SocketId => IsServer && _model != null ? _model.SocketId : _socket.Value;
        public CableState State => IsServer && _model != null ? _model.State : (CableState)_state.Value;
        public NetworkPhysicsItem Item => _item;
        public float Stretch => _stretch;
        public int SnapIndex => _snapIndex.Value;
        public Transform[] Bodies => _bodies;

        /// <summary>The newest cable of an outlet that is not a dead pile of pieces.</summary>
        public static CableNet ForOutlet(int outlet)
        {
            CableNet best = null;
            foreach (CableNet c in All)
            {
                if (c.Outlet != outlet || c.State == CableState.Snapped) { continue; }
                if (best == null || c.IsServer || !best.IsServer) { best = c; }   // prefer the host's instance
            }
            return best;
        }

        private void Awake()
        {
            _item = GetComponent<NetworkPhysicsItem>();
            _stream = GetComponent<SnapshotStream>();
            BuildSegments();
        }

        /// <summary>Segment objects exist on every machine (visuals); the host adds rigidbodies and joints in <see cref="HostInit"/>.</summary>
        private void BuildSegments()
        {
            CableTuning t = CableTuning.Instance;
            int n = t.SegmentCount;
            _segmentRoot = new GameObject("CableSegments");
            _bodies = new Transform[n + 1];
            _segmentRenderers = new Renderer[n];
            float length = t.RestLength / n;
            for (int i = 0; i < n; i++)
            {
                var seg = new GameObject($"Seg{i:D2}");
                seg.transform.SetParent(_segmentRoot.transform, false);
                var mesh = new GameObject("Mesh");
                mesh.transform.SetParent(seg.transform, false);
                mesh.transform.localScale = new Vector3(t.SegmentThickness, t.SegmentThickness, length * 0.95f);
                mesh.AddComponent<MeshFilter>().sharedMesh = _segmentMesh;
                var r = mesh.AddComponent<MeshRenderer>();
                r.sharedMaterial = _segmentMaterial;
                _segmentRenderers[i] = r;
                _bodies[i] = seg.transform;
            }
            _bodies[n] = transform;
            _stream.SetBodies(_bodies);

            var hand = new GameObject("HandRope");
            hand.transform.SetParent(_segmentRoot.transform, false);
            _handRope = hand.AddComponent<LineRenderer>();
            _handRope.useWorldSpace = true;
            _handRope.positionCount = 2;
            _handRope.widthMultiplier = t.SegmentThickness;
            _handRope.sharedMaterial = _segmentMaterial;
            _handRope.enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            // a plugged plug must not block the look-ray: the socket behind it is what you interact with to unplug
            _state.OnValueChanged += (previous, now) => _item.SetRaycastable((CableState)now != CableState.Plugged);
            if ((CableState)_state.Value == CableState.Plugged) { _item.SetRaycastable(false); }
            // metric: how far the plug in the holder's hand (local copy) is from the socket when the host confirms the plug-in
            _socket.OnValueChanged += (previous, now) =>
            {
                if (IsServer || now < 0 || !_item.TryGetCopyPosition(out Vector3 copy)) { return; }
                PowerSocket s = PowerSocket.ById(now);
                if (s != null) { NetMetrics.Sample("plugGapCm", Vector3.Distance(copy, s.PlugPoint.position) * 100.0); }
            };
            if (!IsServer)
            {
                foreach (Transform b in _bodies)
                {
                    var rb = b.GetComponent<Rigidbody>();
                    if (rb != null) { rb.isKinematic = true; }
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (_segmentRoot != null) { Destroy(_segmentRoot); }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (_segmentRoot != null) { Destroy(_segmentRoot); }
        }

        // ---------- host: build ----------

        /// <summary>Host: lay the cable coiled on the ground by the outlet and create the rope physics.</summary>
        public void HostInit(int outlet)
        {
            GeneratorNet gen = GeneratorNet.Instance;
            CableTuning t = CableTuning.Instance;
            _outlet.Value = outlet;
            _model = new CableModel(t.ToSettings());
            _state.Value = (byte)CableState.Fresh;

            int n = t.SegmentCount;
            float length = t.RestLength / n;
            Transform outletT = gen.Outlets[outlet];
            Vector3 outwards = outletT.forward;
            outwards.y = 0f;
            outwards.Normalize();
            Vector3[] pos = Coil(outletT.position, outwards, n + 1, length);

            _rbs = new Rigidbody[n + 1];
            _joints = new ConfigurableJoint[n + 1];
            var colliders = new Collider[n + 1];
            for (int i = 0; i <= n; i++)
            {
                Vector3 dir = i < n ? pos[i + 1] - pos[i] : pos[i] - pos[i - 1];
                Quaternion rot = Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir : outwards);
                Transform body = _bodies[i];
                body.SetPositionAndRotation(pos[i], rot);
                Rigidbody rb = i < n ? body.gameObject.AddComponent<Rigidbody>() : _item.Body;
                rb.mass = i < n ? t.SegmentMass : t.PlugMass;
                rb.linearDamping = 0.4f;
                rb.angularDamping = 3f;
                rb.solverIterations = 20;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.position = pos[i];
                rb.rotation = rot;
                if (i < n)
                {
                    var box = body.gameObject.AddComponent<BoxCollider>();
                    box.size = new Vector3(t.SegmentThickness, t.SegmentThickness, length * 0.95f);
                    colliders[i] = box;
                }
                else
                {
                    colliders[i] = GetComponent<Collider>();
                }
                _rbs[i] = rb;
            }
            _rbs[0].isKinematic = true;
            for (int i = 1; i <= n; i++)
            {
                _joints[i] = _bodies[i].gameObject.AddComponent<ConfigurableJoint>();
                ConfigureJoint(_joints[i], _rbs[i - 1], length, t);
            }
            // segments must not collide with each other (or the plug)
            for (int i = 0; i <= n; i++)
            {
                for (int j = i + 1; j <= n; j++) { Physics.IgnoreCollision(colliders[i], colliders[j]); }
            }
        }

        private static void ConfigureJoint(ConfigurableJoint j, Rigidbody previous, float length, CableTuning t)
        {
            j.connectedBody = previous;
            j.autoConfigureConnectedAnchor = false;
            j.anchor = new Vector3(0f, 0f, -length * 0.5f);
            j.connectedAnchor = new Vector3(0f, 0f, length * 0.5f);
            j.enablePreprocessing = false;
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Free;
            var drive = new JointDrive { positionSpring = t.SegmentSpring, positionDamper = t.SegmentDamper, maximumForce = float.MaxValue };
            j.xDrive = j.yDrive = j.zDrive = drive;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
            var lim = new SoftJointLimit { limit = t.AngularLimitDegrees };
            j.angularYLimit = lim;
            j.angularZLimit = lim;
            j.lowAngularXLimit = new SoftJointLimit { limit = -t.AngularLimitDegrees };
            j.highAngularXLimit = lim;
        }

        /// <summary>A flat spiral on the ground starting at the outlet: bodies spaced one segment length apart.</summary>
        private static Vector3[] Coil(Vector3 outlet, Vector3 outwards, int count, float step)
        {
            var pos = new Vector3[count];
            pos[0] = outlet;
            Vector3 side = Vector3.Cross(Vector3.up, outwards);
            Vector3 centre = new Vector3(outlet.x, 0.05f, outlet.z) + outwards * 0.9f;
            float theta = Mathf.PI;               // start on the generator side of the coil
            float r = 0.5f;
            for (int i = 1; i < count; i++)
            {
                theta += step / r;
                r = 0.5f + 0.09f * (theta - Mathf.PI);
                Vector3 p = centre + (outwards * Mathf.Cos(theta) + side * Mathf.Sin(theta)) * r;
                p.y = 0.05f;
                pos[i] = p;
            }
            return pos;
        }

        // ---------- host: rules ----------

        private float ComputeStretch()
        {
            float path = 0f;
            for (int i = 0; i < _bodies.Length - 1; i++) { path += Vector3.Distance(_bodies[i].position, _bodies[i + 1].position); }
            return Mathf.Max(0f, path / CableTuning.Instance.RestLength - 1f);
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private float _testYankUntil;

        /// <summary>Test hook for the bot runs (development builds only): nothing in the street pulls a plugged cable taut, so
        /// this feeds the real tension state machine a 25 % stretch for 0.8 s and the normal pop-out path does the rest.</summary>
        public void TestYank()
        {
            if (IsServer && State == CableState.Plugged) { _testYankUntil = Time.time + 0.8f; }
        }
#endif

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || _model == null) { return; }
            bool held = _item.IsHeld;
            if (held && (_model.State == CableState.Fresh || _model.State == CableState.Loose)) { _model.Grab(); PublishState(); }
            else if (!held && _model.State == CableState.Held) { _model.Release(); PublishState(); }

            if (_model.State == CableState.Plugged && _socketRef != null) { Pin(_socketRef); }

            _stretch = ComputeStretch();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Time.time < _testYankUntil) { _stretch = Mathf.Max(_stretch, 0.25f); }
#endif
            if (_debug && held && Time.time > _nextLog)
            {
                _nextLog = Time.time + 1f;
                NetPlayer holder = NetPlayer.ByClient.TryGetValue(_item.HolderClientId, out NetPlayer hp) ? hp : null;
                float toHold = holder != null ? Vector3.Distance(transform.position, holder.HoldPoint.position) : -1f;
                Debug.Log($"[Cable] held: plug to hold point {toHold:F2} m, stretch {_stretch:P0}, plug speed {_item.Body.linearVelocity.magnitude:F1}, kinematic {_item.Body.isKinematic}, holdJoint {_item.gameObject.GetComponents<ConfigurableJoint>().Length}");
            }
            switch (_model.Tick(Time.fixedDeltaTime, _stretch))
            {
                case CableEvent.Snapped: HostOnSnapped(); break;
                case CableEvent.PoppedOut: HostOnPoppedOut(); break;
                case CableEvent.ReelReady: RespawnRequested?.Invoke(Outlet); break;
                case CableEvent.DespawnPieces:
                    NetworkObject.Despawn(true);
                    break;
            }
        }

        private void PublishState()
        {
            _state.Value = (byte)_model.State;
            _socket.Value = _model.SocketId;
        }

        private void Pin(PowerSocket socket)
        {
            _item.Body.isKinematic = true;
            transform.SetPositionAndRotation(socket.PlugPoint.position, socket.PlugPoint.rotation);
        }

        public bool HostPlugIn(PowerSocket socket)
        {
            GeneratorNet gen = GeneratorNet.Instance;
            if (_model == null || _model.State != CableState.Held || socket.Occupied) { return false; }
            if (!gen.Grid.Connect(Outlet, socket.Id)) { return false; }
            _item.HostForceRelease();
            _model.PlugIn(socket.Id);
            _socketRef = socket;
            socket.HostSetOccupied(true, this);
            Pin(socket);
            PublishState();
            NetMetrics.Count("plugIns");
            Debug.Log($"[Cable] outlet {Outlet} plugged into socket {socket.Id}");
            return true;
        }

        /// <summary>Unplug into somebody's hands.</summary>
        public bool HostUnplug(int playerId)
        {
            if (_model == null || _model.State != CableState.Plugged) { return false; }
            ReleaseSocket();
            _model.Unplug();
            _item.Body.isKinematic = false;
            _item.HostGrab(playerId);
            PublishState();
            NetMetrics.Count("unplugs");
            Debug.Log($"[Cable] outlet {Outlet} unplugged by player {playerId}");
            return true;
        }

        private void ReleaseSocket()
        {
            GeneratorNet gen = GeneratorNet.Instance;
            if (gen != null) { gen.Grid.Disconnect(Outlet); }
            if (_socketRef != null) { _socketRef.HostSetOccupied(false, null); }
            _socketRef = null;
        }

        private void HostOnPoppedOut()
        {
            ReleaseSocket();
            _item.Body.isKinematic = false;
            _item.Body.AddForce((Vector3.up + UnityEngine.Random.insideUnitSphere * 0.3f) * 3f, ForceMode.VelocityChange);
            PublishState();
            NetMetrics.Count("popOuts");
            Debug.Log($"[Cable] outlet {Outlet} popped out of its socket (stretch {_stretch:P0})");
        }

        private void HostOnSnapped()
        {
            // the joint under the most strain breaks: the one whose neighbours are furthest apart
            int worst = 1;
            float worstGap = 0f;
            for (int i = 1; i < _bodies.Length; i++)
            {
                float gap = Vector3.Distance(_bodies[i].position, _bodies[i - 1].position);
                if (gap > worstGap) { worstGap = gap; worst = i; }
            }
            if (_joints[worst] != null) { Destroy(_joints[worst]); _joints[worst] = null; }
            _item.HostForceRelease();
            _item.ServerSetLocked(true);
            ReleaseSocket();
            _snapIndex.Value = worst;
            PublishState();
            NetMetrics.Count("snaps");
            Debug.Log($"[Cable] outlet {Outlet} SNAPPED at joint {worst} (stretch {_stretch:P0})");
        }

        // ---------- clients (and host display): orientation, hand rope ----------

        private void LateUpdate()
        {
            if (_bodies == null) { return; }
            int snap = _snapIndex.Value;
            if (!IsServer && _stream.HasData)
            {
                int n = _bodies.Length - 1;
                for (int i = 0; i < n; i++)
                {
                    Vector3 d = (i == snap - 1 && i > 0) ? _bodies[i].position - _bodies[i - 1].position : _bodies[i + 1].position - _bodies[i].position;
                    if (d.sqrMagnitude > 1e-6f) { _bodies[i].rotation = Quaternion.LookRotation(d); }
                }
                if (State == CableState.Plugged && PowerSocket.ById(_socket.Value) is PowerSocket s) { transform.rotation = s.PlugPoint.rotation; }
                else
                {
                    Vector3 d = _bodies[n].position - _bodies[n - 1].position;
                    if (d.sqrMagnitude > 1e-6f) { transform.rotation = Quaternion.LookRotation(d); }
                }
            }
            UpdateHandRope();
        }

        /// <summary>The holder's plug copy is in their hand; a short visual rope joins it to the networked rope.</summary>
        private void UpdateHandRope()
        {
            bool active = !IsServer && _item.HeldByLocalClient && _item.HeldCopy != null && _stream.HasData;
            _handRope.enabled = active;
            int n = _segmentRenderers.Length;
            for (int i = n - 3; i < n; i++) { _segmentRenderers[i].enabled = !active; }
            if (!active) { return; }
            _handRope.SetPosition(0, _item.HeldCopy.transform.position);
            _handRope.SetPosition(1, _bodies[n - 3].position);
        }
    }
}
