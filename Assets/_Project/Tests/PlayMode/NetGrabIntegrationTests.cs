#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using PowerCutCrew.Net;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerCutCrew.Tests.PlayMode
{
    /// <summary>In-process host + client (NGO integration helpers): the client grabs a can and the host sees it held.</summary>
    public class NetGrabIntegrationTests : NetcodeIntegrationTest
    {
        private GameObject _playerPrefab;
        private GameObject _canPrefab;

        protected override int NumberOfClients => 1;

        protected override void OnServerAndClientsCreated()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<NetPrefabCatalog>("Assets/_Project/Config/NetPrefabCatalog.asset");
            _playerPrefab = catalog.Player.gameObject;
            _canPrefab = catalog.Can.gameObject;
            m_ServerNetworkManager.AddNetworkPrefab(_playerPrefab);
            m_ServerNetworkManager.AddNetworkPrefab(_canPrefab);
            m_ClientNetworkManagers[0].AddNetworkPrefab(_playerPrefab);
            m_ClientNetworkManagers[0].AddNetworkPrefab(_canPrefab);
            NetworkPhysicsItem.ResetStatics();
        }

        protected override IEnumerator OnTearDown()
        {
            NetworkPhysicsItem.ResetStatics();
            yield return base.OnTearDown();
        }

        [UnityTest]
        public IEnumerator ClientGrabsCan_HostSeesItHeld_AndClientShowsLocalCopy()
        {
            SpawnObject(_playerPrefab, m_ServerNetworkManager);
            SpawnObject(_playerPrefab, m_ClientNetworkManagers[0]);
            GameObject serverCan = SpawnObject(_canPrefab, m_ServerNetworkManager);
            ulong canId = serverCan.GetComponent<NetworkObject>().NetworkObjectId;
            ulong clientId = m_ClientNetworkManagers[0].LocalClientId;

            yield return WaitForConditionOrTimeOut(() => m_ClientNetworkManagers[0].SpawnManager.SpawnedObjects.ContainsKey(canId)
                && NetPlayer.Local != null && NetPlayer.Local.OwnerClientId == clientId);
            AssertOnTimeout("client never saw the can and its own player");

            NetworkObject clientCanObject = m_ClientNetworkManagers[0].SpawnManager.SpawnedObjects[canId];
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
            GameObject hostPlayerGo = SpawnObject(_playerPrefab, m_ServerNetworkManager);
            GameObject clientPlayerGo = SpawnObject(_playerPrefab, m_ClientNetworkManagers[0]);
            GameObject serverCan = SpawnObject(_canPrefab, m_ServerNetworkManager);
            ulong canId = serverCan.GetComponent<NetworkObject>().NetworkObjectId;
            ulong clientId = m_ClientNetworkManagers[0].LocalClientId;

            yield return WaitForConditionOrTimeOut(() => m_ClientNetworkManagers[0].SpawnManager.SpawnedObjects.ContainsKey(canId)
                && NetPlayer.Local != null && NetPlayer.Local.OwnerClientId == clientId);
            AssertOnTimeout("client never saw the can and its own player");

            var serverItem = serverCan.GetComponent<NetworkPhysicsItem>();
            NetPlayer.Local.RequestInteract(m_ClientNetworkManagers[0].SpawnManager.SpawnedObjects[canId].GetComponent<PowerCutCrew.Core.Interactable>());
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
            hostPlayerGo.GetComponent<NetPlayer>().RequestInteract(serverCan.GetComponent<PowerCutCrew.Core.Interactable>());
            yield return WaitForConditionOrTimeOut(() => serverItem.IsHeld && serverItem.HolderClientId == m_ServerNetworkManager.LocalClientId);
            AssertOnTimeout("can could not be grabbed again after the drop");
        }
    }
}
#endif
