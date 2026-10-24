using System.Collections.Generic;
using PowerCutCrew.Core;
using RTLTMPro;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// Networked player: slot (colour), name, owner setup, and the request RPCs to the host
    /// (interact, drop, throw). The host validates everything.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class NetPlayer : NetworkBehaviour
    {
        public static readonly Dictionary<ulong, NetPlayer> ByClient = new Dictionary<ulong, NetPlayer>();
        public static NetPlayer Local { get; private set; }

        [SerializeField] private Transform _holdPoint;
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private RTLTextMeshPro3D _label;
        [SerializeField] private GameObject _ownerCamera;

        private readonly NetworkVariable<int> _slot = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<FixedString64Bytes> _name = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private PlayerController _controller;
        private HumanInput _human;
        private LocalizedString _fallbackName;
        private Vector3 _labelBaseScale;

        public int Slot => _slot.Value;
        public int PlayerId => (int)OwnerClientId;

        /// <summary>Host hold point: items are attached here.</summary>
        public Transform HoldPoint => _holdPoint;

        public PlayerController Controller => _controller;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _labelBaseScale = _label.transform.localScale;
            _fallbackName = new LocalizedString("UI", "net.player_name");
            _fallbackName.StringChanged += s => { if (_name.Value.Length == 0) { _label.text = s; } };
        }

        /// <summary>Server: call right after Spawn(). The spawn position itself comes from the instantiate position.</summary>
        public void ServerSetSlot(int slot)
        {
            _slot.Value = slot;
        }

        public override void OnNetworkSpawn()
        {
            ByClient[OwnerClientId] = this;
            if (IsServer)
            {
                var hb = new GameObject($"HoldBody{OwnerClientId}");
                _holdBody = hb.AddComponent<Rigidbody>();
                _holdBody.isKinematic = true;
            }
            PlayerTuning t = PlayerTuning.Instance;
            _holdPoint.localPosition = new Vector3(0f, t.EyeHeightStanding - t.HoldBelowEyes, t.HoldForward);

            _slot.OnValueChanged += (p, n) => ApplyLook();
            _name.OnValueChanged += (p, n) => ApplyLook();
            ApplyLook();

            _controller.SetOwner(IsOwner, null);
            _ownerCamera.SetActive(false);
            _bodyRenderer.enabled = true;
            if (IsOwner)
            {
                Local = this;
                bool hasGraphics = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
                IPlayerInput input;
                if (LaunchArgs.Bot)
                {
                    input = new BotBrain(LaunchArgs.Seed * 1000 + (int)OwnerClientId);
                }
                else
                {
                    _human = new HumanInput();
                    input = _human;
                }
                _controller.SetOwner(true, input);
                _ownerCamera.SetActive(hasGraphics);
                _bodyRenderer.enabled = !hasGraphics;   // first person: you do not see your own capsule
                if (NetSession.Instance != null && NetSession.Instance.Mode == ConnectionMode.Steam)
                {
                    _name.Value = new FixedString64Bytes(NetSession.Instance.LocalPlayerName);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            ByClient.Remove(OwnerClientId);
            if (Local == this) { Local = null; }
            if (_human != null) { _human.Dispose(); _human = null; }
            if (IsServer)
            {
                // a player that leaves or crashes drops everything they held, in place
                int dropped = NetworkPhysicsItem.HostReleaseAllHeldBy(PlayerId);
                if (dropped > 0) { Debug.Log($"[Net] player {PlayerId} left: dropped {dropped} item(s) in place"); }
                if (HoldRegistry.Instance != null) { HoldRegistry.Instance.EndAllFor(PlayerId); }
                if (_holdBody != null) { Destroy(_holdBody.gameObject); }
            }
        }

        private void ApplyLook()
        {
            int slot = Mathf.Max(0, _slot.Value);
            Color color = Color.HSVToRGB((slot * 0.23f + 0.05f) % 1f, 0.75f, 0.95f);
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            _bodyRenderer.SetPropertyBlock(block);
            _label.color = color;
            if (_name.Value.Length > 0)
            {
                _label.text = _name.Value.ToString();
            }
            else
            {
                // "Player N", localized (the string changes also when the language is switched)
                _fallbackName.Arguments = new object[] { slot + 1 };
                _fallbackName.RefreshString();
            }
        }

        private void LateUpdate()
        {
            if (IsOwner) { return; }
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 toLabel = _label.transform.position - cam.transform.position;
                _label.transform.rotation = Quaternion.LookRotation(toLabel);
                PlayerTuning t = PlayerTuning.Instance;
                // the tag keeps a readable size on screen: not huge next to the camera, not tiny far away (the parent's scale is 1)
                _label.transform.localScale = _labelBaseScale * NameTagScale.For(toLabel.magnitude, t.NameTagReferenceDistance, t.NameTagMinScreenFactor, t.NameTagMaxScreenFactor);
            }
        }

        // ---------- requests (owner -> host) ----------

        public const byte HoldBegin = 0;
        public const byte HoldKeepAlive = 1;
        public const byte HoldEnd = 2;

        private Rigidbody _holdBody;

        /// <summary>Host: kinematic body that spring-held items (the cable plug) are pulled towards.</summary>
        public Rigidbody HoldBody => _holdBody;

        private static byte IndexOf(NetworkObject no, Interactable target)
        {
            Interactable[] all = no.GetComponentsInChildren<Interactable>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == target) { return (byte)i; }
            }
            return 0;
        }

        public void RequestInteract(Interactable target)
        {
            NetworkObject no = target.GetComponentInParent<NetworkObject>();
            if (no == null) { return; }
            byte index = IndexOf(no, target);
            Transform hand = _controller.Hand;
            if (IsServer) { HostInteract(no, index, hand.position, hand.rotation); }
            else { InteractRpc(new NetworkObjectReference(no), index, hand.position, hand.rotation); }
        }

        /// <summary>Begin, keep alive or end a "hold E" interaction (pour, fill).</summary>
        public void RequestHold(Interactable target, byte phase)
        {
            NetworkObject no = target.GetComponentInParent<NetworkObject>();
            if (no == null) { return; }
            byte index = IndexOf(no, target);
            Transform hand = _controller.Hand;
            if (IsServer) { HostHold(no, index, phase, hand.position, hand.rotation); }
            else { HoldRpc(new NetworkObjectReference(no), index, phase, hand.position, hand.rotation); }
        }

        /// <summary>Use the held item (the wrench swing): the request names the item, what was hit and where; the host checks who holds what.</summary>
        public void RequestUse(NetworkPhysicsItem item, Component target, Vector3 point)
        {
            NetworkObject targetObject = target != null ? target.GetComponentInParent<NetworkObject>() : null;
            if (IsServer) { HostUse(item.NetworkObject, targetObject, target, point); return; }
            UseRpc(new NetworkObjectReference(item.NetworkObject), targetObject != null ? new NetworkObjectReference(targetObject) : default, point);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void UseRpc(NetworkObjectReference item, NetworkObjectReference target, Vector3 point)
        {
            if (!item.TryGet(out NetworkObject itemObject, NetworkManager)) { return; }
            NetworkObject targetObject = null;
            target.TryGet(out targetObject, NetworkManager);
            HostUse(itemObject, targetObject, targetObject != null ? targetObject.transform : null, point);
        }

        private void HostUse(NetworkObject itemObject, NetworkObject targetObject, Component target, Vector3 point)
        {
            var item = itemObject.GetComponent<NetworkPhysicsItem>();
            var usable = itemObject.GetComponent<IUsableItem>();
            // only the holder can use it
            if (item == null || usable == null || !item.IsHeld || item.HolderClientId != OwnerClientId) { NetMetrics.Count("useRejected"); return; }
            usable.HostUse(PlayerId, target, point);
        }

        public void RequestDrop()
        {
            NetworkPhysicsItem held = NetworkPhysicsItem.HeldByLocal();
            IUsableItem usable = held != null ? held.GetComponent<IUsableItem>() : null;
            Vector3 velocity = transform.forward * (usable != null ? usable.DropSpeed : PlayerTuning.Instance.DropForwardSpeed);
            SendRelease(velocity, false);
        }

        public void RequestThrow(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.0001f) { return; }
            SendRelease(direction.normalized * PlayerTuning.Instance.ThrowSpeed, true);
        }

        private void SendRelease(Vector3 velocity, bool isThrow)
        {
            Transform hand = _controller.Hand;
            if (IsServer)
            {
                HostRelease(hand.position, hand.rotation, velocity, isThrow);
                return;
            }
            // the holder's copy leaves the hand right now and keeps simulating locally (see NetworkPhysicsItem.BeginGhostRelease)
            NetworkPhysicsItem.HeldByLocal()?.BeginGhostRelease(velocity);
            ReleaseRpc(hand.position, hand.rotation, velocity, isThrow);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void InteractRpc(NetworkObjectReference target, byte index, Vector3 handPosition, Quaternion handRotation)
        {
            if (target.TryGet(out NetworkObject no, NetworkManager)) { HostInteract(no, index, handPosition, handRotation); }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void HoldRpc(NetworkObjectReference target, byte index, byte phase, Vector3 handPosition, Quaternion handRotation)
        {
            if (target.TryGet(out NetworkObject no, NetworkManager)) { HostHold(no, index, phase, handPosition, handRotation); }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ReleaseRpc(Vector3 handPosition, Quaternion handRotation, Vector3 velocity, bool isThrow)
        {
            HostRelease(handPosition, handRotation, velocity, isThrow);
        }

        private void HostRelease(Vector3 handPosition, Quaternion handRotation, Vector3 velocity, bool isThrow)
        {
            var request = new ReleaseRequest { HasPose = true, Position = handPosition, Rotation = handRotation, Velocity = velocity };
            if (isThrow) { NetworkPhysicsItem.HostThrow(PlayerId, request); }
            else { NetworkPhysicsItem.HostDrop(PlayerId, request); }
        }

        /// <summary>Bot-run assertion: send what this client sees to the host (see NetVerifyLog).</summary>
        public void SendVerify(int[] items, int[] versions, ulong[] holders, ushort chainSeq, Vector3[] chainPositions)
        {
            if (!IsServer) { VerifyRpc(items, versions, holders, chainSeq, chainPositions); }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void VerifyRpc(int[] items, int[] versions, ulong[] holders, ushort chainSeq, Vector3[] chainPositions)
        {
            bool ok = NetVerifyLog.Check(OwnerClientId, items, versions, holders, chainSeq, chainPositions, SnapshotStream.VerifyTarget, out string message);
            NetMetrics.Count(ok ? "verifyOk" : "verifyFail");
            Debug.Log($"[Verify] {(ok ? "OK" : "FAIL")} {message}");
        }

        /// <summary>Bot-run assertion, part two: snapshot streams and the game-specific report (see NetVerifyExtras).</summary>
        public void SendVerifyExtras(string report, ulong[] keys, ushort[] seqs, int[] counts, Vector3[] positions)
        {
            if (!IsServer) { VerifyExtrasRpc(report, keys, seqs, counts, positions); }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void VerifyExtrasRpc(string report, ulong[] keys, ushort[] seqs, int[] counts, Vector3[] positions)
        {
            NetVerifyExtras.Check(OwnerClientId, report, keys, seqs, counts, positions);
        }

        /// <summary>The context for a request: the claimed hand pose is only attached when the host trusts it.</summary>
        private InteractionContext MakeContext(Vector3 handPosition, Quaternion handRotation)
        {
            bool trusted = ReleasePoseRule.Accept(handPosition, _holdPoint.position, NetTuning.Instance.ReleasePoseTolerance);
            return trusted ? new InteractionContext(PlayerId, transform.position, handPosition, handRotation) : new InteractionContext(PlayerId, transform.position);
        }

        private static Interactable Resolve(NetworkObject target, byte index)
        {
            Interactable[] all = target.GetComponentsInChildren<Interactable>(true);
            return index < all.Length ? all[index] : null;
        }

        private void HostInteract(NetworkObject target, byte index, Vector3 handPosition, Quaternion handRotation)
        {
            Interactable interactable = Resolve(target, index);
            if (interactable == null) { return; }
            if (interactable.HostTryInteract(MakeContext(handPosition, handRotation))) { NetMetrics.Count("interact"); }
            else { NetMetrics.Count("interactRejected"); }
        }

        private void HostHold(NetworkObject target, byte index, byte phase, Vector3 handPosition, Quaternion handRotation)
        {
            Interactable interactable = Resolve(target, index);
            if (interactable == null || HoldRegistry.Instance == null) { return; }
            ulong key = target.NetworkObjectId * 32UL + index;
            HoldRegistry.Instance.Handle(this, interactable, key, phase, MakeContext(handPosition, handRotation));
        }

        private void FixedUpdate()
        {
            if (_holdBody != null) { _holdBody.MovePosition(_holdPoint.position); }
        }
    }
}
