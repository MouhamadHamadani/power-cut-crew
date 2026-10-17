using System.Collections;
using System.Collections.Generic;
using PowerCutCrew.Core;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>Optional component on an item that can veto being picked up right now (a plugged or snapped cable plug).</summary>
    public interface IGrabGate
    {
        bool CanGrab { get; }
    }

    /// <summary>What a holder asked for when releasing: optionally the local hand pose, and the velocity (throws).</summary>
    public struct ReleaseRequest
    {
        public bool HasPose;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
    }

    /// <summary>
    /// A grabbable physics object. The host simulates the Rigidbody; clients show the interpolated NetworkTransform.
    /// The holder sees a local copy attached to the hand so the held item never trails the camera; on release the copy
    /// keeps simulating locally (a ghost) until the networked item has caught up, so drops and throws start where the
    /// holder saw the item. Everybody else sees the networked item. The host owns the real item.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class NetworkPhysicsItem : NetworkBehaviour, IHeldItem
    {
        public const ulong NoHolder = ulong.MaxValue;
        private const int IgnoreRaycastLayer = 2;

        /// <summary>Host-side arbitration of who holds what (player id = client id).</summary>
        public static readonly InteractionArbiter Arbiter = new InteractionArbiter();

        public static readonly List<NetworkPhysicsItem> All = new List<NetworkPhysicsItem>();
        private static readonly Dictionary<ulong, NetworkPhysicsItem> _byObjectId = new Dictionary<ulong, NetworkPhysicsItem>();

        [SerializeField] private string _kind = "can";
        [SerializeField] private Transform _visualRoot;
        [SerializeField] private Renderer[] _renderers;
        [Tooltip("Hold with a spring joint to the holder's hold body (cable plug) instead of a kinematic follow (cans).")]
        [SerializeField] private bool _springHold;
        [SerializeField] private float _holdSpring = 600f;
        [SerializeField] private float _holdDamper = 60f;
        [SerializeField] private float _holdMaxForce = 3000f;

        private readonly NetworkVariable<ulong> _holder = new NetworkVariable<ulong>(NoHolder);
        private readonly NetworkVariable<int> _holderVersion = new NetworkVariable<int>(0);
        private readonly NetworkVariable<int> _index = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<bool> _locked = new NetworkVariable<bool>(false);

        private Rigidbody _rb;
        private Transform _follow;
        private ConfigurableJoint _holdJoint;
        private GameObject _heldCopy;
        private Coroutine _blend;
        private float _heldSince;
        private float _nextLagLog;
        private int _originalLayer;

        // local release ghost (holder's machine)
        private bool _ghostActive;
        private Rigidbody _ghostBody;
        private float _ghostStart;
        private Vector3 _lastNetPos;
        private float _netSpeed;

        public string Kind => _kind;
        public ulong HolderClientId => _holder.Value;
        public bool IsHeld => _holder.Value != NoHolder;
        public int HolderVersion => _holderVersion.Value;
        public int Index => _index.Value;
        public bool Locked => _locked.Value;
        public Rigidbody Body => _rb;
        public Transform VisualRoot => _visualRoot;
        public GameObject HeldCopy => _heldCopy != null && !_ghostActive ? _heldCopy : null;

        /// <summary>False while the item is locked or its gate (for example a plugged cable) forbids picking it up.</summary>
        public bool CanBeGrabbed
        {
            get
            {
                if (_locked.Value) { return false; }
                IGrabGate gate = GetComponent<IGrabGate>();
                return gate == null || gate.CanGrab;
            }
        }

        public bool HeldByLocalClient => NetworkManager != null && IsHeld && _holder.Value == NetworkManager.LocalClientId;

        /// <summary>The item the local player holds, or null.</summary>
        public static NetworkPhysicsItem HeldByLocal()
        {
            foreach (NetworkPhysicsItem item in All)
            {
                if (item.HeldByLocalClient) { return item; }
            }
            return null;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _originalLayer = gameObject.layer;
            if (_kind == "can") { SettleLikeACan(); }
        }

        private bool _ghostRegrabbed;
        private static PhysicsMaterial _canMaterial;

        private void SettleLikeACan()
        {
            NetTuning t = NetTuning.Instance;
            _rb.linearDamping = t.CanLinearDamping;
            _rb.angularDamping = t.CanAngularDamping;
            if (_canMaterial == null)
            {
                _canMaterial = new PhysicsMaterial("CanNoBounce")
                {
                    bounciness = 0f, dynamicFriction = t.CanFriction, staticFriction = t.CanFriction,
                    bounceCombine = PhysicsMaterialCombine.Minimum, frictionCombine = PhysicsMaterialCombine.Maximum,
                };
            }
            var box = GetComponent<BoxCollider>();
            if (box != null) { box.sharedMaterial = _canMaterial; }
        }

        /// <summary>Server: call right after Spawn().</summary>
        public void ServerInit(int index)
        {
            _index.Value = index;
        }

        public void ServerSetLocked(bool locked)
        {
            _locked.Value = locked;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsServer) { _byObjectId[NetworkObjectId] = this; _homePosition = transform.position; _homeRotation = transform.rotation; }   // host-side lookup only
            _rb.isKinematic = !IsServer;
            _rb.interpolation = IsServer ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            _lastNetPos = transform.position;
            if (!IsServer)
            {
                _holder.OnValueChanged += OnHolderChanged;
                if (HeldByLocalClient) { BeginHeldCopy(); }
            }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (IsServer) { _byObjectId.Remove(NetworkObjectId); }
            if (IsServer && IsHeld) { Arbiter.Release(NetworkObjectId, (int)_holder.Value); }
            DestroyCopy();
        }

        // ---------- host ----------

        public bool HostGrab(int playerId)
        {
            if (!CanBeGrabbed) { return false; }
            if (!NetPlayer.ByClient.TryGetValue((ulong)playerId, out NetPlayer player)) { return false; }
            if (!Arbiter.TryAcquire(NetworkObjectId, playerId)) { return false; }
            if (_springHold && player.HoldBody != null)
            {
                _rb.isKinematic = false;
                _holdJoint = gameObject.AddComponent<ConfigurableJoint>();
                _holdJoint.connectedBody = player.HoldBody;
                _holdJoint.autoConfigureConnectedAnchor = false;
                _holdJoint.anchor = Vector3.zero;
                _holdJoint.connectedAnchor = Vector3.zero;
                var drive = new JointDrive { positionSpring = _holdSpring, positionDamper = _holdDamper, maximumForce = _holdMaxForce };
                _holdJoint.xMotion = _holdJoint.yMotion = _holdJoint.zMotion = ConfigurableJointMotion.Free;
                _holdJoint.xDrive = _holdJoint.yDrive = _holdJoint.zDrive = drive;
            }
            else
            {
                _follow = player.HoldPoint;
                _rb.isKinematic = true;
            }
            SetHolder((ulong)playerId);
            NetMetrics.Count("grab");
            Debug.Log($"[Net] {_kind} {Index} grabbed by player {playerId}");
            return true;
        }

        /// <summary>Release as the holder asked: teleport to the claimed hand pose when the host trusts it, then apply the velocity.</summary>
        public bool HostRelease(int playerId, ReleaseRequest request)
        {
            if (!Arbiter.Release(NetworkObjectId, playerId)) { return false; }
            Detach();
            bool teleported = false;
            if (request.HasPose && NetPlayer.ByClient.TryGetValue((ulong)playerId, out NetPlayer p))
            {
                teleported = ReleasePoseRule.Accept(request.Position, p.HoldPoint.position, NetTuning.Instance.ReleasePoseTolerance);
                NetMetrics.Count(teleported ? "releasePoseAccepted" : "releasePoseRejected");
            }
            _rb.isKinematic = false;
            if (teleported)
            {
                Quaternion level = ReleaseRotation(request.Rotation);
                transform.SetPositionAndRotation(request.Position, level);
                _rb.position = request.Position;
                _rb.rotation = level;
            }
            float maxSpeed = PlayerTuning.Instance.ThrowSpeed * NetTuning.Instance.ThrowSpeedMargin;
            Vector3 v = ReleasePoseRule.ClampVelocity(request.Velocity, maxSpeed);
            _rb.linearVelocity = v;
            _rb.angularVelocity = ReleaseSpin(v);
            if (LaunchArgs.Has("-botlog")) { Debug.Log($"[Net] host release {_kind}: pos {_rb.position} rot {_rb.rotation.eulerAngles} v {v} teleported {teleported} t {Time.realtimeSinceStartup:F2}"); }
            SetHolder(NoHolder);
            return true;
        }

        /// <summary>Host: let go without a pose (disconnect, cable snap, plug-in). The item stays where it is.</summary>
        public void HostForceRelease()
        {
            if (!IsHeld) { return; }
            Arbiter.Release(NetworkObjectId, (int)_holder.Value);
            Detach();
            _rb.isKinematic = false;
            SetHolder(NoHolder);
        }

        /// <summary>The same spin on the host and in the holder's ghost, so both land alike (a random spin made them diverge).</summary>
        private static Vector3 ReleaseSpin(Vector3 velocity)
        {
            // a gentle drop does not tumble (a tipping corner is where host and holder drift apart); a throw spins up
            float k = Mathf.Clamp01((velocity.magnitude - 3f) / 3f);
            return k > 0f ? Vector3.Cross(Vector3.up, velocity).normalized * 2f * k : Vector3.zero;
        }

        /// <summary>Released items start level (yaw only), the same on the host and in the holder's copy.</summary>
        private static Quaternion ReleaseRotation(Quaternion hand)
        {
            return Quaternion.Euler(0f, hand.eulerAngles.y, 0f);
        }

        private void Detach()
        {
            _follow = null;
            if (_holdJoint != null) { Destroy(_holdJoint); _holdJoint = null; }
        }

        public static bool PlayerHoldsAnything(int playerId) => Arbiter.HoldsAnything(playerId);

        /// <summary>The item a player holds (host side), or null.</summary>
        public static NetworkPhysicsItem HeldBy(int playerId)
        {
            foreach (NetworkPhysicsItem item in All)
            {
                if (item.IsServer && item.IsHeld && item._holder.Value == (ulong)playerId) { return item; }
            }
            return null;
        }

        /// <summary>Forget all registry state (a new session starts).</summary>
        private Vector3 _homePosition;
        private Quaternion _homeRotation;

        /// <summary>Host: let go of everything that is held, in place (the street is being reset between shifts).</summary>
        public static void HostReleaseEverything()
        {
            foreach (NetworkPhysicsItem item in new List<NetworkPhysicsItem>(All))
            {
                if (item != null && item.IsServer && item.IsHeld) { item.HostForceRelease(); }
            }
        }

        /// <summary>Host: back to where the item spawned, at rest (reset between shifts). Cables are not items to put back: they are replaced.</summary>
        public void HostResetToHome()
        {
            if (!IsServer) { return; }
            if (IsHeld) { HostForceRelease(); }
            _rb.isKinematic = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = _homePosition;
            _rb.rotation = _homeRotation;
            transform.SetPositionAndRotation(_homePosition, _homeRotation);
        }

        public static void ResetStatics()
        {
            All.Clear();
            _byObjectId.Clear();
            Arbiter.Clear();
        }

        public static void HostDrop(int playerId, ReleaseRequest request)
        {
            NetworkPhysicsItem item = HeldBy(playerId);
            if (item == null) { return; }
            if (item.HostRelease(playerId, request)) { NetMetrics.Count("drop"); }
        }

        public static void HostThrow(int playerId, ReleaseRequest request)
        {
            NetworkPhysicsItem item = HeldBy(playerId);
            if (item == null || request.Velocity.sqrMagnitude < 0.0001f) { return; }
            if (item.HostRelease(playerId, request)) { NetMetrics.Count("throw"); }
        }

        /// <summary>Drop everything a player holds, in place (disconnect or crash). Returns how many items.</summary>
        public static int HostReleaseAllHeldBy(int playerId)
        {
            List<ulong> ids = Arbiter.ReleaseAll(playerId);
            foreach (ulong id in ids)
            {
                if (_byObjectId.TryGetValue(id, out NetworkPhysicsItem item))
                {
                    item.Detach();
                    item._rb.isKinematic = false;
                    item.SetHolder(NoHolder);
                    Debug.Log($"[Net] {item._kind} {item.Index} dropped in place (holder {playerId} gone)");
                    NetMetrics.Count("droppedOnDisconnect");
                }
            }
            return ids.Count;
        }

        private void SetHolder(ulong holder)
        {
            _holder.Value = holder;
            _holderVersion.Value++;
            NetVerifyLog.RecordHolder(Index, _holderVersion.Value, holder);
            // the host's own held item sits right in front of its camera: keep it out of the interaction raycast
            if (IsServer && NetworkManager != null) { SetRaycastable(holder != NetworkManager.LocalClientId); }
        }

        private void FixedUpdate()
        {
            if (IsSpawned && IsServer && _follow != null)
            {
                _rb.MovePosition(_follow.position);
                _rb.MoveRotation(_follow.rotation);
            }
        }

        // ---------- holder's machine: instant local copy in the hand, ghost on release ----------

        private void OnHolderChanged(ulong previous, ulong current)
        {
            ulong local = NetworkManager.LocalClientId;
            if (_ghostActive && current != NoHolder && current != local) { _ghostRegrabbed = true; }
            if (current == local)
            {
                CancelGhost();
                BeginHeldCopy();
            }
            else if (previous == local && !_ghostActive)
            {
                EndHeldCopy();
            }
        }

        private void BeginHeldCopy()
        {
            if (_visualRoot == null || NetPlayer.Local == null) { return; }
            if (_blend != null) { StopCoroutine(_blend); _blend = null; }
            DestroyCopy();
            _heldCopy = Instantiate(_visualRoot.gameObject, NetPlayer.Local.Controller.Hand);
            _heldCopy.name = "HeldCopy";
            // the copy sits where the visual would be if the item's own pivot were in the hand (the pose the host teleports the item to)
            _heldCopy.transform.localPosition = transform.InverseTransformPoint(_visualRoot.position);
            _heldCopy.transform.localRotation = Quaternion.Inverse(transform.rotation) * _visualRoot.rotation;
            _heldSince = Time.realtimeSinceStartup;
            SetNetworkedRenderers(false);
            SetRaycastable(false);
        }

        /// <summary>Holder's machine: where the local copy is right now (while it exists).</summary>
        public bool TryGetCopyPosition(out Vector3 position)
        {
            position = _heldCopy != null ? _heldCopy.transform.position : default;
            return _heldCopy != null;
        }

        private void EndHeldCopy()
        {
            SetRaycastable(true);
            if (_heldCopy == null) { SetNetworkedRenderers(true); return; }
            _blend = StartCoroutine(BlendBack());
        }

        /// <summary>
        /// Called by the local holder's request to drop or throw: the copy leaves the hand at once and keeps simulating
        /// locally with the same velocity the host will use, until the networked item has caught up with it.
        /// </summary>
        public void BeginGhostRelease(Vector3 velocity)
        {
            if (_heldCopy == null || _ghostActive || IsServer) { return; }
            var box = GetComponent<BoxCollider>();
            Transform t = _heldCopy.transform;
            t.SetParent(null, true);
            // level, exactly like the host does when it teleports the item (the copy's pivot is the item's pivot, see BeginHeldCopy)
            Quaternion visualInItem = Quaternion.Inverse(transform.rotation) * _visualRoot.rotation;
            Quaternion handRotation = t.rotation * Quaternion.Inverse(visualInItem);
            t.rotation = ReleaseRotation(handRotation) * visualInItem;
            _ghostBody = _heldCopy.AddComponent<Rigidbody>();
            _ghostBody.mass = _rb.mass;
            _ghostBody.linearDamping = _rb.linearDamping;
            _ghostBody.angularDamping = _rb.angularDamping;
            _ghostBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (box != null)
            {
                // the same box as the host's collider, expressed in the copy's local space (the copy has its own scale and pivot)
                var bc = _heldCopy.AddComponent<BoxCollider>();
                Vector3 ratio = DivideScale(box.transform.lossyScale, _visualRoot.lossyScale);
                bc.size = Vector3.Scale(box.size, ratio);
                bc.center = _visualRoot.InverseTransformPoint(box.transform.TransformPoint(box.center));   // item-relative, so it does not depend on where either object is now
                bc.sharedMaterial = box.sharedMaterial;
                // nor with the networked item it stands for (it is still at the holder's hand, hidden, and would shove the copy away)
                foreach (Collider own in GetComponentsInChildren<Collider>()) { Physics.IgnoreCollision(bc, own); }
                // do not collide with the local player
                if (NetPlayer.Local != null)
                {
                    foreach (Collider c in NetPlayer.Local.GetComponentsInChildren<Collider>()) { Physics.IgnoreCollision(bc, c); }
                }
            }
            if (LaunchArgs.Has("-botlog")) { Debug.Log($"[Net] ghost start {_kind}: pos {t.position} rot {t.rotation.eulerAngles} v {velocity} t {Time.realtimeSinceStartup:F2}"); }
            _ghostBody.linearVelocity = velocity;
            _ghostBody.angularVelocity = ReleaseSpin(velocity);
            _ghostActive = true;
            _ghostRegrabbed = false;
            _ghostStart = Time.realtimeSinceStartup;
            SetRaycastable(true);
        }

        private static Vector3 DivideScale(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x / Mathf.Max(0.0001f, b.x), a.y / Mathf.Max(0.0001f, b.y), a.z / Mathf.Max(0.0001f, b.z));
        }

        private void CancelGhost()
        {
            if (!_ghostActive) { return; }
            _ghostActive = false;
            DestroyCopy();
        }

        private void UpdateGhost()
        {
            float t = Time.realtimeSinceStartup - _ghostStart;
            _netSpeed = Mathf.Lerp(_netSpeed, (transform.position - _lastNetPos).magnitude / Mathf.Max(Time.deltaTime, 0.0001f), 0.3f);
            _lastNetPos = transform.position;
            if (_ghostBody == null) { _ghostActive = false; return; }
            bool confirmed = _holder.Value != NetworkManager.LocalClientId;
            if (!confirmed)
            {
                // the host never released it (it rejected the request): put the copy back in the hand
                if (t > NetTuning.Instance.GhostMaxSeconds + 1f)
                {
                    _ghostActive = false;
                    DestroyCopy();
                    BeginHeldCopy();
                }
                return;
            }
            float d = Vector3.Distance(_heldCopy.transform.position, transform.position);
            bool settled = _ghostBody.linearVelocity.sqrMagnitude < 0.01f && _netSpeed < 0.05f;
            NetTuning tuning = NetTuning.Instance;
            if (t > tuning.GhostMinSeconds && (d < tuning.GhostHandoverDistance || settled || t > tuning.GhostMaxSeconds))
            {
                if (!IsHeld && !_ghostRegrabbed && d < 1.5f)   // somebody grabbing / throwing it again meanwhile is not a prediction error
                {
                    NetMetrics.Sample("releasePopCm", d * 100.0);
                    NetMetrics.Sample("releasePopCm_" + _kind, d * 100.0);
                }
                else
                {
                    NetMetrics.Count("releasePopInterfered");
                }
                Debug.Log($"[Net] release pop {_kind} {d * 100f:F0} cm after {t:F2} s (settled {settled}, confirmed {confirmed})");
                Debug.Log($"[Net] release pop detail {_kind}: ghost {_heldCopy.transform.position} net {transform.position} ghostV {_ghostBody.linearVelocity.magnitude:F2} netSpeed {_netSpeed:F2}");
                _ghostActive = false;
                Destroy(_ghostBody);
                foreach (Collider c in _heldCopy.GetComponents<Collider>()) { Destroy(c); }
                _ghostBody = null;
                _blend = StartCoroutine(BlendBack());
            }
        }

        private IEnumerator BlendBack()
        {
            Transform copy = _heldCopy.transform;
            copy.SetParent(null, true);
            Vector3 startPos = copy.position;
            Quaternion startRot = copy.rotation;
            // a big disagreement slides home at about 2 m/s at most instead of jumping
            float duration = Mathf.Clamp(Vector3.Distance(startPos, transform.position) / 2f, Mathf.Max(0.01f, NetTuning.Instance.HeldBlendSeconds), 0.6f);
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                copy.SetPositionAndRotation(Vector3.Lerp(startPos, transform.position, k), Quaternion.Slerp(startRot, transform.rotation, k));
                yield return null;
            }
            DestroyCopy();
            SetNetworkedRenderers(true);
            _blend = null;
        }

        private void DestroyCopy()
        {
            if (_heldCopy != null) { Destroy(_heldCopy); _heldCopy = null; }
            _ghostBody = null;
        }

        private void SetNetworkedRenderers(bool visible)
        {
            foreach (Renderer r in _renderers)
            {
                if (r != null) { r.enabled = visible; }
            }
        }

        /// <summary>The holder's own held item must not block their interaction raycast (it sits in front of the camera).</summary>
        public void SetRaycastable(bool raycastable)
        {
            int layer = raycastable ? _originalLayer : IgnoreRaycastLayer;
            foreach (Collider c in GetComponentsInChildren<Collider>(true)) { c.gameObject.layer = layer; }
        }

        private void LateUpdate()
        {
            if (_ghostActive) { UpdateGhost(); return; }
            if (_heldCopy == null || _blend != null || NetPlayer.Local == null) { return; }
            // metrics: the visible (local) copy against the hand, and where the networked item is, for comparison
            Vector3 hand = NetPlayer.Local.Controller.Hand.position;
            NetMetrics.Sample("heldVisibleDistCm", Vector3.Distance(_heldCopy.transform.position, hand) * 100.0);
            double lag = Vector3.Distance(transform.position, hand) * 100.0;
            if (LaunchArgs.Has("-botlog") && Time.time > _nextLagLog)
            {
                _nextLagLog = Time.time + 1f;
                Debug.Log($"[Net] held {_kind}: networked {transform.position} hand {hand} lag {lag:F0} cm parent {(transform.parent != null ? transform.parent.name : "none")}");
            }
            NetMetrics.Sample("heldNetworkedLagCm", lag);
            NetMetrics.Sample("heldNetworkedLagCm_" + _kind, lag);
            // the first moments after a grab include the item flying from the ground to the hand; steady = after 0.6 s
            if (Time.realtimeSinceStartup - _heldSince > 0.6f)
            {
                NetMetrics.Sample("heldNetworkedLagSteadyCm", lag);
                NetMetrics.Sample("heldNetworkedLagSteadyCm_" + _kind, lag);
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            DestroyCopy();
        }
    }
}
