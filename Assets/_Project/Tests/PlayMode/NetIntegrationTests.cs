#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using PowerCutCrew.Cables;
using PowerCutCrew.Core;
using PowerCutCrew.Fuel;
using PowerCutCrew.Generator;
using PowerCutCrew.Net;
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

        protected override int NumberOfClients => 2;

        private NetworkManager A => m_ClientNetworkManagers[0];
        private NetworkManager B => m_ClientNetworkManagers[1];

        protected override void OnServerAndClientsCreated()
        {
            _net = AssetDatabase.LoadAssetAtPath<NetPrefabCatalog>("Assets/_Project/Config/NetPrefabCatalog.asset");
            _loop = Resources.Load<CoreLoopCatalog>("CoreLoopCatalog");
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
            NetworkPhysicsItem.ResetStatics();
            if (_ground != null) { Object.Destroy(_ground); }
            if (_registry != null) { Object.Destroy(_registry); }
            yield return base.OnTearDown();
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
