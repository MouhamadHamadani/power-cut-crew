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
