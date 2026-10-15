using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Buildings
{
    /// <summary>The network prefabs of the buildings and the shift (Resources/BuildingsCatalog).</summary>
    public class BuildingsCatalog : ScriptableObject
    {
        public NetworkObject Building;
        public NetworkObject ShiftManager;
    }
}
