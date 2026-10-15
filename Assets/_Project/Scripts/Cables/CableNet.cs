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
        private CableTubeMesh _tube;
        private Mesh _mesh;
        private Material _tubeMaterial;
        private readonly List<Vector3> _points = new List<Vector3>(64);
        private readonly List<Vector3> _samples = new List<Vector3>(256);
        private readonly List<Vector3> _pieceB = new List<Vector3>(64);
        private readonly System.Diagnostics.Stopwatch _meshTimer = new System.Diagnostics.Stopwatch();
        private int _tubeFrames;

        // host: how the plug sits in a socket (it slides in first and is pinned when it arrives)
        private enum Seat { None, Sliding, Seated }
        private Seat _seat;
        private Vector3 _slidePosition;
        private Quaternion _slideRotation;
        private ConfigurableJoint _anchorJoint;
        private Collider[] _colliders;
        private readonly List<(Collider rope, Collider other)> _socketIgnores = new List<(Collider, Collider)>();
        private float _restLength;
        private float _lengthScale = 1f;
        private float _seatedAt;
        private bool _settleReported;
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
                r.enabled = false;   // the cable is drawn as one tube mesh, not as boxes
                _segmentRenderers[i] = r;
                _bodies[i] = seg.transform;
            }
            _bodies[n] = transform;
            _stream.SetBodies(_bodies);

            int longest = CableSpline.SampleCount(t.SegmentCount + 3, t.SplineSubdivisions) + 2;   // outlet + every segment + plug end (two tubes share this)
            _tube = new CableTubeMesh(t.TubeSides, t.TubeRadius, longest);
            _points.Capacity = Mathf.Max(_points.Capacity, t.SegmentCount + 3);
            _pieceB.Capacity = Mathf.Max(_pieceB.Capacity, t.SegmentCount + 3);
            _samples.Capacity = Mathf.Max(_samples.Capacity, longest);
            _mesh = new Mesh { name = "CableTube" };
            _mesh.MarkDynamic();
            _tubeMaterial = new Material(_segmentMaterial);
            _tubeMaterial.SetColor("_BaseColor", t.CableColor);
            _tubeMaterial.color = t.CableColor;
            if (_tubeMaterial.HasProperty("_Smoothness")) { _tubeMaterial.SetFloat("_Smoothness", 0.25f); }
            var tubeObject = new GameObject("Tube");
            tubeObject.transform.SetParent(_segmentRoot.transform, false);
            tubeObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            tubeObject.AddComponent<MeshRenderer>().sharedMaterial = _tubeMaterial;
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
            if (_mesh != null) { Destroy(_mesh); }
            if (_tubeMaterial != null) { Destroy(_tubeMaterial); }
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
                rb.mass = i < n ? t.SegmentMass : Mathf.Min(t.PlugMass, t.SegmentMass * t.MaxPlugToSegmentMassRatio);
                rb.linearDamping = t.LinearDamping;
                rb.angularDamping = t.AngularDamping;
                rb.solverIterations = Mathf.Max(20, t.SolverIterations);
                rb.solverVelocityIterations = Mathf.Max(10, t.SolverVelocityIterations);
                rb.sleepThreshold = t.SleepThreshold;
                rb.maxDepenetrationVelocity = t.MaxDepenetrationVelocity;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.position = pos[i];
                rb.rotation = rot;
                if (i < n)
                {
                    var box = body.gameObject.AddComponent<BoxCollider>();
                    box.size = new Vector3(t.SegmentThickness, t.SegmentThickness, length * 0.95f);
                    box.sharedMaterial = RopeMaterial(t);
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
                ConfigureJoint(_joints[i], _rbs[i - 1], length, t, i == n);
            }
            _colliders = colliders;
            _restLength = t.RestLength;
            // segments must not collide with each other (or the plug)
            for (int i = 0; i <= n; i++)
            {
                for (int j = i + 1; j <= n; j++) { Physics.IgnoreCollision(colliders[i], colliders[j]); }
            }
            // ... nor with the generator they are attached to
            foreach (Collider g in gen.GetComponentsInChildren<Collider>())
            {
                foreach (Collider c in colliders) { Physics.IgnoreCollision(c, g); }
            }
        }

        private static PhysicsMaterial _ropeMaterial;

        private static PhysicsMaterial RopeMaterial(CableTuning t)
        {
            if (_ropeMaterial == null)
            {
                _ropeMaterial = new PhysicsMaterial("Rope")
                {
                    bounciness = 0f, dynamicFriction = t.SegmentFriction, staticFriction = t.SegmentFriction + 0.1f,
                    bounceCombine = PhysicsMaterialCombine.Minimum, frictionCombine = PhysicsMaterialCombine.Minimum,   // never grippier than the rope's own value: no stick-slip
                };
            }
            return _ropeMaterial;
        }

        private static void ConfigureJoint(ConfigurableJoint j, Rigidbody previous, float length, CableTuning t, bool plugEnd)
        {
            j.connectedBody = previous;
            j.autoConfigureConnectedAnchor = false;
            // the rope's end is attached to the back of the plug, not to its middle
            j.anchor = new Vector3(0f, 0f, plugEnd ? -t.PlugBackOffset : -length * 0.5f);
            j.connectedAnchor = new Vector3(0f, 0f, length * 0.5f);
            j.enablePreprocessing = false;
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Free;
            var drive = new JointDrive { positionSpring = t.SegmentSpring, positionDamper = t.SegmentDamper, maximumForce = float.MaxValue };
            j.xDrive = j.yDrive = j.zDrive = drive;
            var bend = new JointDrive { positionSpring = 0f, positionDamper = t.JointAngularDamper, maximumForce = float.MaxValue };
            j.angularXDrive = bend;
            j.angularYZDrive = bend;
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

        /// <summary>For the log lines: where the rope is stretched most ("joint 27: 3.21 m").</summary>
        public string WorstGap()
        {
            int worst = 0; float gap = 0f;
            for (int i = 0; i < _bodies.Length - 1; i++)
            {
                float d = Vector3.Distance(_bodies[i].position, _bodies[i + 1].position);
                if (d > gap) { gap = d; worst = i; }
            }
            return $"widest gap after body {worst}: {gap:F2} m";
        }

        private float ComputeStretch()
        {
            float path = 0f;
            for (int i = 0; i < _bodies.Length - 1; i++) { path += Vector3.Distance(_bodies[i].position, _bodies[i + 1].position); }
            return Mathf.Max(0f, path / (_restLength > 0f ? _restLength : CableTuning.Instance.RestLength) - 1f);
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

            if (_model.State == CableState.Plugged && _socketRef != null) { TickSeat(); }

            _stretch = ComputeStretch();
            if (_seat == Seat.Sliding || (_seat == Seat.Seated && Time.time - _seatedAt < CableTuning.Instance.SeatGraceSeconds)) { _stretch = 0f; }
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

        /// <summary>Host: the released plug slides into the socket; only when it has arrived is it pinned and the rope's last joint moved to the socket.</summary>
        private void TickSeat()
        {
            CableTuning t = CableTuning.Instance;
            if (_seat == Seat.Sliding)
            {
                Pose target = _socketRef.SeatedPose;
                _slidePosition = Vector3.MoveTowards(_slidePosition, target.position, t.PlugSlideSpeed * Time.fixedDeltaTime);
                _slideRotation = Quaternion.RotateTowards(_slideRotation, target.rotation, 720f * Time.fixedDeltaTime);
                _item.Body.MovePosition(_slidePosition);
                _item.Body.MoveRotation(_slideRotation);
                if ((_slidePosition - target.position).sqrMagnitude < 1e-6f && Quaternion.Angle(_slideRotation, target.rotation) < 0.5f) { SeatPlug(); }
            }
            else if (_seat == Seat.Seated && !_settleReported)
            {
                float energy = RopeKineticEnergy();
                if (energy < t.SettleEnergy)
                {
                    _settleReported = true;
                    NetMetrics.Sample("ropeSettleSeconds", Time.time - _seatedAt);
                }
                else if (Time.time - _seatedAt > t.SettleWithinSeconds + 2f)
                {
                    _settleReported = true;
                    NetMetrics.Count("ropeUnsettled");
                    Debug.Log($"[Cable] outlet {Outlet} rope still moving {Time.time - _seatedAt:F1} s after plug-in (energy {energy:F3} J)");
                }
            }
        }

        private void SeatPlug()
        {
            CableTuning t = CableTuning.Instance;
            Pose target = _socketRef.SeatedPose;
            _item.Body.position = target.position;
            _item.Body.rotation = target.rotation;
            transform.SetPositionAndRotation(target.position, target.rotation);
            // the last rope joint goes from the segment to the socket's anchor point: nothing that moves is attached to the rope any more
            int n = _bodies.Length - 1;
            if (_joints[n] != null) { Destroy(_joints[n]); _joints[n] = null; }
            float length = t.RestLength / t.SegmentCount;
            _anchorJoint = _bodies[n - 1].gameObject.AddComponent<ConfigurableJoint>();
            ConfigureJoint(_anchorJoint, _socketRef.AnchorBody, length, t, false);
            _anchorJoint.anchor = new Vector3(0f, 0f, length * _lengthScale * 0.5f);
            _anchorJoint.connectedAnchor = Vector3.zero;
            // the anchor body is not rotated like the rope: its angular limits would whip the last segment into range
            _anchorJoint.angularXMotion = _anchorJoint.angularYMotion = _anchorJoint.angularZMotion = ConfigurableJointMotion.Free;
            _seat = Seat.Seated;
            _seatedAt = Time.time;
            _settleReported = false;
        }

        /// <summary>Host: undo everything plugging did to the rope (joint, collisions, extra length).</summary>
        private void LeaveSocket()
        {
            CableTuning t = CableTuning.Instance;
            int n = _bodies.Length - 1;
            if (_anchorJoint != null) { Destroy(_anchorJoint); _anchorJoint = null; }
            if (_seat == Seat.Seated && _joints[n] == null && _rbs != null)
            {
                _joints[n] = _bodies[n].gameObject.AddComponent<ConfigurableJoint>();
                ConfigureJoint(_joints[n], _rbs[n - 1], t.RestLength / t.SegmentCount, t, true);
            }
            _seat = Seat.None;
            foreach ((Collider rope, Collider other) in _socketIgnores)
            {
                if (rope != null && other != null) { Physics.IgnoreCollision(rope, other, false); }
            }
            _socketIgnores.Clear();
            SetLengthScale(1f);
        }

        /// <summary>The rope gets a little longer when the socket is nearly as far away as the rope is long, so it sags instead of hanging tight.</summary>
        private void SetLengthScale(float scale)
        {
            CableTuning t = CableTuning.Instance;
            _lengthScale = scale;
            _restLength = t.RestLength * scale;
            float length = t.RestLength / t.SegmentCount * scale;
            int n = _bodies.Length - 1;
            for (int i = 1; i <= n; i++)
            {
                if (_joints[i] == null) { continue; }
                if (i < n) { _joints[i].anchor = new Vector3(0f, 0f, -length * 0.5f); }
                _joints[i].connectedAnchor = new Vector3(0f, 0f, length * 0.5f);
            }
            if (_anchorJoint != null) { _anchorJoint.anchor = new Vector3(0f, 0f, length * 0.5f); }
        }

        /// <summary>The plug, the rope and the socket's wall must not fight over the same space.</summary>
        private void IgnoreAround(PowerSocket socket)
        {
            Vector3 point = socket.PlugPoint.position;
            foreach (Collider other in Physics.OverlapSphere(point, 0.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                // only what the plug actually touches (the socket plate and the wall behind it): never the floor under it
                if (Vector3.Distance(other.ClosestPoint(point), point) > 0.3f) { continue; }
                if (other is CharacterController || other.GetComponentInParent<NetPlayer>() != null) { continue; }
                bool ours = other.GetComponentInParent<NetworkObject>() == socket.NetworkObject;
                if (!ours && other.attachedRigidbody != null) { continue; }   // walls and the socket only, not cans or other bodies
                foreach (Collider rope in _colliders)
                {
                    if (rope == null || rope == other) { continue; }
                    Physics.IgnoreCollision(rope, other, true);
                    _socketIgnores.Add((rope, other));
                }
            }
        }

        /// <summary>Host: total kinetic energy of the free rope segments (J). The settle check watches this after plug-in.</summary>
        public float RopeKineticEnergy()
        {
            float energy = 0f;
            if (_rbs == null) { return energy; }
            for (int i = 1; i < _rbs.Length - 1; i++)
            {
                if (_rbs[i] != null && !_rbs[i].isKinematic) { energy += 0.5f * _rbs[i].mass * _rbs[i].linearVelocity.sqrMagnitude; }
            }
            return energy;
        }

        /// <summary>Host: true once the plug is pinned in its socket.</summary>
        public bool PlugSeated => _seat == Seat.Seated;

        public bool HostPlugIn(PowerSocket socket)
        {
            GeneratorNet gen = GeneratorNet.Instance;
            if (_model == null || _model.State != CableState.Held || socket.Occupied) { return false; }
            if (!gen.Grid.Connect(Outlet, socket.Id)) { return false; }
            _item.HostForceRelease();
            _item.Body.isKinematic = true;
            _model.PlugIn(socket.Id);
            _socketRef = socket;
            socket.HostSetOccupied(true, this);

            CableTuning t = CableTuning.Instance;
            float straight = Vector3.Distance(gen.Outlets[Outlet].position, socket.AnchorPoint);
            SetLengthScale(Mathf.Clamp(straight * (1f + t.SlackFraction) / t.RestLength, 1f, t.MaxSlackScale));
            IgnoreAround(socket);
            _seat = Seat.Sliding;
            _slidePosition = _item.Body.position;
            _slideRotation = _item.Body.rotation;

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
            LeaveSocket();
            _socketRef = null;
        }

        private void HostOnPoppedOut()
        {
            ReleaseSocket();
            _item.Body.isKinematic = false;
            _item.Body.AddForce((Vector3.up + UnityEngine.Random.insideUnitSphere * 0.3f) * 3f, ForceMode.VelocityChange);
            PublishState();
            NetMetrics.Count("popOuts");
            Debug.Log($"[Cable] outlet {Outlet} popped out of its socket (stretch {_stretch:P0}, {WorstGap()}, restLength {_restLength:F2} m)");
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
            Debug.Log($"[Cable] outlet {Outlet} SNAPPED at joint {worst} (stretch {_stretch:P0}, {WorstGap()})");
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
                if (State == CableState.Plugged && PowerSocket.ById(_socket.Value) is PowerSocket s) { transform.rotation = s.SeatedPose.rotation; }
                else
                {
                    Vector3 d = _bodies[n].position - _bodies[n - 1].position;
                    if (d.sqrMagnitude > 1e-6f) { transform.rotation = Quaternion.LookRotation(d); }
                }
            }
            RebuildTube();
        }

        /// <summary>
        /// One continuous tube: a Catmull-Rom spline through the rope points, from exactly the generator outlet to exactly the back of the plug
        /// (or of the holder's local copy while it is in their hand). A snapped cable is two tubes. Same code on the host and on clients.
        /// </summary>
        private void RebuildTube()
        {
            if (_tube == null || _bodies == null) { return; }
            _meshTimer.Restart();
            CableTuning t = CableTuning.Instance;
            int n = _bodies.Length - 1;
            GeneratorNet gen = GeneratorNet.Instance;
            Vector3 start = gen != null && gen.Outlets != null && Outlet >= 0 && Outlet < gen.Outlets.Length ? gen.Outlets[Outlet].position : _bodies[0].position;

            bool inHand = !IsServer && _item.HeldByLocalClient && _item.HeldCopy != null && _stream.HasData;
            Vector3 end;
            int lastBody = n - 1;
            if (inHand)
            {
                Transform copy = _item.HeldCopy.transform;
                end = copy.position - copy.forward * t.PlugBackOffset;
                lastBody = n - 4;          // the networked plug lags behind the hand: the cable ends at the hand's copy instead
            }
            else
            {
                end = transform.position - transform.forward * t.PlugBackOffset;
            }

            _tube.Clear();
            int snap = State == CableState.Snapped ? Mathf.Clamp(_snapIndex.Value, 1, n) : -1;
            _points.Clear();
            _points.Add(start);
            if (snap < 0)
            {
                for (int i = 1; i <= lastBody; i++) { _points.Add(_bodies[i].position); }
                _points.Add(end);
                AddPiece(_points, t);
            }
            else
            {
                for (int i = 1; i < snap; i++) { _points.Add(_bodies[i].position); }
                AddPiece(_points, t);
                _pieceB.Clear();
                for (int i = snap; i < n; i++) { _pieceB.Add(_bodies[i].position); }
                _pieceB.Add(end);
                AddPiece(_pieceB, t);
            }
            _tube.Apply(_mesh);
            if (++_tubeFrames > 120)   // the first frames include one-off JIT and page-in costs
            {
                double ms = _meshTimer.Elapsed.TotalMilliseconds;
                NetMetrics.Sample("cableMeshMs", ms);
                if (ms > 1.0) { NetMetrics.Count("cableMeshOver1ms"); }
            }
        }

        private void AddPiece(List<Vector3> points, CableTuning t)
        {
            if (points.Count < 2) { return; }
            CableSpline.Sample(points, t.SplineSubdivisions, _samples);
            _tube.AddTube(_samples);
        }
    }
}
