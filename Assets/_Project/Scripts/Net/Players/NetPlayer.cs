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

        public int Slot => _slot.Value;
        public int PlayerId => (int)OwnerClientId;

        /// <summary>Host hold point: items are attached here.</summary>
        public Transform HoldPoint => _holdPoint;

        public PlayerController Controller => _controller;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
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
                _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - cam.transform.position);
            }
        }

        // ---------- requests (owner -> host) ----------

        public void RequestInteract(Interactable target)
        {
            NetworkObject no = target.GetComponentInParent<NetworkObject>();
            if (no == null) { return; }
            if (IsServer) { HostInteract(no); } else { InteractRpc(new NetworkObjectReference(no)); }
        }

        public void RequestDrop()
        {
            if (IsServer) { NetworkPhysicsItem.HostDrop(PlayerId); } else { DropRpc(); }
        }

        public void RequestThrow(Vector3 direction)
        {
            if (IsServer) { NetworkPhysicsItem.HostThrow(PlayerId, direction); } else { ThrowRpc(direction); }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void InteractRpc(NetworkObjectReference target)
        {
            if (target.TryGet(out NetworkObject no, NetworkManager)) { HostInteract(no); }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DropRpc()
        {
            NetworkPhysicsItem.HostDrop(PlayerId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ThrowRpc(Vector3 direction)
        {
            NetworkPhysicsItem.HostThrow(PlayerId, direction);
        }

        /// <summary>Bot-run assertion: send what this client sees to the host (see NetVerifyLog).</summary>
        public void SendVerify(int[] items, int[] versions, ulong[] holders, ushort chainSeq, Vector3[] chainPositions)
        {
            if (!IsServer) { VerifyRpc(items, versions, holders, chainSeq, chainPositions); }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void VerifyRpc(int[] items, int[] versions, ulong[] holders, ushort chainSeq, Vector3[] chainPositions)
        {
            SnapshotStream chain = FindFirstObjectByType<SnapshotStream>();
            bool ok = NetVerifyLog.Check(OwnerClientId, items, versions, holders, chainSeq, chainPositions, chain, out string message);
            NetMetrics.Count(ok ? "verifyOk" : "verifyFail");
            Debug.Log($"[Verify] {(ok ? "OK" : "FAIL")} {message}");
        }

        private void HostInteract(NetworkObject target)
        {
            Interactable interactable = target.GetComponentInChildren<Interactable>();
            if (interactable == null) { return; }
            var context = new InteractionContext(PlayerId, transform.position);
            if (interactable.HostTryInteract(context)) { NetMetrics.Count("interact"); }
            else { NetMetrics.Count("interactRejected"); }
        }
    }
}
