using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// Kinematic capsule so the host's physics (chain, items) reacts to every player. The player's own
    /// CharacterController must not collide with it.
    /// </summary>
    public class PlayerPushCollider : MonoBehaviour
    {
        private void Awake()
        {
            var cc = GetComponentInParent<CharacterController>();
            var col = GetComponent<Collider>();
            if (cc != null && col != null) { Physics.IgnoreCollision(cc, col); }
        }
    }
}
