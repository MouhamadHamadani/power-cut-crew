using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PowerCutCrew.Net
{
    /// <summary>F1 overlay (Development builds and the Editor only): mode, RTT, kbps and the player list with ping. Dev text, not localized.</summary>
    public class NetDebugOverlay : MonoBehaviour
    {
        private bool _visible;
        private readonly StringBuilder _sb = new StringBuilder();

        private void Update()
        {
            Keyboard k = Keyboard.current;
            if (k != null && k.f1Key.wasPressedThisFrame) { _visible = !_visible; }
            NetSession s = NetSession.Instance;
            if (s != null && s.Manager.IsConnectedClient && !s.Manager.IsServer)
            {
                NetMetrics.RttMs = s.Manager.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
            }
        }

        private void OnGUI()
        {
            if (!_visible) { return; }
            NetSession s = NetSession.Instance;
            _sb.Clear();
            _sb.AppendLine("NET (F1)");
            _sb.AppendLine($"mode {s.Mode}  {(s.IsHost ? "HOST" : (s.Manager.IsClient ? "CLIENT" : "offline"))}  v{s.Version}");
            if (s.Lobby != null && s.Lobby.LobbyId != 0) { _sb.AppendLine($"steam lobby {s.Lobby.LobbyId}"); }
            _sb.AppendLine($"rtt {NetMetrics.RttMs:0} ms   up {NetMetrics.KbpsUp:0.0} kbps   down {NetMetrics.KbpsDown:0.0} kbps");
            foreach (var pair in NetPlayer.ByClient)
            {
                NetPlayer p = pair.Value;
                ulong rtt = 0;
                if (s.IsHost && pair.Key != NetworkManager.ServerClientId) { rtt = s.Manager.NetworkConfig.NetworkTransport.GetCurrentRtt(pair.Key); }
                else if (!s.IsHost && pair.Key == s.Manager.LocalClientId) { rtt = (ulong)NetMetrics.RttMs; }
                _sb.AppendLine($"  player {pair.Key} slot {p.Slot}{(p.IsOwner ? " (you)" : "")}  ping {rtt} ms");
            }
            GUI.Box(new Rect(8, 8, 420, 24 + 18 * (3 + NetPlayer.ByClient.Count)), GUIContent.none);
            GUI.Label(new Rect(14, 10, 410, 24 + 18 * (3 + NetPlayer.ByClient.Count)), _sb.ToString());
        }
    }
}
