#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using PowerCutCrew.Cables;
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
    /// Host only (no client copies): in one process all network managers share one physics world, and the clients' kinematic copies of
    /// the plug would sit exactly where the host's plug and rope end are and shove the rope, which cannot happen in a real game.
    /// </summary>
    public class CableSettleTests : NetcodeIntegrationTest
    {
        private NetPrefabCatalog _net;
        private CoreLoopCatalog _loop;
        private GameObject _ground;
        private GameObject _registry;

        protected override int NumberOfClients => 0;

        protected override void OnServerAndClientsCreated()
        {
            _net = AssetDatabase.LoadAssetAtPath<NetPrefabCatalog>("Assets/_Project/Config/NetPrefabCatalog.asset");
            _loop = Resources.Load<CoreLoopCatalog>("CoreLoopCatalog");
            m_ServerNetworkManager.AddNetworkPrefab(_net.Player.gameObject);
            m_ServerNetworkManager.AddNetworkPrefab(_loop.Generator.gameObject);
            m_ServerNetworkManager.AddNetworkPrefab(_loop.Drum.gameObject);
            m_ServerNetworkManager.AddNetworkPrefab(_loop.Socket.gameObject);
            m_ServerNetworkManager.AddNetworkPrefab(_loop.Cable.gameObject);
            NetworkPhysicsItem.ResetStatics();
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(200f, 1f, 200f);
            _registry = new GameObject("HoldRegistry");
            _registry.AddComponent<HoldRegistry>();
        }

        protected override IEnumerator OnTearDown()
        {
            NetworkPhysicsItem.ResetStatics();
            if (_ground != null) { Object.Destroy(_ground); }
            if (_registry != null) { Object.Destroy(_registry); }
            yield return base.OnTearDown();
        }

        private NetworkObject SpawnAt(NetworkObject prefab, Vector3 position)
        {
            return NetworkObject.InstantiateAndSpawn(prefab.gameObject, m_ServerNetworkManager, position: position, rotation: Quaternion.identity);
        }

        [UnityTest]
        public IEnumerator PluggedCable_RopeSettles_WithinTwoSeconds_AndStaysStillForTenSeconds()
        {
            NetworkObject playerObject = SpawnObject(_net.Player.gameObject, m_ServerNetworkManager).GetComponent<NetworkObject>();
            NetworkObject generator = SpawnAt(_loop.Generator, new Vector3(0f, 0f, 6f));
            // a socket right beside the generator: the whole 12 m of rope lies in slack heaps, the hardest thing to settle
            NetworkObject socketObject = SpawnAt(_loop.Socket, new Vector3(1.2f, 0f, 4.3f));
            socketObject.GetComponent<PowerSocket>().ServerInit(0, 5f);
            GeneratorNet gen = generator.GetComponent<GeneratorNet>();
            CableNet hostCable = null;
            for (int outlet = 0; outlet < gen.Outlets.Length; outlet++)
            {
                NetworkObject c = SpawnAt(_loop.Cable, gen.Outlets[outlet].position);
                c.GetComponent<CableNet>().HostInit(outlet);
                if (outlet == 0) { hostCable = c.GetComponent<CableNet>(); }
            }

            yield return WaitForConditionOrTimeOut(() => NetPlayer.Local != null || NetPlayer.ByClient.ContainsKey(m_ServerNetworkManager.LocalClientId));
            AssertOnTimeout("the player never spawned");
            NetPlayer player = NetPlayer.ByClient[m_ServerNetworkManager.LocalClientId];
            player.Controller.enabled = false;   // no input, no gravity: the test player only supplies poses
            foreach (Collider c in player.GetComponentsInChildren<Collider>()) { if (!(c is CharacterController)) { c.enabled = false; } }

            CableTuning tuning = CableTuning.Instance;
            var socket = socketObject.GetComponent<PowerSocket>();

            // take the plug from the reel
            player.Controller.Teleport(new Vector3(0f, 0f, 4.5f), 0f);
            yield return new WaitForSeconds(0.3f);
            player.RequestInteract(generator.GetComponentsInChildren<CableReel>(true)[0]);
            yield return WaitForConditionOrTimeOut(() => hostCable.Item.HolderClientId == m_ServerNetworkManager.LocalClientId);
            AssertOnTimeout("the plug was not taken from the reel");

            // walk the hand to the socket in small steps (about 4 m/s), then plug in
            Vector3 target = socket.PlugPoint.position + socket.PlugPoint.forward * 0.4f;
            Vector3 delta = target - player.Controller.Hand.position;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / (4f * Time.fixedDeltaTime)));
            for (int i = 0; i < steps; i++)
            {
                player.Controller.Teleport(player.transform.position + delta / steps, 0f);
                yield return new WaitForFixedUpdate();
            }
            yield return new WaitForSeconds(1f);
            player.RequestInteract(socketObject.GetComponentInChildren<SocketInteractable>());
            yield return WaitForConditionOrTimeOut(() => socket.Occupied && hostCable.PlugSeated);
            AssertOnTimeout("the plug never finished sliding into the socket");

            // nobody touches anything from here on: the rope must be at rest within 2 s and stay there for 10 s
            player.Controller.Teleport(new Vector3(18f, 0f, 18f), 0f);
            float seated = Time.time;
            float peakAfterSettle = 0f;
            float peakAt = 0f;
            float settledAt = -1f;
            while (Time.time - seated < tuning.SettleWithinSeconds + tuning.SettleStaySeconds)
            {
                float energy = hostCable.RopeKineticEnergy();
                float elapsed = Time.time - seated;
                if (settledAt < 0f && energy < tuning.SettleEnergy) { settledAt = elapsed; }
                if (elapsed >= tuning.SettleWithinSeconds && energy > peakAfterSettle) { peakAfterSettle = energy; peakAt = elapsed; }
                yield return null;
            }
            Assert.That(settledAt, Is.InRange(0f, tuning.SettleWithinSeconds), $"rope settled after {settledAt:F2} s (limit {tuning.SettleWithinSeconds} s)");
            Assert.Less(peakAfterSettle, tuning.SettleEnergy, $"rope moved again after settling (peak {peakAfterSettle:F4} J at {peakAt:F2} s, state {hostCable.State}; limit {tuning.SettleEnergy} J)");
            Assert.AreEqual(CableState.Plugged, hostCable.State, "it must not have popped out while settling");
        }
    }
}
#endif
