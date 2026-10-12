using System;
using UnityEngine;

namespace PowerCutCrew.Core
{
    /// <summary>Who is asking, and from where. Passed to the host-side hook.</summary>
    public readonly struct InteractionContext
    {
        public readonly int PlayerId;
        public readonly Vector3 PlayerPosition;

        public InteractionContext(int playerId, Vector3 playerPosition)
        {
            PlayerId = playerId;
            PlayerPosition = playerPosition;
        }
    }

    /// <summary>
    /// Marks an object players can interact with ("[E] Pick up", refuel, plug cable, fix breakdown).
    /// Network-agnostic: the net layer sends the request, the host calls <see cref="HostTryInteract"/>.
    /// </summary>
    public class Interactable : MonoBehaviour
    {
        [SerializeField] private string _promptKey = "interact.pickup";
        [SerializeField] private float _maxRange = 3f;

        /// <summary>Key in the UI string table shown as the prompt.</summary>
        public string PromptKey => _promptKey;

        public float MaxRange => _maxRange;

        /// <summary>Raised on the host when an interaction request is approved.</summary>
        public event Action<InteractionContext> Approved;

        /// <summary>Override to add rules (already held, out of fuel, ...). Called on the host.</summary>
        public virtual bool IsAvailable(in InteractionContext context)
        {
            return true;
        }

        /// <summary>Host side: validate range and availability, then run the hook.</summary>
        public bool HostTryInteract(in InteractionContext context)
        {
            if (!InRange(context.PlayerPosition) || !IsAvailable(context))
            {
                return false;
            }
            OnApproved(context);
            Approved?.Invoke(context);
            return true;
        }

        /// <summary>Distance check against the closest point of this object's colliders, with a little slack for latency.</summary>
        public bool InRange(Vector3 playerPosition)
        {
            return DistanceTo(playerPosition) <= _maxRange * 1.5f;
        }

        public float DistanceTo(Vector3 point)
        {
            Collider c = GetComponentInChildren<Collider>();
            return c != null ? Vector3.Distance(c.ClosestPoint(point), point) : Vector3.Distance(transform.position, point);
        }

        /// <summary>Host-side hook for subclasses.</summary>
        protected virtual void OnApproved(in InteractionContext context)
        {
        }
    }
}
