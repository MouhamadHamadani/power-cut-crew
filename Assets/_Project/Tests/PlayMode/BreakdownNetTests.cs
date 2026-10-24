#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using PowerCutCrew.Breakdowns;
using PowerCutCrew.Cables;
using PowerCutCrew.Core;
using PowerCutCrew.Generator;
using PowerCutCrew.Net;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerCutCrew.Tests.PlayMode
{
    /// <summary>
    /// In-process host + two clients and the three breakdowns: a client fixes each kind through the same requests a player sends (take the wrench and swing it, take a belt
    /// from the crate and hold it at the slot, hold the lever), the others see it, and a client that joins late sees what is broken right now.
    /// </summary>
    public class BreakdownNetTests : NetcodeIntegrationTest
    {
        private NetPrefabCatalog _net;
        private CoreLoopCatalog _loop;
        private BreakdownCatalog _breakdowns;
        private GameObject _ground;
        private GameObject _registry;
        private NetworkObject _hostGenerator;

        protected override int NumberOfClients => 2;

        private NetworkManager A => m_ClientNetworkManagers[0];
        private NetworkManager B => m_ClientNetworkManagers[1];
        private BreakdownNet Host => _hostGenerator.GetComponent<BreakdownNet>();

        private void AddPrefabs(NetworkManager nm)
        {
            nm.AddNetworkPrefab(_net.Player.gameObject);
            nm.AddNetworkPrefab(_loop.Generator.gameObject);
            nm.AddNetworkPrefab(_loop.Socket.gameObject);
            nm.AddNetworkPrefab(_loop.Cable.gameObject);
            nm.AddNetworkPrefab(_breakdowns.Wrench.gameObject);
            nm.AddNetworkPrefab(_breakdowns.Belt.gameObject);
        }

        protected override void OnServerAndClientsCreated()
        {
            _net = AssetDatabase.LoadAssetAtPath<NetPrefabCatalog>("Assets/_Project/Config/NetPrefabCatalog.asset");
            _loop = Resources.Load<CoreLoopCatalog>("CoreLoopCatalog");
            _breakdowns = Resources.Load<BreakdownCatalog>("BreakdownCatalog");
            AddPrefabs(m_ServerNetworkManager);
            foreach (NetworkManager nm in m_ClientNetworkManagers) { AddPrefabs(nm); }
            NetworkPhysicsItem.ResetStatics();
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(200f, 1f, 200f);
            _registry = new GameObject("HoldRegistry");
            _registry.AddComponent<HoldRegistry>();
        }

        protected override void OnNewClientCreated(NetworkManager networkManager)
        {
            AddPrefabs(networkManager);
            base.OnNewClientCreated(networkManager);
        }

        protected override IEnumerator OnTearDown()
        {
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
            foreach (GeneratorNet g in Object.FindObjectsByType<GeneratorNet>(FindObjectsSortMode.None)) { Object.Destroy(g.gameObject); }
            foreach (CableNet c in Object.FindObjectsByType<CableNet>(FindObjectsSortMode.None)) { Object.Destroy(c.gameObject); }
            foreach (PowerSocket p in Object.FindObjectsByType<PowerSocket>(FindObjectsSortMode.None)) { Object.Destroy(p.gameObject); }
            foreach (WrenchItem w in Object.FindObjectsByType<WrenchItem>(FindObjectsSortMode.None)) { Object.Destroy(w.gameObject); }
            foreach (SpareBelt b in Object.FindObjectsByType<SpareBelt>(FindObjectsSortMode.None)) { Object.Destroy(b.gameObject); }
            yield return null;
        }

        // ---------- world ----------

        private static NetworkObject Mirror(NetworkManager nm, NetworkObject hostObject)
        {
            return nm.SpawnManager.SpawnedObjects.TryGetValue(hostObject.NetworkObjectId, out NetworkObject o) ? o : null;
        }

        private T Find<T>(NetworkManager nm, NetworkObject hostObject) where T : Component
        {
            NetworkObject o = Mirror(nm, hostObject);
            return o != null ? o.GetComponentInChildren<T>(true) : null;
        }

        private static NetPlayer PlayerOf(NetworkManager nm)
        {
            foreach (NetPlayer p in Object.FindObjectsByType<NetPlayer>(FindObjectsSortMode.None))
            {
                if (p.NetworkManager == nm && p.IsOwner) { return p; }
            }
            return null;
        }

        private static void Teleport(NetPlayer p, Vector3 position) => p.Controller.Teleport(position, 0f);

        private IEnumerator SpawnWorld()
        {
            SpawnObject(_net.Player.gameObject, A);
            SpawnObject(_net.Player.gameObject, B);
            _hostGenerator = NetworkObject.InstantiateAndSpawn(_loop.Generator.gameObject, m_ServerNetworkManager, position: new Vector3(0f, 0f, 6f), rotation: Quaternion.identity);
            yield return WaitForConditionOrTimeOut(() => NetPlayer.ByClient.ContainsKey(A.LocalClientId) && NetPlayer.ByClient.ContainsKey(B.LocalClientId)
                && Mirror(A, _hostGenerator) != null && Mirror(B, _hostGenerator) != null && WrenchOn(A) != null && WrenchOn(B) != null);
            AssertOnTimeout("the world (generator, players, wrench) did not spawn on the clients");
            foreach (NetPlayer anyPlayer in Object.FindObjectsByType<NetPlayer>(FindObjectsSortMode.None))
            {
                if (anyPlayer.IsOwner) { anyPlayer.Controller.enabled = false; }
                foreach (Collider c in anyPlayer.GetComponentsInChildren<Collider>()) { if (!(c is CharacterController)) { c.enabled = false; } }
            }
        }

        private static NetworkPhysicsItem WrenchOn(NetworkManager nm)
        {
            foreach (WrenchItem w in Object.FindObjectsByType<WrenchItem>(FindObjectsSortMode.None))
            {
                if (w.Item.IsSpawned && w.Item.NetworkManager == nm) { return w.Item; }
            }
            return null;
        }

        private static NetworkPhysicsItem BeltHeldBy(NetworkManager nm, ulong client)
        {
            foreach (SpareBelt b in Object.FindObjectsByType<SpareBelt>(FindObjectsSortMode.None))
            {
                if (b.Item.IsSpawned && b.Item.NetworkManager == nm && b.Item.HolderClientId == client) { return b.Item; }
            }
            return null;
        }

        /// <summary>The player stands this far in front of a point of the generator (the generator is at z = 6, its front faces +z).</summary>
        private static void StandNear(NetPlayer p, Vector3 point, float distance = 1.2f)
        {
            Teleport(p, new Vector3(point.x, 0f, point.z + distance));
        }

        private IEnumerator TakeWrench(NetworkManager nm)
        {
            NetPlayer p = PlayerOf(nm);
            NetworkPhysicsItem hostWrench = WrenchOn(m_ServerNetworkManager);
            NetworkPhysicsItem mine = WrenchOn(nm);
            Teleport(p, hostWrench.transform.position + new Vector3(0f, -hostWrench.transform.position.y, 1.2f));
            yield return new WaitForSeconds(0.4f);
            p.RequestInteract(mine.GetComponent<Interactable>());
            yield return WaitForConditionOrTimeOut(() => hostWrench.HolderClientId == nm.LocalClientId);
            AssertOnTimeout("the wrench was not picked up");
        }

        private IEnumerator Swing(NetworkManager nm, Vector3 point)
        {
            NetPlayer p = PlayerOf(nm);
            StandNear(p, point, 1.2f);
            yield return new WaitForSeconds(0.35f);   // the host's copy of the player has to catch up with the teleport
            NetworkPhysicsItem mine = WrenchOn(nm);
            p.RequestUse(mine, Find<BreakdownNet>(nm, _hostGenerator).transform, point);
            yield return new WaitForSeconds(0.5f);    // longer than the swing cooldown
        }

        // ---------- tests ----------

        [UnityTest]
        public IEnumerator AClientFixesTheSputteringEngine_WithTheWrench_AndEveryoneSeesIt()
        {
            yield return SpawnWorld();
            Assert.IsTrue(Host.HostForce(BreakdownType.Sputter, 1), "the breakdown starts");
            yield return WaitForConditionOrTimeOut(() => Find<BreakdownNet>(A, _hostGenerator).IsActive(BreakdownType.Sputter) && Find<BreakdownNet>(B, _hostGenerator).SweetSpotIndex == 1);
            AssertOnTimeout("the clients do not see the sputtering engine and its sweet spot");

            yield return TakeWrench(A);
            BreakdownNet seenByA = Find<BreakdownNet>(A, _hostGenerator);
            Vector3 sweet = seenByA.SweetSpots[1].position;
            Vector3 elsewhere = seenByA.SweetSpots[3].position;

            int misses = NetMetrics.GetCount("wrenchHit_Miss");
            yield return Swing(A, elsewhere);
            Assert.AreEqual(0, Host.HitsDone, "a hit elsewhere makes no progress");
            Assert.IsTrue(Host.IsActive(BreakdownType.Sputter));
            Assert.AreEqual(misses + 1, NetMetrics.GetCount("wrenchHit_Miss"), "the miss was judged by the host");

            yield return Swing(A, sweet);
            Assert.AreEqual(1, Host.HitsDone);
            yield return WaitForConditionOrTimeOut(() => Find<BreakdownNet>(B, _hostGenerator).HitsDone == 1);
            AssertOnTimeout("the other client does not see the hit");
            yield return Swing(A, sweet);
            yield return Swing(A, sweet);
            Assert.IsFalse(Host.IsActive(BreakdownType.Sputter), "three good hits fix it");
            yield return WaitForConditionOrTimeOut(() => !Find<BreakdownNet>(A, _hostGenerator).IsActive(BreakdownType.Sputter) && !Find<BreakdownNet>(B, _hostGenerator).IsActive(BreakdownType.Sputter));
            AssertOnTimeout("the clients still see a sputtering engine");
            Assert.AreEqual(1, Host.FixedCount);
        }

        [UnityTest]
        public IEnumerator TheHostRefusesAHitFromTooFarAway_AndFromAPlayerWhoDoesNotHoldTheWrench()
        {
            yield return SpawnWorld();
            Host.HostForce(BreakdownType.Sputter, 0);
            yield return TakeWrench(A);
            Vector3 sweet = Find<BreakdownNet>(A, _hostGenerator).SweetSpots[0].position;
            NetPlayer pa = PlayerOf(A);
            Teleport(pa, new Vector3(sweet.x, 0f, sweet.z - 6f));   // six metres off, behind the generator
            yield return new WaitForSeconds(0.4f);
            int tooFar = NetMetrics.GetCount("wrenchOutOfRange");
            pa.RequestUse(WrenchOn(A), Find<BreakdownNet>(A, _hostGenerator).transform, sweet);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(tooFar + 1, NetMetrics.GetCount("wrenchOutOfRange"));
            Assert.AreEqual(0, Host.HitsDone);

            // B does not hold it: the request is dropped
            NetPlayer pb = PlayerOf(B);
            StandNear(pb, sweet, 1.2f);
            yield return new WaitForSeconds(0.4f);
            int rejected = NetMetrics.GetCount("useRejected");
            pb.RequestUse(WrenchOn(B), Find<BreakdownNet>(B, _hostGenerator).transform, sweet);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(rejected + 1, NetMetrics.GetCount("useRejected"));
            Assert.AreEqual(0, Host.HitsDone);
        }

        [UnityTest]
        public IEnumerator AClientFitsAnewBelt_FromTheCrate_HoldingItAtTheSlot()
        {
            yield return SpawnWorld();
            Assert.IsTrue(Host.HostForce(BreakdownType.Belt));
            GeneratorNet hostGen = _hostGenerator.GetComponent<GeneratorNet>();
            yield return WaitForConditionOrTimeOut(() => hostGen.Model != null && hostGen.Model.CoolingBroken);
            AssertOnTimeout("the generator's cooling is not broken");
            yield return WaitForConditionOrTimeOut(() => Find<BreakdownNet>(B, _hostGenerator).IsActive(BreakdownType.Belt));
            AssertOnTimeout("the other client does not see the broken belt");

            NetPlayer pa = PlayerOf(A);
            var crate = Find<SparePartsCrate>(A, _hostGenerator);
            StandNear(pa, crate.transform.position, 1.3f);
            yield return new WaitForSeconds(0.4f);
            pa.RequestInteract(crate);
            yield return WaitForConditionOrTimeOut(() => BeltHeldBy(m_ServerNetworkManager, A.LocalClientId) != null);
            AssertOnTimeout("no belt came out of the crate into the player's hands");

            var slot = Find<BeltSlot>(A, _hostGenerator);
            StandNear(pa, slot.transform.position, 1.2f);
            yield return new WaitForSeconds(0.4f);
            pa.RequestHold(slot, NetPlayer.HoldBegin);
            float first = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < first)
            {
                pa.RequestHold(slot, NetPlayer.HoldKeepAlive);
                yield return new WaitForSeconds(0.2f);
            }
            Assert.IsTrue(Host.IsActive(BreakdownType.Belt), "one second is not enough");
            Assert.Greater(Find<BreakdownNet>(B, _hostGenerator).BeltProgress01, 0.2f, "the other client sees the fitting progress");
            float end = Time.realtimeSinceStartup + 1.8f;
            while (Time.realtimeSinceStartup < end && Host.IsActive(BreakdownType.Belt))
            {
                pa.RequestHold(slot, NetPlayer.HoldKeepAlive);
                yield return new WaitForSeconds(0.2f);
            }
            pa.RequestHold(slot, NetPlayer.HoldEnd);
            Assert.IsFalse(Host.IsActive(BreakdownType.Belt), "two seconds of holding fits the belt");
            Assert.IsFalse(hostGen.Model.CoolingBroken, "the generator cools again");
            yield return WaitForConditionOrTimeOut(() => !Find<BreakdownNet>(B, _hostGenerator).IsActive(BreakdownType.Belt) && BeltHeldBy(m_ServerNetworkManager, A.LocalClientId) == null);
            AssertOnTimeout("the belt is not used up or the other client still sees it broken");
        }

        [UnityTest]
        public IEnumerator TheCrateNeverHasMoreThanTwoBeltsLyingAround()
        {
            yield return SpawnWorld();
            NetPlayer pa = PlayerOf(A);
            var crate = Find<SparePartsCrate>(A, _hostGenerator);
            StandNear(pa, crate.transform.position, 1.3f);
            yield return new WaitForSeconds(0.4f);
            for (int i = 0; i < 3; i++)
            {
                pa.RequestInteract(crate);
                yield return new WaitForSeconds(0.4f);
                pa.RequestDrop();   // put it down, so the hands are free for the next one
                yield return new WaitForSeconds(0.4f);
            }
            Assert.AreEqual(2, SpareBelt.CountIn(m_ServerNetworkManager), "the third request is refused");
        }

        [UnityTest]
        public IEnumerator AClientResetsTheTrippedBreaker_AndWhenTheStreetIsOverloadedItFallsBackAfterASecond()
        {
            yield return SpawnWorld();
            GeneratorNet hostGen = _hostGenerator.GetComponent<GeneratorNet>();
            Assert.IsTrue(Host.HostForce(BreakdownType.Breaker, -1, 0));
            yield return WaitForConditionOrTimeOut(() => hostGen.Grid.IsTripped(0));
            AssertOnTimeout("the outlet is not tripped");
            yield return WaitForConditionOrTimeOut(() => Find<BreakdownNet>(B, _hostGenerator).TrippedOutlet == 0 && !Find<BreakdownNet>(B, _hostGenerator).LeverUp);
            AssertOnTimeout("the other client does not see the lever down and the tripped outlet");

            NetPlayer pa = PlayerOf(A);
            var lever = Find<BreakerLever>(A, _hostGenerator);
            StandNear(pa, lever.transform.position, 1.4f);
            yield return new WaitForSeconds(0.4f);
            pa.RequestHold(lever, NetPlayer.HoldBegin);
            float end = Time.realtimeSinceStartup + 2.2f;
            while (Time.realtimeSinceStartup < end && Host.IsActive(BreakdownType.Breaker))
            {
                pa.RequestHold(lever, NetPlayer.HoldKeepAlive);
                yield return new WaitForSeconds(0.2f);
            }
            pa.RequestHold(lever, NetPlayer.HoldEnd);
            Assert.IsFalse(Host.IsActive(BreakdownType.Breaker), "a lever held for 1.5 s resets the breaker (nothing is plugged in: no overload)");
            Assert.IsFalse(hostGen.Grid.IsTripped(0));
            yield return WaitForConditionOrTimeOut(() => !Find<BreakdownNet>(B, _hostGenerator).IsActive(BreakdownType.Breaker));
            AssertOnTimeout("the other client still sees it tripped");
            Assert.AreEqual(1, Host.FixedCount);

            // now with a building asking for 15 A on that outlet: the lever goes up and falls back
            hostGen.Grid.RegisterSocket(7, 15f);
            Assert.IsTrue(hostGen.Grid.Connect(0, 7));
            Assert.IsTrue(Host.HostForce(BreakdownType.Breaker, -1, 0));
            yield return new WaitForSeconds(0.3f);
            pa.RequestHold(lever, NetPlayer.HoldBegin);
            end = Time.realtimeSinceStartup + 3.8f;   // 1.5 s of holding, 1 s with the lever up, then it falls
            bool sawLeverUp = false;
            while (Time.realtimeSinceStartup < end)
            {
                pa.RequestHold(lever, NetPlayer.HoldKeepAlive);
                sawLeverUp |= Host.LeverUp && Host.IsActive(BreakdownType.Breaker);
                yield return new WaitForSeconds(0.1f);
            }
            pa.RequestHold(lever, NetPlayer.HoldEnd);
            Assert.IsTrue(sawLeverUp, "the lever went up for a moment");
            Assert.IsTrue(Host.IsActive(BreakdownType.Breaker), "but the street is still overloaded, so it is not fixed");
            Assert.GreaterOrEqual(Host.Retrips, 1, "it fell back");
            Assert.AreEqual(1, Host.FixedCount, "no second fix");
        }

        [UnityTest]
        public IEnumerator ALateJoinerSeesWhatIsBrokenRightNow_WithItsSweetSpotAndTheTrippedOutlet()
        {
            yield return SpawnWorld();
            Assert.IsTrue(Host.HostForce(BreakdownType.Sputter, 3));
            Assert.IsTrue(Host.HostForce(BreakdownType.Breaker, -1, 1));
            Host.HostDevHit(true);   // one good hit on the sweet spot already
            yield return new WaitForSeconds(0.3f);

            CreateAndStartNewClient();
            NetworkManager late = m_ClientNetworkManagers[m_ClientNetworkManagers.Length - 1];
            yield return WaitForConditionOrTimeOut(() => late.IsConnectedClient && Mirror(late, _hostGenerator) != null);
            AssertOnTimeout("the late joiner did not get the generator");
            BreakdownNet seen = Find<BreakdownNet>(late, _hostGenerator);
            yield return WaitForConditionOrTimeOut(() => seen.IsSpawned && seen.ActiveMask == Host.ActiveMask);
            AssertOnTimeout("the late joiner does not see the active breakdowns");
            Assert.IsTrue(seen.IsActive(BreakdownType.Sputter));
            Assert.IsTrue(seen.IsActive(BreakdownType.Breaker));
            Assert.IsFalse(seen.IsActive(BreakdownType.Belt));
            Assert.AreEqual(3, seen.SweetSpotIndex, "the sweet spot");
            Assert.AreEqual(1, seen.HitsDone, "the hits done");
            Assert.AreEqual(1, seen.TrippedOutlet, "the tripped outlet");
            Assert.IsFalse(seen.LeverUp, "the lever is down");
            Assert.IsNotNull(WrenchOn(late), "and the wrench hangs on its hook");
        }
    }
}
#endif
