using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Cables
{
    public class CoreLoopCatalog : ScriptableObject
    {
        public NetworkObject Generator;
        public NetworkObject Drum;
        public NetworkObject Socket;
        public NetworkObject Cable;
    }
}
