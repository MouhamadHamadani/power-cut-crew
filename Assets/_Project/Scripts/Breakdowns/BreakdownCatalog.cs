using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Breakdowns
{
    /// <summary>The network prefabs of the breakdowns: the wrench and the spare belt. Loaded from Resources/BreakdownCatalog.</summary>
    public class BreakdownCatalog : ScriptableObject
    {
        public NetworkObject Wrench;
        public NetworkObject Belt;
    }
}
