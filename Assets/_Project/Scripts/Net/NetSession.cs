using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Netcode.Transports;
using PowerCutCrew.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
#endif

namespace PowerCutCrew.Net
{
    public enum ConnectionMode
    {
        Local,
        Steam,
    }

    /// <summary>
    /// Sessions over local UDP (Unity Transport) or Steam (SteamNetworkingSockets). One NetworkManager object carries both
    /// transports; the active one is chosen before starting. Host-authoritative: the host owns the world, approves
    /// connections, loads the game scene (clients follow) and spawns players and the world.
    /// </summary>
    public class NetSession : MonoBehaviour
    {
        public static NetSession Instance { get; private set; }

        /// <summary>Menu message (UI string key) to show after returning to the main menu, then cleared.</summary>
        public static string PendingMenuMessageKey { get; set; }

        /// <summary>Other assemblies (generator, cables) register their network prefabs here; raised on every peer when the session object is created.</summary>
        public static event Action<NetworkManager> RegisterPrefabs;

        /// <summary>Host: raised after the base world (cans, chain) is spawned, so other assemblies can spawn their own world objects.</summary>
        public static event Action<WorldSpawner> WorldSpawning;

        [SerializeField] private NetworkManager _nm;
        [SerializeField] private MeteredUnityTransport _udp;
        [SerializeField] private SteamNetworkingSocketsTransport _steam;
        [SerializeField] private NetPrefabCatalog _catalog;

        private SteamLobbyService _lobby;
        private readonly Dictionary<ulong, int> _slots = new Dictionary<ulong, int>();
        private bool _worldSpawned;
        private bool _wasConnected;
        private bool _selfShutdown;
        private bool _netSimAdded;

        public ConnectionMode Mode { get; private set; }
        public bool IsHost => _nm != null && _nm.IsServer;
        public bool IsRunning => _nm != null && (_nm.IsServer || _nm.IsClient);
        public string LocalPlayerName => _lobby != null ? _lobby.PersonaName : string.Empty;
        public SteamLobbyService Lobby => _lobby;
        public NetworkManager Manager => _nm;
        public string Version => Application.version;

        /// <summary>A localized error to show (UI string key).</summary>
        public event Action<string> ErrorRaised;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            NetMetrics.Ensure();
            _lobby = gameObject.AddComponent<SteamLobbyService>();
            _lobby.JoinRequested += OnSteamJoinRequested;
            _nm.NetworkPrefabs_Add(_catalog);
            RegisterPrefabs?.Invoke(_nm);
            LocalHolding.Provider = () => NetworkPhysicsItem.HeldByLocal();
            gameObject.AddComponent<HoldRegistry>();
            _nm.ConnectionApprovalCallback = OnApproval;
            _nm.OnServerStarted += OnServerStarted;
            _nm.OnServerStopped += OnServerStopped;
            _nm.OnClientConnectedCallback += OnClientConnected;
            _nm.OnClientDisconnectCallback += OnClientDisconnected;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            gameObject.AddComponent<NetDebugOverlay>();
#endif
            gameObject.AddComponent<NetVerifier>();
            SceneManager.sceneLoaded += (scene, mode) => Debug.Log($"[Net] scene loaded: {scene.name}");
        }

        private void OnDestroy()
        {
            if (Instance == this) { Instance = null; }
        }

        // ---------- public API ----------

        public void StartHost(ConnectionMode mode)
        {
            if (IsRunning) { return; }
            Mode = mode;
            if (mode == ConnectionMode.Steam)
            {
                if (!_lobby.Init()) { Raise("net.error.steam_unavailable"); return; }
                _lobby.CreateLobby(NetTuning.Instance.MaxPlayers, Version, true, ok =>
                {
                    if (!ok) { Raise("net.error.steam_lobby"); return; }
                    BeginHost();
                });
                return;
            }
            BeginHost();
        }

        public void StartClient(ConnectionMode mode, string address)
        {
            if (IsRunning) { return; }
            Mode = mode;
            if (mode == ConnectionMode.Steam)
            {
                if (!_lobby.Init()) { Raise("net.error.steam_unavailable"); return; }
                if (!ulong.TryParse(address, out ulong lobbyId)) { Raise("net.error.connect_failed"); return; }
                _lobby.JoinLobby(lobbyId, ok =>
                {
                    if (!ok) { Raise(_lobby.JoinFailedFull ? ConnectionRules.ReasonFull : "net.error.connect_failed"); return; }
                    string hostVersion = _lobby.GetLobbyData(SteamLobbyService.VersionKey);
                    if (!string.IsNullOrEmpty(hostVersion) && hostVersion != Version) { _lobby.Leave(); Raise(ConnectionRules.ReasonVersion); return; }
                    _steam.ConnectToSteamID = _lobby.HostSteamId;
                    BeginClient();
                });
                return;
            }
            _udp.SetConnectionData(address, NetTuning.Instance.Port);
            BeginClient();
        }

