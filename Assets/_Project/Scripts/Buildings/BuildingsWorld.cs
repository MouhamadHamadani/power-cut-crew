using PowerCutCrew.Cables;
using PowerCutCrew.Net;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Buildings
{
    /// <summary>
    /// Hooks the buildings into the session: registers the Building prefab on every peer and, once the core loop's world exists on the host,
    /// spawns one building behind each socket (replacing the placeholder walls of the greybox street).
    /// </summary>
    public static class BuildingsWorld
    {
        private static BuildingsCatalog _catalog;

        /// <summary>Raised on the host after the buildings are spawned: the shift hooks in here.</summary>
        public static event System.Action<WorldSpawner> WorldBuilt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            _catalog = Resources.Load<BuildingsCatalog>("BuildingsCatalog");
            if (_catalog == null) { return; }
            NetSession.RegisterPrefabs += nm =>
            {
                nm.AddNetworkPrefab(_catalog.Building.gameObject);
                if (_catalog.ShiftManager != null) { nm.AddNetworkPrefab(_catalog.ShiftManager.gameObject); }
            };
            CoreLoopWorld.WorldBuilt += OnCoreLoopBuilt;
        }

        private static void OnCoreLoopBuilt(WorldSpawner spawner)
        {
            CoreLoopAnchors a = CoreLoopAnchors.Instance;
            if (a == null) { return; }
            // the greybox's placeholder walls make room for the real, networked buildings
            for (int i = 0; i < a.Sockets.Length; i++)
            {
                GameObject placeholder = GameObject.Find($"Building{i}");
                if (placeholder != null && placeholder.GetComponent<NetworkObject>() == null) { Object.Destroy(placeholder); }
            }
            for (int i = 0; i < a.Sockets.Length && i < BuildingTuning.Instance.Buildings.Length; i++)
            {
                Transform s = a.Sockets[i];
                // the building stands behind the socket (the socket faces the generator, so behind it is the other way)
                Vector3 position = s.position - s.forward * 2.15f;
                var rotation = Quaternion.LookRotation(-s.forward);
                NetworkObject o = spawner.Spawn(_catalog.Building, new Vector3(position.x, 0f, position.z), rotation);
                o.GetComponent<BuildingNet>().ServerInit(i);
            }
            WorldBuilt?.Invoke(spawner);
        }
    }
}
