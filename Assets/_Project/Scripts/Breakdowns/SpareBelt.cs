using System.Collections.Generic;
using PowerCutCrew.Core;
using PowerCutCrew.Net;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Breakdowns
{
    /// <summary>A spare fan belt: a grabbable item from the spare-parts crate. It only counts how many are lying around (the crate refuses more than <c>MaxLooseBelts</c>).</summary>
    [RequireComponent(typeof(NetworkPhysicsItem))]
    public class SpareBelt : MonoBehaviour
    {
        public const string Kind = "belt";

        public static readonly List<SpareBelt> All = new List<SpareBelt>();

        public NetworkPhysicsItem Item { get; private set; }

        private void Awake() => Item = GetComponent<NetworkPhysicsItem>();
        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        /// <summary>How many spare belts exist in the world of this network manager (a process can hold two in tests).</summary>
        public static int CountIn(NetworkManager manager)
        {
            int n = 0;
            foreach (SpareBelt b in All)
            {
                if (b != null && b.Item != null && b.Item.IsSpawned && b.Item.NetworkManager == manager) { n++; }
            }
            return n;
        }

        /// <summary>The spare belt a player holds (host side), or null.</summary>
        public static SpareBelt HeldBy(int playerId)
        {
            NetworkPhysicsItem item = NetworkPhysicsItem.HeldBy(playerId);
            return item != null ? item.GetComponent<SpareBelt>() : null;
        }
    }
}
