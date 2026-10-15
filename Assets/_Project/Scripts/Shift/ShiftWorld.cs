using PowerCutCrew.Buildings;
using PowerCutCrew.Cables;
using PowerCutCrew.Net;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Shift
{
    /// <summary>Spawns the shift manager (with the clipboard next to the generator) once the street's buildings exist on the host.</summary>
    public static class ShiftWorld
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            BuildingsWorld.WorldBuilt += OnBuildingsBuilt;
        }

        private static void OnBuildingsBuilt(WorldSpawner spawner)
        {
            var catalog = Resources.Load<BuildingsCatalog>("BuildingsCatalog");
            CoreLoopAnchors a = CoreLoopAnchors.Instance;
            if (catalog == null || catalog.ShiftManager == null || a == null) { return; }
            Vector3 position = a.Generator.position + new Vector3(1.9f, 0f, 1.3f);
            spawner.Spawn(catalog.ShiftManager, position, Quaternion.LookRotation(new Vector3(-0.6f, 0f, -1f)));
        }
    }
}
