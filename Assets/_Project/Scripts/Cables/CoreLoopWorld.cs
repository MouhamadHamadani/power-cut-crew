using PowerCutCrew.Fuel;
using PowerCutCrew.Generator;
using PowerCutCrew.Net;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Cables
{
    /// <summary>
    /// Hooks the core loop into the session: registers its network prefabs on every peer and, on the host, spawns the
    /// generator, drum, sockets, one cable per outlet and the starting fuel. Also spawns a fresh cable after a snap.
    /// </summary>
    public static class CoreLoopWorld
    {
        private static WorldSpawner _spawner;

        /// <summary>Raised on the host when the generator, drum, sockets, cables and cans are in the world: buildings and the shift hook in here.</summary>
        public static event System.Action<WorldSpawner> WorldBuilt;
        private static CoreLoopCatalog _catalog;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            _catalog = Resources.Load<CoreLoopCatalog>("CoreLoopCatalog");
            if (_catalog == null) { return; }
            NetSession.RegisterPrefabs += nm =>
            {
                nm.AddNetworkPrefab(_catalog.Generator.gameObject);
                nm.AddNetworkPrefab(_catalog.Drum.gameObject);
                nm.AddNetworkPrefab(_catalog.Socket.gameObject);
                nm.AddNetworkPrefab(_catalog.Cable.gameObject);
            };
            NetSession.WorldSpawning += OnWorldSpawning;
            CableNet.RespawnRequested += OnRespawnRequested;
        }

        private static void OnWorldSpawning(WorldSpawner spawner)
        {
            CoreLoopAnchors a = CoreLoopAnchors.Instance;
            if (a == null) { return; }
            _spawner = spawner;
            spawner.Spawn(_catalog.Generator, a.Generator.position, a.Generator.rotation);
            spawner.Spawn(_catalog.Drum, a.Drum.position, a.Drum.rotation);
            for (int i = 0; i < a.Sockets.Length; i++)
            {
                NetworkObject so = spawner.Spawn(_catalog.Socket, a.Sockets[i].position, a.Sockets[i].rotation);
                so.GetComponent<PowerSocket>().ServerInit(i, a.SocketAmps[i]);
            }
            GeneratorNet gen = GeneratorNet.Instance;
            for (int outlet = 0; outlet < gen.Outlets.Length; outlet++) { SpawnCable(outlet); }
            foreach (NetworkPhysicsItem item in NetworkPhysicsItem.All)
            {
                var can = item.GetComponent<FuelCan>();
                if (can != null && item.Index >= 0 && item.Index < a.CanStartFuel.Length) { can.HostSet(a.CanStartFuel[item.Index]); }
            }
            WorldBuilt?.Invoke(spawner);
        }

        /// <summary>
        /// Host: put the street back to the scene defaults for the next shift: everything let go, the generator off with its start fuel, a fresh cable on each reel
        /// (nothing plugged in), every can back at its spawn point with its start fuel.
        /// </summary>
        public static void ResetStreet()
        {
            CoreLoopAnchors a = CoreLoopAnchors.Instance;
            NetworkPhysicsItem.HostReleaseEverything();
            foreach (CableNet c in new System.Collections.Generic.List<CableNet>(CableNet.All))
            {
                if (c != null && c.IsServer) { c.HostRemove(); }
            }
            GeneratorNet gen = GeneratorNet.Instance;
            if (gen != null && gen.IsServer) { gen.HostReset(); }
            foreach (NetworkPhysicsItem item in new System.Collections.Generic.List<NetworkPhysicsItem>(NetworkPhysicsItem.All))
            {
                if (item == null || !item.IsServer) { continue; }
                var can = item.GetComponent<FuelCan>();
                if (can == null) { continue; }
                item.HostResetToHome();
                if (a != null && item.Index >= 0 && item.Index < a.CanStartFuel.Length) { can.HostSet(a.CanStartFuel[item.Index]); }
            }
            if (gen != null && gen.IsServer && _spawner != null)
            {
                for (int outlet = 0; outlet < gen.Outlets.Length; outlet++) { SpawnCable(outlet); }
            }
            Debug.Log("[World] street reset to the scene defaults");
        }

        private static void OnRespawnRequested(int outlet)
        {
            if (NetSession.Instance != null && NetSession.Instance.IsHost) { SpawnCable(outlet); }
        }

        private static void SpawnCable(int outlet)
        {
            GeneratorNet gen = GeneratorNet.Instance;
            if (gen == null || _spawner == null) { return; }
            Transform o = gen.Outlets[outlet];
            NetworkObject cable = _spawner.Spawn(_catalog.Cable, o.position, o.rotation);
            cable.GetComponent<CableNet>().HostInit(outlet);
            Debug.Log($"[Cable] fresh cable on outlet {outlet}");
        }
    }
}
