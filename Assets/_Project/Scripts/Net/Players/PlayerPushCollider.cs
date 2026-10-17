using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// Kinematic capsule so the host's physics (chain, items) reacts to every player. The player's own
    /// CharacterController must not collide with it.
    /// </summary>
    public class PlayerPushCollider : MonoBehaviour
    {
        /// <summary>Every player's push capsule on this machine (cables ignore them: a kinematic capsule flings a light rope and unplugs it).</summary>
        public static readonly List<Collider> All = new List<Collider>();
        public static event Action<Collider> Added;

        private void Awake()
        {
            var cc = GetComponentInParent<CharacterController>();
            var col = GetComponent<Collider>();
            if (cc != null && col != null) { Physics.IgnoreCollision(cc, col); }
            if (col != null) { All.Add(col); Added?.Invoke(col); }
        }
        private void OnDestroy()
        {
            All.Remove(GetComponent<Collider>());
        }
    }
}
