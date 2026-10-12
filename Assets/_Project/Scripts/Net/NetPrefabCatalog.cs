using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>Network prefabs the session registers and spawns.</summary>
    [CreateAssetMenu(menuName = "Power Cut Crew/Config/Net Prefab Catalog", fileName = "NetPrefabCatalog")]
    public class NetPrefabCatalog : ScriptableObject
    {
        public NetworkObject Player;
        public NetworkObject Can;
        public NetworkObject Chain;
    }
}