        public void Shutdown()
        {
            _selfShutdown = true;
            if (_nm.IsListening) { _nm.Shutdown(); }
            _lobby.Leave();
            _worldSpawned = false;
            _slots.Clear();
            NetworkPhysicsItem.ResetStatics();
            NetVerifyLog.Reset();
        }

        // ---------- start ----------

        private void PrepareTransport()
        {
            NetTuning t = NetTuning.Instance;
            _nm.NetworkConfig.NetworkTransport = Mode == ConnectionMode.Steam ? (NetworkTransport)_steam : _udp;
            _nm.NetworkConfig.TickRate = t.TickRate;
            _nm.NetworkConfig.ConnectionApproval = true;
            _nm.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(Version);
            _udp.DisconnectTimeoutMS = (int)(t.DisconnectTimeoutSeconds * 1000f);
            _udp.ConnectTimeoutMS = 1000;
            _udp.MaxConnectAttempts = Mathf.Max(1, Mathf.RoundToInt(t.ConnectTimeoutSeconds));
            _selfShutdown = false;
            _wasConnected = false;
            if (Mode == ConnectionMode.Local) { ApplyNetSim(LaunchArgs.NetSim); }
        }

        private void BeginHost()
        {
            PrepareTransport();
            if (Mode == ConnectionMode.Local) { _udp.SetConnectionData("127.0.0.1", NetTuning.Instance.Port, "0.0.0.0"); }
            NetworkPhysicsItem.ResetStatics();
            NetVerifyLog.Reset();
            if (!_nm.StartHost()) { Raise("net.error.connect_failed"); }
        }

        private void BeginClient()
        {
            PrepareTransport();
            NetworkPhysicsItem.ResetStatics();
            if (!_nm.StartClient()) { Raise("net.error.connect_failed"); return; }
            StartCoroutine(ConnectTimeout());
        }

        private IEnumerator ConnectTimeout()
        {
            float end = Time.realtimeSinceStartup + NetTuning.Instance.ConnectTimeoutSeconds + 3f;
            while (!_wasConnected && Time.realtimeSinceStartup < end && _nm.IsClient && !_selfShutdown) { yield return null; }
            if (!_wasConnected && !_selfShutdown && _nm.IsListening)
            {
                Shutdown();
                Raise("net.error.connect_failed");
            }
        }

