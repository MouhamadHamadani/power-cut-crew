using System.Collections;
using System.Collections.Generic;
using PowerCutCrew.Core;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// A grabbable physics object. The host simulates the Rigidbody; clients show the interpolated NetworkTransform.
    /// The holder sees a local copy attached to the hand so the held item never trails the camera; everybody else
    /// sees the networked item. The host owns the real item.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class NetworkPhysicsItem : NetworkBehaviour
    {
        public const ulong NoHolder = ulong.MaxValue;

        /// <summary>Host-side arbitration of who holds what (player id = client id).</summary>
        public static readonly InteractionArbiter Arbiter = new InteractionArbiter();

        public static readonly List<NetworkPhysicsItem> All = new List<NetworkPhysicsItem>();
        private static readonly Dictionary<ulong, NetworkPhysicsItem> _byObjectId = new Dictionary<ulong, NetworkPhysicsItem>();

        [SerializeField] private Transform _visualRoot;
        [SerializeField] private Renderer[] _renderers;

        private readonly NetworkVariable<ulong> _holder = new NetworkVariable<ulong>(NoHolder);
        private readonly NetworkVariable<int> _holderVersion = new NetworkVariable<int>(0);
        private readonly NetworkVariable<int> _index = new NetworkVariable<int>(-1);

        private Rigidbody _rb;
        private Transform _follow;
        private Vector3 _home;
        private GameObject _heldCopy;
        private Coroutine _blend;
        private float _heldSince;

        public ulong HolderClientId => _holder.Value;
        public bool IsHeld => _holder.Value != NoHolder;
        public int HolderVersion => _holderVersion.Value;
        public int Index => _index.Value;
        public Rigidbody Body => _rb;

        public bool HeldByLocalClient => NetworkManager != null && IsHeld && _holder.Value == NetworkManager.LocalClientId;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        /// <summary>Server: call right after Spawn().</summary>
        public void ServerInit(int index, Vector3 home)
        {
            _home = home;
            _index.Value = index;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsServer) { _byObjectId[NetworkObjectId] = this; }   // host-side lookup only
            _rb.isKinematic = !IsServer;
            _rb.interpolation = IsServer ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
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
            if (!NetPlayer.ByClient.TryGetValue((ulong)playerId, out NetPlayer player)) { return false; }
            if (!Arbiter.TryAcquire(NetworkObjectId, playerId)) { return false; }
            _follow = player.HoldPoint;
            _rb.isKinematic = true;
            SetHolder((ulong)playerId);
            NetMetrics.Count("grab");
            Debug.Log($"[Net] item {Index} grabbed by player {playerId}");
            return true;
        }

        public void HostRelease(int playerId, Vector3 velocity)
        {
            if (!Arbiter.Release(NetworkObjectId, playerId)) { return; }
            _follow = null;
            _rb.isKinematic = false;
            _rb.linearVelocity = velocity;
            _rb.angularVelocity = velocity.sqrMagnitude > 0.01f ? Random.insideUnitSphere * 3f : Vector3.zero;
            SetHolder(NoHolder);
        }

        public static bool PlayerHoldsAnything(int playerId) => Arbiter.HoldsAnything(playerId);

        /// <summary>Forget all registry state (a new session starts).</summary>
        public static void ResetStatics()
        {
            All.Clear();
            _byObjectId.Clear();
            Arbiter.Clear();
        }

        public static void HostDrop(int playerId)
        {
            NetworkPhysicsItem item = HeldBy(playerId);
            if (item == null) { return; }
            Vector3 forward = NetPlayer.ByClient.TryGetValue((ulong)playerId, out NetPlayer p) ? p.transform.forward : Vector3.zero;
            item.HostRelease(playerId, forward * PlayerTuning.Instance.DropForwardSpeed);
            NetMetrics.Count("drop");
        }

        public static void HostThrow(int playerId, Vector3 direction)
        {
            NetworkPhysicsItem item = HeldBy(playerId);
            if (item == null || direction.sqrMagnitude < 0.0001f) { return; }
            item.HostRelease(playerId, direction.normalized * PlayerTuning.Instance.ThrowSpeed);
            NetMetrics.Count("throw");
        }

        /// <summary>Drop everything a player holds, in place (disconnect or crash). Returns how many items.</summary>
        public static int HostReleaseAllHeldBy(int playerId)
        {
            List<ulong> ids = Arbiter.ReleaseAll(playerId);
            foreach (ulong id in ids)
            {
                if (_byObjectId.TryGetValue(id, out NetworkPhysicsItem item))
                {
                    item._follow = null;
                    item._rb.isKinematic = false;
                    item.SetHolder(NoHolder);
                    Debug.Log($"[Net] item {item.Index} dropped in place (holder {playerId} gone)");
                    NetMetrics.Count("droppedOnDisconnect");
                }
            }
            return ids.Count;
        }

        private static NetworkPhysicsItem HeldBy(int playerId)
        {
            foreach (NetworkPhysicsItem item in All)
            {
                if (item.IsServer && item.IsHeld && item._holder.Value == (ulong)playerId) { return item; }
            }
            return null;
        }

        private void SetHolder(ulong holder)
        {
            _holder.Value = holder;
            _holderVersion.Value++;
            NetVerifyLog.RecordHolder(Index, _holderVersion.Value, holder);
        }

        private void FixedUpdate()
        {
            if (IsSpawned && IsServer && _follow != null)
            {
                _rb.MovePosition(_follow.position);
                _rb.MoveRotation(_follow.rotation);
            }
        }

        // ---------- holder's machine: instant local copy in the hand ----------

        private void OnHolderChanged(ulong previous, ulong current)
        {
            ulong local = NetworkManager.LocalClientId;
            if (current == local) { BeginHeldCopy(); }
            else if (previous == local) { EndHeldCopy(); }
        }

        private void BeginHeldCopy()
        {
            if (_visualRoot == null || NetPlayer.Local == null) { return; }
            if (_blend != null) { StopCoroutine(_blend); _blend = null; }
            DestroyCopy();
            _heldCopy = Instantiate(_visualRoot.gameObject, NetPlayer.Local.Controller.Hand);
            _heldCopy.name = "HeldCopy";
            _heldSince = Time.realtimeSinceStartup;
            _heldCopy.transform.localPosition = Vector3.zero;
            _heldCopy.transform.localRotation = Quaternion.identity;
            SetNetworkedRenderers(false);
        }

        private void EndHeldCopy()
        {
            if (_heldCopy == null) { SetNetworkedRenderers(true); return; }
            _blend = StartCoroutine(BlendBack());
        }

        private IEnumerator BlendBack()
        {
            Transform copy = _heldCopy.transform;
            copy.SetParent(null, true);
            Vector3 startPos = copy.position;
            Quaternion startRot = copy.rotation;
            float duration = Mathf.Max(0.01f, NetTuning.Instance.HeldBlendSeconds);
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
        }

        private void SetNetworkedRenderers(bool visible)
        {
            foreach (Renderer r in _renderers)
            {
                if (r != null) { r.enabled = visible; }
            }
        }

        private void LateUpdate()
        {
            if (_heldCopy == null || _blend != null || NetPlayer.Local == null) { return; }
            // metrics: the visible (local) copy against the hand, and where the networked item is, for comparison
            Vector3 hand = NetPlayer.Local.Controller.Hand.position;
            NetMetrics.Sample("heldVisibleDistCm", Vector3.Distance(_heldCopy.transform.position, hand) * 100.0);
            double lag = Vector3.Distance(transform.position, hand) * 100.0;
            NetMetrics.Sample("heldNetworkedLagCm", lag);
            // the first moments after a grab include the item flying from the ground to the hand; steady = after 0.6 s
            if (Time.realtimeSinceStartup - _heldSince > 0.6f) { NetMetrics.Sample("heldNetworkedLagSteadyCm", lag); }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            DestroyCopy();
        }
    }
}
