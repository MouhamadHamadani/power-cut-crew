using System.Collections;
using System.Collections.Generic;
using PowerCutCrew.Net;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Breakdowns
{
    /// <summary>
    /// Hooks the breakdowns into the session: registers the wrench and belt prefabs on every peer; on the host puts the wrench on its hook when the generator spawns,
    /// hands out spare belts from the crate and clears the street's loose parts between shifts.
    /// </summary>
    public static class BreakdownWorld
    {
        public const int WrenchIndex = 50;
        private const int FirstBeltIndex = 100;

        private static BreakdownCatalog _catalog;
        private static int _nextBelt = FirstBeltIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            _catalog = Resources.Load<BreakdownCatalog>("BreakdownCatalog");
            if (_catalog == null) { return; }
            NetSession.RegisterPrefabs += nm =>
            {
                nm.AddNetworkPrefab(_catalog.Wrench.gameObject);
                nm.AddNetworkPrefab(_catalog.Belt.gameObject);
            };
        }

        /// <summary>Host: the generator is in the world, so the wrench goes on its hook (a frame later: nothing is spawned from inside a spawn).</summary>
        public static IEnumerator SpawnWrenchSoon(BreakdownNet net)
        {
            yield return null;
            if (_catalog == null || net == null || !net.IsSpawned || !net.IsServer || WrenchItem.Instance != null || !Registered(net, _catalog.Wrench)) { yield break; }
            Transform hook = net.WrenchHook != null ? net.WrenchHook : net.transform;
            NetworkObject wrench = NetworkObject.InstantiateAndSpawn(_catalog.Wrench.gameObject, net.NetworkManager, position: hook.position, rotation: hook.rotation);
            PlaceBody(wrench, hook.position, hook.rotation);
            wrench.GetComponent<NetworkPhysicsItem>().ServerInit(WrenchIndex);
            Debug.Log($"[Breakdown] the wrench hangs on its hook at {hook.position} (spawned at {wrench.transform.position})");
        }

        /// <summary>A world that did not register the breakdown prefabs (a test that only needs the generator) simply has no wrench and no belts.</summary>
        private static bool Registered(BreakdownNet net, NetworkObject prefab) => net.NetworkManager.NetworkConfig.Prefabs.Contains(prefab.gameObject);

        /// <summary>The spawned object's rigidbody starts where the prefab was made, not where it was put: without this the physics step drags the item back to the origin (inside the generator).</summary>
        private static void PlaceBody(NetworkObject spawned, Vector3 position, Quaternion rotation)
        {
            spawned.transform.SetPositionAndRotation(position, rotation);
            var rb = spawned.GetComponent<Rigidbody>();
            if (rb == null) { return; }
            rb.position = position;
            rb.rotation = rotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        /// <summary>Host: a belt out of the crate, straight into the player's hands.</summary>
        public static void HostGiveBelt(int playerId, Vector3 at)
        {
            BreakdownNet net = BreakdownNet.Instance;
            if (_catalog == null || net == null || !net.IsServer || !Registered(net, _catalog.Belt)) { return; }
            NetworkObject belt = NetworkObject.InstantiateAndSpawn(_catalog.Belt.gameObject, net.NetworkManager, position: at, rotation: Quaternion.identity);
            PlaceBody(belt, at, Quaternion.identity);
            var item = belt.GetComponent<NetworkPhysicsItem>();
            item.ServerInit(_nextBelt++);
            item.HostGrab(playerId);
            NetMetrics.Count("beltsTaken");
            Debug.Log($"[Breakdown] player {playerId} takes a spare belt from the crate");
        }

        /// <summary>Host: a belt that was fitted is used up.</summary>
        public static void HostConsume(SpareBelt belt)
        {
            if (belt == null) { return; }
            NetworkObject o = belt.Item.NetworkObject;
            if (o != null && o.IsSpawned) { o.Despawn(true); }
        }

        /// <summary>Host: no loose belts, the wrench on its hook (the street is reset between shifts).</summary>
        public static void ResetItems()
        {
            foreach (SpareBelt b in new List<SpareBelt>(SpareBelt.All))
            {
                if (b != null && b.Item != null && b.Item.IsServer) { HostConsume(b); }
            }
            _nextBelt = FirstBeltIndex;
            WrenchItem wrench = WrenchItem.Instance;
            if (wrench != null && wrench.Item.IsServer) { wrench.Item.HostResetToHome(); }
        }
    }
}