        private void ApplyNetSim(string profile)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            NetTuning t = NetTuning.Instance;
            if (_netSimAdded || !t.TryGetProfile(profile, out NetSimProfile p)) { return; }
            _netSimAdded = true;
            float k = t.NetSimCalibration;
            var sim = gameObject.AddComponent<NetworkSimulator>();
            sim.ConnectionPreset = NetworkSimulatorPreset.Create(p.Name, "", Mathf.RoundToInt(p.DelayMs * k), Mathf.RoundToInt(p.JitterMs * k), 0, p.LossPercent);
#endif
        }

        // ---------- approval ----------

        private void OnApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            if (request.ClientNetworkId == NetworkManager.ServerClientId)
            {
                response.Approved = true;
                return;
            }
            string clientVersion = Encoding.UTF8.GetString(request.Payload ?? new byte[0]);
            ConnectionDecision d = ConnectionRules.Evaluate(_nm.ConnectedClientsIds.Count, NetTuning.Instance.MaxPlayers, Version, clientVersion);
            response.Approved = d.Approved;
            response.Reason = d.ReasonKey;
            if (!d.Approved) { NetMetrics.Count("connectionRejected"); Debug.Log($"[Net] connection rejected: {d.ReasonKey}"); }
        }

        // ---------- server: world and players ----------

        private void OnServerStarted()
        {
            _nm.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            _nm.SceneManager.OnSynchronizeComplete += OnSynchronizeComplete;
            _nm.SceneManager.LoadScene(NetTuning.Instance.GameScene, LoadSceneMode.Single);
        }

        private void OnServerStopped(bool wasHost)
        {
            _worldSpawned = false;
            _slots.Clear();
        }

        private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            if (sceneName != NetTuning.Instance.GameScene) { return; }
            if (!_worldSpawned) { SpawnWorld(); }
            foreach (ulong id in completed) { SpawnPlayer(id); }
        }

        private void OnSynchronizeComplete(ulong clientId)
        {
            if (_worldSpawned) { SpawnPlayer(clientId); }
        }

        private void SpawnWorld()
        {
            WorldAnchors anchors = WorldAnchors.Instance;
            if (anchors == null) { Debug.LogError("[Net] game scene has no WorldAnchors"); return; }
            _worldSpawned = true;
            Transform[] spawns = anchors.CanSpawns;
            for (int i = 0; i < spawns.Length; i++)
            {
                NetworkObject can = Instantiate(_catalog.Can, spawns[i].position, spawns[i].rotation);
                can.Spawn();
                can.GetComponent<NetworkPhysicsItem>().ServerInit(i);
            }
            if (_catalog.Chain != null && anchors.Chain != null)
            {
                NetworkObject chain = Instantiate(_catalog.Chain, anchors.Chain.position, anchors.Chain.rotation);
                chain.Spawn();
            }
            WorldSpawning?.Invoke(new WorldSpawner());
        }

        private void SpawnPlayer(ulong clientId)
        {
            if (_slots.ContainsKey(clientId) || WorldAnchors.Instance == null) { return; }
            int slot = 0;
            while (_slots.ContainsValue(slot)) { slot++; }
            _slots[clientId] = slot;
            Transform spawn = WorldAnchors.Instance.GetPlayerSpawn(slot);
            NetworkObject player = Instantiate(_catalog.Player, spawn.position, spawn.rotation);
            player.SpawnAsPlayerObject(clientId);
            player.GetComponent<NetPlayer>().ServerSetSlot(slot);
            Debug.Log($"[Net] player spawned: client {clientId} slot {slot}");
        }

        // ---------- connection events ----------

        private void OnClientConnected(ulong clientId)
        {
            if (clientId == _nm.LocalClientId) { _wasConnected = true; }
            if (_nm.IsServer) { Debug.Log($"[Net] client {clientId} connected ({_nm.ConnectedClientsIds.Count}/{NetTuning.Instance.MaxPlayers})"); }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (_nm.IsServer && clientId != NetworkManager.ServerClientId)
            {
                _slots.Remove(clientId);
                Debug.Log($"[Net] client {clientId} disconnected");
                return;
            }
            if (_nm.IsServer || clientId != _nm.LocalClientId || _selfShutdown) { return; }

            // we are a client and lost (or never got) our connection
            string reason = _nm.DisconnectReason;
            if (!string.IsNullOrEmpty(reason) && reason.StartsWith("net.error."))
            {
                Debug.Log($"[Net] rejected by host: {reason}");
                Shutdown();
                Raise(reason);
            }
            else if (_wasConnected)
            {
                Debug.Log("[Net] host left -> returning to the main menu");
                NetMetrics.Count("hostLeft");
                ReturnToMenu("net.error.host_left");
            }
            else
            {
                // never connected: either the host rejected us (the reason message can land just after the disconnect event) or it is unreachable
                StartCoroutine(ReportFailedConnect());
            }
        }

        private IEnumerator ReportFailedConnect()
        {
            yield return new WaitForSecondsRealtime(0.5f);
            string reason = _nm.DisconnectReason;
            Debug.Log($"[Net] connect failed, disconnect reason '{reason}'");
            Shutdown();
            Raise(!string.IsNullOrEmpty(reason) && reason.StartsWith("net.error.") ? reason : "net.error.connect_failed");
        }

        private void ReturnToMenu(string messageKey)
        {
            PendingMenuMessageKey = messageKey;
            Shutdown();
            SceneManager.LoadScene(NetTuning.Instance.MenuScene);
        }

        private void OnSteamJoinRequested(ulong lobbyId)
        {
            Debug.Log($"[Steam] join requested for lobby {lobbyId}");
            if (IsRunning) { return; }
            StartClient(ConnectionMode.Steam, lobbyId.ToString());
        }

        private void Raise(string key)
        {
            Debug.LogWarning($"[Net] error: {key}");
            ErrorRaised?.Invoke(key);
        }

        /// <summary>Start from launch arguments (bot runs, Steam invites).</summary>
        public bool TryAutoStart()
        {
            if (LaunchArgs.Host) { StartHost(ConnectionMode.Local); return true; }
            if (LaunchArgs.Client) { StartClient(ConnectionMode.Local, LaunchArgs.Get("-client", "127.0.0.1")); return true; }
            if (LaunchArgs.SteamHost) { StartHost(ConnectionMode.Steam); return true; }
            if (LaunchArgs.SteamClient) { StartClient(ConnectionMode.Steam, LaunchArgs.Get("-steamclient", "0")); return true; }
            string invite = LaunchArgs.Get("+connect_lobby");
            if (!string.IsNullOrEmpty(invite)) { StartClient(ConnectionMode.Steam, invite); return true; }
            return false;
        }
    }

    /// <summary>Handed to world-spawn hooks: spawn a network prefab on the host.</summary>
    public class WorldSpawner
    {
        public NetworkObject Spawn(NetworkObject prefab, Vector3 position, Quaternion rotation)
        {
            NetworkObject o = UnityEngine.Object.Instantiate(prefab, position, rotation);
            o.Spawn();
            return o;
        }
    }

    internal static class NetworkManagerExtensions
    {
        /// <summary>Register every prefab of the catalog with the NetworkManager.</summary>
        public static void NetworkPrefabs_Add(this NetworkManager nm, NetPrefabCatalog catalog)
        {
            nm.AddNetworkPrefab(catalog.Player.gameObject);
            nm.AddNetworkPrefab(catalog.Can.gameObject);
            if (catalog.Chain != null) { nm.AddNetworkPrefab(catalog.Chain.gameObject); }
        }
    }
}
