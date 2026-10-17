#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using PowerCutCrew.Buildings;
using PowerCutCrew.Cables;
using PowerCutCrew.Core;
using PowerCutCrew.Fuel;
using PowerCutCrew.Generator;
using PowerCutCrew.Net;
using PowerCutCrew.Shift;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerCutCrew.Tests.PlayMode
{
    /// <summary>In-process host + two clients playing the core loop: pour, pull the cord, plug in, snap a cable, throw from the hand.</summary>
    public class NetIntegrationTests : NetcodeIntegrationTest
    {
        private NetPrefabCatalog _net;
        private CoreLoopCatalog _loop;
        private GameObject _ground;
        private GameObject _registry;
        private NetworkObject _hostGenerator;
        private NetworkObject _hostSocket;
        private NetworkObject _hostDrum;
        private BuildingsCatalog _buildings;
        private NetworkObject _hostManager;
        private readonly NetworkObject[] _hostBuildings = new NetworkObject[4];
        private readonly NetworkObject[] _hostSockets = new NetworkObject[4];

        protected override int NumberOfClients => 2;

        private NetworkManager A => m_ClientNetworkManagers[0];
        private NetworkManager B => m_ClientNetworkManagers[1];

        protected override void OnServerAndClientsCreated()
        {
            _net = AssetDatabase.LoadAssetAtPath<NetPrefabCatalog>("Assets/_Project/Config/NetPrefabCatalog.asset");
            _loop = Resources.Load<CoreLoopCatalog>("CoreLoopCatalog");
            _buildings = Resources.Load<BuildingsCatalog>("BuildingsCatalog");
            var nms = new System.Collections.Generic.List<NetworkManager> { m_ServerNetworkManager };
            nms.AddRange(m_ClientNetworkManagers);
            foreach (NetworkManager nm in nms)
            {
                nm.AddNetworkPrefab(_net.Player.gameObject);
                nm.AddNetworkPrefab(_net.Can.gameObject);
                nm.AddNetworkPrefab(_loop.Generator.gameObject);
                nm.AddNetworkPrefab(_loop.Drum.gameObject);
                nm.AddNetworkPrefab(_loop.Socket.gameObject);
                nm.AddNetworkPrefab(_loop.Cable.gameObject);
                nm.AddNetworkPrefab(_buildings.Building.gameObject);
                nm.AddNetworkPrefab(_buildings.ShiftManager.gameObject);
            }
            NetworkPhysicsItem.ResetStatics();
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(200f, 1f, 200f);
            _registry = new GameObject("HoldRegistry");
            _registry.AddComponent<HoldRegistry>();
            CableNet.RespawnRequested += OnRespawn;
        }

        protected override IEnumerator OnTearDown()
        {
            CableNet.RespawnRequested -= OnRespawn;
            SetTuning<BuildingTuning>(null);
            SetTuning<ShiftTuning>(null);
            SetTuning<CableTuning>(null);
            // the world this fixture spawned must not outlive the test (old ropes would collide with the next test's rope)
            if (m_ServerNetworkManager != null && m_ServerNetworkManager.IsListening)
            {
                foreach (NetworkObject spawned in new System.Collections.Generic.List<NetworkObject>(m_ServerNetworkManager.SpawnManager.SpawnedObjectsList))
                {
                    if (spawned != null && spawned.IsSpawned && !spawned.IsPlayerObject) { spawned.Despawn(true); }
                }
            }
            yield return null;
            NetworkPhysicsItem.ResetStatics();
            if (_ground != null) { Object.Destroy(_ground); }
            if (_registry != null) { Object.Destroy(_registry); }
            yield return base.OnTearDown();
            // whatever the network managers left behind (their world objects are not destroyed with them): gone before the next test
            foreach (CableNet c in Object.FindObjectsByType<CableNet>(FindObjectsSortMode.None)) { Object.Destroy(c.gameObject); }
            foreach (GeneratorNet g in Object.FindObjectsByType<GeneratorNet>(FindObjectsSortMode.None)) { Object.Destroy(g.gameObject); }
            foreach (PowerSocket p in Object.FindObjectsByType<PowerSocket>(FindObjectsSortMode.None)) { Object.Destroy(p.gameObject); }
            foreach (FuelDrum d in Object.FindObjectsByType<FuelDrum>(FindObjectsSortMode.None)) { Object.Destroy(d.gameObject); }
            foreach (BuildingNet b in Object.FindObjectsByType<BuildingNet>(FindObjectsSortMode.None)) { Object.Destroy(b.gameObject); }
            foreach (ShiftNet n in Object.FindObjectsByType<ShiftNet>(FindObjectsSortMode.None)) { Object.Destroy(n.gameObject); }
            yield return null;
        }

        // ---------- world ----------

        private NetworkObject SpawnAt(NetworkObject prefab, Vector3 position)
        {
            // explicit manager: the singleton may belong to the real NetSession of an earlier test
            return NetworkObject.InstantiateAndSpawn(prefab.gameObject, m_ServerNetworkManager, position: position, rotation: Quaternion.identity);
        }

        private void OnRespawn(int outlet)
        {
            NetworkObject c = SpawnAt(_loop.Cable, _hostGenerator.GetComponent<GeneratorNet>().Outlets[outlet].position);
            c.GetComponent<CableNet>().HostInit(outlet);
        }

        private IEnumerator SpawnWorld()
        {
            SpawnObject(_net.Player.gameObject, A);
            SpawnObject(_net.Player.gameObject, B);
            _hostGenerator = SpawnAt(_loop.Generator, new Vector3(0f, 0f, 6f));
            _hostDrum = SpawnAt(_loop.Drum, new Vector3(-4f, 0f, 6f));
            _hostSocket = SpawnAt(_loop.Socket, new Vector3(5f, 1f, 8f));
            _hostSocket.GetComponent<PowerSocket>().ServerInit(0, 5f);
            GeneratorNet gen = _hostGenerator.GetComponent<GeneratorNet>();
            for (int i = 0; i < gen.Outlets.Length; i++) { OnRespawn(i); }

            yield return WaitForConditionOrTimeOut(() => NetPlayer.ByClient.ContainsKey(A.LocalClientId) && NetPlayer.ByClient.ContainsKey(B.LocalClientId)
                && Mirror(A, _hostGenerator) != null && Mirror(B, _hostGenerator) != null && Mirror(B, _hostSocket) != null);
            AssertOnTimeout("world did not spawn on the clients");
            // the test players only supply poses: no input, no gravity, no pushing (a character controller overlapping rope or items drifts out of range)
            foreach (NetPlayer anyPlayer in Object.FindObjectsByType<NetPlayer>(FindObjectsSortMode.None))
            {
                if (anyPlayer.IsOwner) { anyPlayer.Controller.enabled = false; }
                foreach (Collider c in anyPlayer.GetComponentsInChildren<Collider>()) { if (!(c is CharacterController)) { c.enabled = false; } }
            }
        }

        private static NetworkObject Mirror(NetworkManager nm, NetworkObject hostObject)
        {
            return nm.SpawnManager.SpawnedObjects.TryGetValue(hostObject.NetworkObjectId, out NetworkObject o) ? o : null;
        }

        private NetPlayer PlayerOf(NetworkManager nm)
        {
            foreach (NetPlayer p in Object.FindObjectsByType<NetPlayer>(FindObjectsSortMode.None))
            {
                if (p.NetworkManager == nm && p.IsOwner) { return p; }
            }
            return null;
        }

        private static void Teleport(NetPlayer p, Vector3 position) => p.Controller.Teleport(position, 0f);

        private static void MoveHandTo(NetPlayer p, Vector3 handTarget)
        {
            Teleport(p, p.transform.position + (handTarget - p.Controller.Hand.position));
        }

        private T Find<T>(NetworkManager nm, NetworkObject hostObject) where T : Component
        {
            NetworkObject o = Mirror(nm, hostObject);
            return o != null ? o.GetComponentInChildren<T>(true) : null;
        }

        private IEnumerator Grab(NetworkManager nm, Interactable target, NetworkPhysicsItem item)
        {
            ulong id = nm.LocalClientId;
            PlayerOf(nm).RequestInteract(target);
            yield return WaitForConditionOrTimeOut(() => item.HolderClientId == id);
            AssertOnTimeout("grab was not approved");
        }

        // ---------- tests ----------

        [UnityTest]
        public IEnumerator ClientPoursFuel_GeneratorGainsAndCanLoses_SeenByTheOtherClient()
        {
            yield return SpawnWorld();
            NetworkObject canObj = SpawnAt(_net.Can, new Vector3(0f, 0.5f, 3f));
            var hostCan = canObj.GetComponent<FuelCan>();
            hostCan.HostSet(30f);
            yield return WaitForConditionOrTimeOut(() => Mirror(A, canObj) != null && Mirror(B, canObj) != null);
            AssertOnTimeout("can not spawned on clients");

            NetPlayer pa = PlayerOf(A);
            Teleport(pa, new Vector3(0f, 0f, 4.5f));
            yield return new WaitForSeconds(0.3f);
            var clientItem = Mirror(A, canObj).GetComponent<NetworkPhysicsItem>();
            yield return Grab(A, clientItem.GetComponent<Interactable>(), clientItem);

            GeneratorNet hostGen = _hostGenerator.GetComponent<GeneratorNet>();
            float before = hostGen.Fuel;
            var tank = Find<GeneratorTank>(A, _hostGenerator);
            pa.RequestHold(tank, NetPlayer.HoldBegin);
            float end = Time.realtimeSinceStartup + 1.2f;
            while (Time.realtimeSinceStartup < end)
            {
                pa.RequestHold(tank, NetPlayer.HoldKeepAlive);
                yield return new WaitForSeconds(0.25f);
            }
            pa.RequestHold(tank, NetPlayer.HoldEnd);

            Assert.Greater(hostGen.Fuel, before + 5f, "generator tank did not fill");
            Assert.Less(hostCan.Amount, 30f - 5f, "can did not empty");
            Assert.AreEqual(hostGen.Fuel - before, 30f - hostCan.Amount, 0.05f, "no fuel may be created or lost");
            yield return WaitForConditionOrTimeOut(() => Mathf.Abs(Find<GeneratorNet>(B, _hostGenerator).Fuel - hostGen.Fuel) < 1f
                && Mathf.Abs(Find<FuelCan>(B, canObj).Amount - hostCan.Amount) < 1f);
            AssertOnTimeout("the other client does not see the new fuel levels");
        }

        [UnityTest]
        public IEnumerator ClientPullsCord_UntilRunning_SeenByTheOtherClient()
        {
            yield return SpawnWorld();
            NetPlayer pa = PlayerOf(A);
            Teleport(pa, new Vector3(0f, 0f, 4.5f));
            yield return new WaitForSeconds(0.3f);
            var sw = Find<GeneratorSwitch>(A, _hostGenerator);
            GeneratorNet seenByB = Find<GeneratorNet>(B, _hostGenerator);

            for (int i = 0; i < 6 && seenByB.State != GeneratorState.Running; i++)
            {
                pa.RequestInteract(sw);
                yield return new WaitForSeconds(0.5f);
            }
            Assert.AreEqual(GeneratorState.Running, _hostGenerator.GetComponent<GeneratorNet>().State, "host: generator should run within the guaranteed pulls");
            yield return WaitForConditionOrTimeOut(() => seenByB.State == GeneratorState.Running);
            AssertOnTimeout("the other client never saw the generator running");
        }

        private IEnumerator StartGenerator()
        {
            NetPlayer pa = PlayerOf(A);
            Teleport(pa, new Vector3(0f, 0f, 4.5f));
            yield return new WaitForSeconds(0.3f);
            GeneratorNet hostGen = _hostGenerator.GetComponent<GeneratorNet>();
            var sw = Find<GeneratorSwitch>(A, _hostGenerator);
            for (int i = 0; i < 6 && !hostGen.IsRunning; i++)
            {
                pa.RequestInteract(sw);
                yield return new WaitForSeconds(0.3f);
            }
            Assert.IsTrue(hostGen.IsRunning, "generator did not start");
        }

        private IEnumerator TakePlug(NetworkManager nm, int outlet)
        {
            NetPlayer p = PlayerOf(nm);
            Teleport(p, new Vector3(0f, 0f, 4.5f));
            yield return new WaitForSeconds(0.3f);
            CableNet hostCable = CableNet.ForOutlet(outlet);
            CableReel reel = Find<CableReel>(nm, _hostGenerator);
            CableReel[] reels = Mirror(nm, _hostGenerator).GetComponentsInChildren<CableReel>(true);
            reel = reels[outlet];
            p.RequestInteract(reel);
            yield return WaitForConditionOrTimeOut(() => hostCable.Item.HolderClientId == nm.LocalClientId);
            AssertOnTimeout("plug was not taken from the reel");
        }

        [UnityTest]
        public IEnumerator ClientPlugsIn_SocketPoweredOnHostAndOtherClient_AndLoadUpdates()
        {
            yield return SpawnWorld();
            yield return StartGenerator();
            GeneratorNet hostGen = _hostGenerator.GetComponent<GeneratorNet>();
            yield return TakePlug(A, 0);

            NetPlayer pa = PlayerOf(A);
            var socketInteractable = Find<SocketInteractable>(A, _hostSocket);
            var hostSocket = _hostSocket.GetComponent<PowerSocket>();
            MoveHandTo(pa, hostSocket.PlugPoint.position);
            Teleport(pa, pa.transform.position);
            yield return new WaitForSeconds(0.4f);
            pa.RequestInteract(socketInteractable);

            yield return WaitForConditionOrTimeOut(() => hostSocket.Occupied && hostSocket.Powered);
            AssertOnTimeout("socket not powered on the host");
            yield return WaitForConditionOrTimeOut(() => Find<PowerSocket>(B, _hostSocket).Powered && Find<PowerSocket>(A, _hostSocket).Powered);
            AssertOnTimeout("socket not lit on the clients");
            Assert.AreEqual(5f, hostGen.Load, 0.01f, "host load");
            yield return WaitForConditionOrTimeOut(() => Mathf.Abs(Find<GeneratorNet>(B, _hostGenerator).Load - 5f) < 0.2f);
            AssertOnTimeout("load did not reach the other client");
        }

        [UnityTest]
        public IEnumerator ClientDragsPlugPastTheLimit_CableSnaps_SocketDark_AndNewPlugOnTheReelAfterThreeSeconds()
        {
            yield return SpawnWorld();
            yield return StartGenerator();
            yield return TakePlug(A, 0);
            CableNet hostCable = CableNet.ForOutlet(0);

            NetPlayer pa = PlayerOf(A);
            var hostSocket = _hostSocket.GetComponent<PowerSocket>();
            var socketInteractable = Find<SocketInteractable>(A, _hostSocket);
            MoveHandTo(pa, hostSocket.PlugPoint.position);
            yield return new WaitForSeconds(0.4f);
            pa.RequestInteract(socketInteractable);
            yield return WaitForConditionOrTimeOut(() => hostSocket.Powered);
            AssertOnTimeout("socket never powered");

            // take the plug out again (the socket goes dark), then walk away with it
            pa.RequestInteract(socketInteractable);
            yield return WaitForConditionOrTimeOut(() => !hostSocket.Occupied && hostCable.State == CableState.Held);
            AssertOnTimeout("could not unplug");
            yield return WaitForConditionOrTimeOut(() => !hostSocket.Powered);
            AssertOnTimeout("socket should be dark after unplugging");

            Teleport(pa, new Vector3(0f, 0f, 40f));
            yield return WaitForConditionOrTimeOut(() => hostCable.State == CableState.Snapped);
            AssertOnTimeout("cable never snapped");
            Assert.IsFalse(hostSocket.Powered, "socket dark on the host");
            Assert.IsFalse(Find<PowerSocket>(B, _hostSocket).Powered, "socket dark on the other client");

            // a fresh cable shows up on the reel about three seconds after the snap
            yield return WaitForConditionOrTimeOut(() => { CableNet c = CableNet.ForOutlet(0); return c != null && c != hostCable && c.State == CableState.Fresh; });
            AssertOnTimeout("no fresh cable on the reel after the snap");
        }

        [UnityTest]
        public IEnumerator ClientThrowsCan_FromTheClientHandPose_NotTheLaggedHostPosition()
        {
            yield return SpawnWorld();
            NetworkObject canObj = SpawnAt(_net.Can, new Vector3(0f, 0.5f, 3f));
            yield return WaitForConditionOrTimeOut(() => Mirror(A, canObj) != null);
            AssertOnTimeout("can not spawned on the client");

            NetPlayer pa = PlayerOf(A);
            Teleport(pa, new Vector3(0f, 0f, 2f));
            yield return new WaitForSeconds(0.3f);
            var clientItem = Mirror(A, canObj).GetComponent<NetworkPhysicsItem>();
            var hostItem = canObj.GetComponent<NetworkPhysicsItem>();
            yield return Grab(A, clientItem.GetComponent<Interactable>(), clientItem);
            yield return new WaitForSeconds(0.4f);

            // the player steps 2 m sideways and throws in the same frame: the host's idea of the hand is stale
            Teleport(pa, pa.transform.position + new Vector3(2f, 0f, 0f));
            Vector3 claimedHand = pa.Controller.Hand.position;
            pa.RequestThrow(Vector3.down);
            yield return WaitForConditionOrTimeOut(() => !hostItem.IsHeld);
            AssertOnTimeout("can was not released");
            float fromClaimed = Vector3.Distance(canObj.transform.position, claimedHand);
            Assert.Less(fromClaimed, 0.8f, $"released {fromClaimed:F2} m from the client's hand pose");
        }

        // ---------- the shift ----------

        private static void SetTuning<T>(T instance) where T : ScriptableObject
        {
            typeof(T).GetField("_instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, instance);
        }

        /// <summary>Short windows, a short shift and fast payment (a clone, never saved) so a test takes seconds, not minutes.</summary>
        private static void UseFastTuning(float shiftSeconds = 10f)
        {
            var b = Object.Instantiate(Resources.Load<BuildingTuning>("BuildingTuning"));
            b.InitialDelayMin = b.InitialDelayMax = 0.5f;
            b.RequestMin = b.RequestMax = 8f;
            b.QuietMin = b.QuietMax = 3f;
            b.SpikeChance = 0f;
            b.RatePerAmpPerMinute = 60f;     // 5 A pays 5 per second
            b.FallPerSecond = 30f;           // 70 happiness is gone in 2.3 s
            b.SyncRateHz = 20f;
            SetTuning(b);
            var t = Object.Instantiate(Resources.Load<ShiftTuning>("ShiftTuning"));
            t.DurationSeconds = shiftSeconds;
            t.CountdownSeconds = 1.2f;
            t.SyncRateHz = 20f;
            t.MoodSeconds = 0.2f;
            SetTuning(t);
            // ponytail: in one process the clients' body copies disturb the host rope (see CLAUDE.md), so a seated plug would pop out; these tests are about the shift, not the rope
            var c = Object.Instantiate(Resources.Load<CableTuning>("CableTuning"));
            c.PopOutStretch = c.SnapStretch = 10f;
            SetTuning(c);
        }

        private IEnumerator SpawnShiftWorld(float shiftSeconds = 10f)
        {
            UseFastTuning(shiftSeconds);
            yield return SpawnWorld();
            _hostSockets[0] = _hostSocket;
            for (int i = 1; i < 4; i++)
            {
                _hostSockets[i] = SpawnAt(_loop.Socket, new Vector3(5f + 4f * i, 1f, 8f));
                _hostSockets[i].GetComponent<PowerSocket>().ServerInit(i, 5f);
            }
            for (int i = 0; i < 4; i++)
            {
                _hostBuildings[i] = SpawnAt(_buildings.Building, new Vector3(5f + 4f * i, 0f, 12f));
                _hostBuildings[i].GetComponent<BuildingNet>().ServerInit(i);
            }
            _hostManager = SpawnAt(_buildings.ShiftManager, new Vector3(0f, 0f, 3f));
            yield return WaitForConditionOrTimeOut(() => Mirror(A, _hostManager) != null && Mirror(B, _hostManager) != null
                && Mirror(A, _hostBuildings[3]) != null && Mirror(B, _hostBuildings[3]) != null && Mirror(A, _hostSockets[3]) != null);
            AssertOnTimeout("the shift world did not reach the clients");
        }

        private ShiftNet ShiftOn(NetworkManager nm) => nm == null ? _hostManager.GetComponent<ShiftNet>() : Mirror(nm, _hostManager).GetComponent<ShiftNet>();
        private BuildingNet BuildingOn(NetworkManager nm, int index) => nm == null ? _hostBuildings[index].GetComponent<BuildingNet>() : Mirror(nm, _hostBuildings[index]).GetComponent<BuildingNet>();

        /// <summary>Client A takes the plug of outlet 0 and plugs it into socket (and so building) 0.</summary>
        private IEnumerator PlugIntoBuildingZero()
        {
            yield return StartGenerator();
            yield return TakePlug(A, 0);
            NetPlayer pa = PlayerOf(A);
            MoveHandTo(pa, _hostSockets[0].GetComponent<PowerSocket>().PlugPoint.position);
            Teleport(pa, pa.transform.position);
            yield return new WaitForSeconds(0.4f);
            pa.RequestInteract(Find<SocketInteractable>(A, _hostSockets[0]));
            yield return WaitForConditionOrTimeOut(() => _hostSockets[0].GetComponent<PowerSocket>().Occupied);
            AssertOnTimeout("the plug did not go into the socket");
        }

        [UnityTest]
        public IEnumerator ClientStartsAShift_HostAndTheOtherClientEnterRunning()
        {
            yield return SpawnShiftWorld();
            NetPlayer pa = PlayerOf(A);
            Teleport(pa, new Vector3(0f, 0f, 2f));
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(ShiftState.Lobby, ShiftOn(null).State);
            Assert.AreEqual(ShiftState.Lobby, ShiftOn(B).State);

            pa.RequestInteract(Find<ShiftClipboard>(A, _hostManager));
            yield return WaitForConditionOrTimeOut(() => ShiftOn(null).State == ShiftState.Countdown && ShiftOn(B).State == ShiftState.Countdown);
            AssertOnTimeout("the countdown did not start on every peer");
            yield return WaitForConditionOrTimeOut(() => ShiftOn(null).State == ShiftState.Running && ShiftOn(A).State == ShiftState.Running && ShiftOn(B).State == ShiftState.Running);
            AssertOnTimeout("the shift did not reach Running on every peer");
            Assert.AreEqual(1, ShiftOn(null).ShiftNumber);
            Assert.Less(ShiftOn(B).TimeLeft, 10.01f);
        }

        [UnityTest]
        public IEnumerator PluggedRequestingBuilding_IsPoweredOnAllPeers_AndMoneyRisesOnAllPeers()
        {
            yield return SpawnShiftWorld();
            yield return PlugIntoBuildingZero();
            ShiftOn(null).HostStart();
            yield return WaitForConditionOrTimeOut(() => BuildingOn(null, 0).Requesting && BuildingOn(A, 0).Requesting && BuildingOn(B, 0).Requesting);
            AssertOnTimeout("building 0 never started requesting on every peer");
            yield return WaitForConditionOrTimeOut(() => BuildingOn(null, 0).Powered && BuildingOn(A, 0).Powered && BuildingOn(B, 0).Powered);
            AssertOnTimeout($"the plugged, requesting building is not powered on every peer (host {BuildingOn(null, 0).Powered}/{BuildingOn(null, 0).Window}, A {BuildingOn(A, 0).Powered}/{BuildingOn(A, 0).Window}, B {BuildingOn(B, 0).Powered}/{BuildingOn(B, 0).Window}, socket {_hostSockets[0].GetComponent<PowerSocket>().Powered}/{_hostSockets[0].GetComponent<PowerSocket>().Occupied}, A socket {(BuildingOn(A, 0).Socket == null ? "null" : BuildingOn(A, 0).Socket.NetworkObjectId + "/" + BuildingOn(A, 0).Socket.IsServer + "/" + BuildingOn(A, 0).Socket.Powered)} vs host {_hostSockets[0].NetworkObjectId}, idx {BuildingOn(A, 0).Index}, gen running {_hostGenerator.GetComponent<GeneratorNet>().IsRunning})");
            Assert.IsTrue(Find<PowerSocket>(B, _hostSockets[0]).Powered);
            yield return WaitForConditionOrTimeOut(() => ShiftOn(null).Money >= 3 && ShiftOn(A).Money >= 3 && ShiftOn(B).Money >= 3);
            AssertOnTimeout($"money did not rise on every peer (host {ShiftOn(null).Money}, A {ShiftOn(A).Money}, B {ShiftOn(B).Money})");
            // the generator sees the building's amps
            Assert.AreEqual(5f, _hostGenerator.GetComponent<GeneratorNet>().Load, 0.3f);
        }

        [UnityTest]
        public IEnumerator UnpoweredRequestingBuilding_LosesAllHappiness_TurnsAngryOnAllPeers_AndStopsPaying()
        {
            yield return SpawnShiftWorld();
            yield return PlugIntoBuildingZero();
            ShiftOn(null).HostStart();
            yield return WaitForConditionOrTimeOut(() => ShiftOn(null).Money >= 2 && ShiftOn(A).Money >= 2 && ShiftOn(B).Money >= 2);
            AssertOnTimeout("building 0 never paid");

            // take the plug out again: the building is in the dark while it still requests
            PlayerOf(A).RequestInteract(Find<SocketInteractable>(A, _hostSockets[0]));
            yield return WaitForConditionOrTimeOut(() => !_hostSockets[0].GetComponent<PowerSocket>().Occupied);
            AssertOnTimeout("could not unplug");
            yield return WaitForConditionOrTimeOut(() => BuildingOn(null, 0).Angry && BuildingOn(A, 0).Angry && BuildingOn(B, 0).Angry);
            AssertOnTimeout($"the building did not turn Angry on every peer (happiness host {BuildingOn(null, 0).Happiness})");
            Assert.AreEqual(0f, BuildingOn(null, 0).Happiness, 0.5f);
            yield return WaitForConditionOrTimeOut(() => BuildingOn(B, 0).Happiness < 1f);
            AssertOnTimeout("happiness did not reach 0 on the other client");

            // an Angry building pays nothing: even when it has power again the money stays where it was
            yield return new WaitForSeconds(0.3f);
            int before = ShiftOn(null).Money;
            PlayerOf(A).RequestInteract(Find<SocketInteractable>(A, _hostSockets[0]));   // (the player holds the plug: this plugs it back in)
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(before, ShiftOn(null).Money, "an angry building must not pay");
            Assert.AreEqual(before, ShiftOn(A).Money);
            Assert.AreEqual(before, ShiftOn(B).Money);
        }

        [UnityTest]
        public IEnumerator ShiftEnds_AllPeersShowTheSameSummary_AndAnotherShiftResetsToTheLobby()
        {
            yield return SpawnShiftWorld(shiftSeconds: 8f);
            ShiftOn(null).HostStart();
            yield return WaitForConditionOrTimeOut(() => ShiftOn(null).State == ShiftState.Summary && ShiftOn(A).State == ShiftState.Summary && ShiftOn(B).State == ShiftState.Summary, new TimeoutHelper(30f));
            AssertOnTimeout("the shift did not reach the summary on every peer");
            yield return new WaitForSeconds(0.4f);
            ShiftSummaryData host = ShiftOn(null).Summary;
            Assert.IsTrue(host.Equals(ShiftOn(A).Summary), "client A shows other summary numbers than the host");
            Assert.IsTrue(host.Equals(ShiftOn(B).Summary), "client B shows other summary numbers than the host");
            Assert.AreEqual(ShiftOn(null).Money, host.Money);
            Assert.GreaterOrEqual(host.AngryBuildings, 1, "nothing was plugged in: the requesting buildings turn angry");
            Assert.IsTrue(ShiftOn(A).Hud.SummaryVisible && ShiftOn(B).Hud.SummaryVisible && ShiftOn(null).Hud.SummaryVisible, "every peer shows the summary screen");

            // "Another shift": everything back to the defaults, in the lobby, on every peer
            Assert.IsTrue(ShiftOn(null).HostAnotherShift());
            yield return WaitForConditionOrTimeOut(() => ShiftOn(null).State == ShiftState.Lobby && ShiftOn(A).State == ShiftState.Lobby && ShiftOn(B).State == ShiftState.Lobby);
            AssertOnTimeout("not every peer is back in the lobby");
            yield return new WaitForSeconds(0.4f);
            foreach (NetworkManager nm in new NetworkManager[] { null, A, B })
            {
                Assert.AreEqual(0, ShiftOn(nm).Money);
                Assert.IsTrue(ShiftOn(nm).Summary.Equals(default(ShiftSummaryData)));
                Assert.IsFalse(ShiftOn(nm).Hud.SummaryVisible);
                for (int i = 0; i < 4; i++)
                {
                    BuildingNet b = BuildingOn(nm, i);
                    Assert.IsFalse(b.Requesting);
                    Assert.IsFalse(b.Angry);
                    Assert.AreEqual(70f, b.Happiness, 1.01f);
                    Assert.AreEqual(0f, b.BubbleSecondsLeft, 1e-3f, "no bubble left over");
                }
            }
        }

        // ---------- grab / holder-leaves (the Prompt 03 tests; one NetcodeIntegrationTest fixture per editor session) ----------

        [UnityTest]
        public IEnumerator ClientGrabsCan_HostSeesItHeld_AndClientShowsLocalCopy()
        {
            SpawnObject(_net.Player.gameObject, m_ServerNetworkManager);
            SpawnObject(_net.Player.gameObject, A);
            GameObject serverCan = SpawnObject(_net.Can.gameObject, m_ServerNetworkManager);
            ulong canId = serverCan.GetComponent<NetworkObject>().NetworkObjectId;
            ulong clientId = A.LocalClientId;

            yield return WaitForConditionOrTimeOut(() => A.SpawnManager.SpawnedObjects.ContainsKey(canId)
                && NetPlayer.Local != null && NetPlayer.Local.OwnerClientId == clientId);
            AssertOnTimeout("client never saw the can and its own player");

            NetworkObject clientCanObject = A.SpawnManager.SpawnedObjects[canId];
            var clientCan = clientCanObject.GetComponent<NetworkPhysicsItem>();
            NetPlayer clientPlayer = NetPlayer.Local;
            Assert.AreEqual(clientId, clientPlayer.OwnerClientId);

            clientPlayer.RequestInteract(clientCanObject.GetComponent<PowerCutCrew.Core.Interactable>());

            var serverCan2 = serverCan.GetComponent<NetworkPhysicsItem>();
            yield return WaitForConditionOrTimeOut(() => serverCan2.HolderClientId == clientId && clientCan.HolderClientId == clientId);
            AssertOnTimeout("host never saw the can held by the client");

            // the holder sees a local copy in the hand and not the networked item
            Assert.IsNotNull(clientPlayer.Controller.Hand.Find("HeldCopy"), "local copy should be in the hand");
            Assert.IsFalse(clientCan.GetComponentInChildren<Renderer>().enabled, "networked renderer should be hidden for the holder");

            // dropping returns it to the world for everybody
            clientPlayer.RequestDrop();
            yield return WaitForConditionOrTimeOut(() => !serverCan2.IsHeld && !clientCan.IsHeld);
            AssertOnTimeout("can was not released");
        }

        [UnityTest]
        public IEnumerator HolderLeaves_ItemDropsInPlace_AndCanBeGrabbedAgain()
        {
            GameObject hostPlayerGo = SpawnObject(_net.Player.gameObject, m_ServerNetworkManager);
            GameObject clientPlayerGo = SpawnObject(_net.Player.gameObject, A);
            GameObject serverCan = SpawnAt(_net.Can, new Vector3(0f, 0.5f, 3f)).gameObject;
            ulong canId = serverCan.GetComponent<NetworkObject>().NetworkObjectId;
            ulong clientId = A.LocalClientId;

            yield return WaitForConditionOrTimeOut(() => A.SpawnManager.SpawnedObjects.ContainsKey(canId)
                && NetPlayer.Local != null && NetPlayer.Local.OwnerClientId == clientId);
            AssertOnTimeout("client never saw the can and its own player");

            PlayerOf(A).Controller.Teleport(new Vector3(0f, 0f, 2f), 0f);
            yield return new WaitForSeconds(0.3f);
            var serverItem = serverCan.GetComponent<NetworkPhysicsItem>();
            NetPlayer.Local.RequestInteract(A.SpawnManager.SpawnedObjects[canId].GetComponent<PowerCutCrew.Core.Interactable>());
            yield return WaitForConditionOrTimeOut(() => serverItem.HolderClientId == clientId);
            AssertOnTimeout("client never grabbed the can");
            yield return new WaitForSeconds(0.5f);   // let the can settle in the holder's hold point
            Vector3 heldAt = serverCan.transform.position;

            // the client crashes / leaves: its player despawns on the host
            ulong clientPlayerId = clientPlayerGo.GetComponent<NetworkObject>().NetworkObjectId;
            m_ServerNetworkManager.SpawnManager.SpawnedObjects[clientPlayerId].Despawn(true);
            yield return WaitForConditionOrTimeOut(() => !serverItem.IsHeld);
            AssertOnTimeout("can stayed held after its holder left");
            Vector3 now = serverCan.transform.position;
            // no ground in this test scene, so the can keeps falling and may drift while it is polled: only require that it was not teleported or reset
            Assert.Less(Vector2.Distance(new Vector2(heldAt.x, heldAt.z), new Vector2(now.x, now.z)), 1.5f, $"can should stay near where it was held (held at {heldAt}, now {now})");

            // somebody else can grab it again
            hostPlayerGo.GetComponent<NetPlayer>().Controller.Teleport(serverCan.transform.position + new Vector3(0f, 0f, -1f), 0f);   // the test world has a ground now: walk up to the can
            yield return new WaitForSeconds(0.2f);
            hostPlayerGo.GetComponent<NetPlayer>().RequestInteract(serverCan.GetComponent<PowerCutCrew.Core.Interactable>());
            yield return WaitForConditionOrTimeOut(() => serverItem.IsHeld && serverItem.HolderClientId == m_ServerNetworkManager.LocalClientId);
            AssertOnTimeout($"can could not be grabbed again after the drop (held={serverItem.IsHeld} grabbable={serverItem.CanBeGrabbed} byClient0={NetPlayer.ByClient.ContainsKey(0)} playerAt={hostPlayerGo.transform.position} canAt={serverCan.transform.position} dist={serverCan.GetComponent<Interactable>().DistanceTo(hostPlayerGo.transform.position):F2} holds={NetworkPhysicsItem.PlayerHoldsAnything(0)})");
        }
    }
}
#endif
