using System;
using UnityEngine;

namespace PowerCutCrew.Core
{
    /// <summary>Who is asking, from where, and (optionally) with which hand pose. Passed to the host-side hooks.</summary>
    public readonly struct InteractionContext
    {
        public readonly int PlayerId;
        public readonly Vector3 PlayerPosition;
        public readonly bool HasPose;
        public readonly Vector3 HandPosition;
        public readonly Quaternion HandRotation;

        public InteractionContext(int playerId, Vector3 playerPosition)
        {
            PlayerId = playerId;
            PlayerPosition = playerPosition;
            HasPose = false;
            HandPosition = default;
            HandRotation = Quaternion.identity;
        }

        public InteractionContext(int playerId, Vector3 playerPosition, Vector3 handPosition, Quaternion handRotation)
        {
            PlayerId = playerId;
            PlayerPosition = playerPosition;
            HasPose = true;
            HandPosition = handPosition;
            HandRotation = handRotation;
        }
    }

    /// <summary>
    /// Marks an object players can interact with ("[E] Pick up", pull cord, plug in, pour fuel).
    /// Network-agnostic: the net layer sends the request, the host calls <see cref="HostTryInteract"/>
    /// (or the hold variants for "hold E" interactions).
    /// </summary>
    public class Interactable : MonoBehaviour
    {
        [SerializeField] private string _promptKey = "interact.pickup";
        [SerializeField] private float _maxRange = 3f;
        [SerializeField] private bool _hold;

        /// <summary>
        /// UI string key of the prompt as the local player sees it right now, or null when there is nothing to do
        /// (the sensor then ignores this object). Override to depend on state: generator on/off, what is in hand.
        /// </summary>
        public virtual string CurrentPromptKey => _promptKey;

        public float MaxRange => _maxRange;

        /// <summary>True for "hold E" interactions (pouring, filling).</summary>
        public bool IsHold => _hold;

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

        /// <summary>Host side: start a hold interaction. False when not allowed.</summary>
        public bool HostHoldBegin(in InteractionContext context)
        {
            if (!InRange(context.PlayerPosition) || !IsAvailable(context)) { return false; }
            OnHoldBegin(context);
            return true;
        }

        /// <summary>Host side: called every tick while the hold is active. Return false to end it.</summary>
        public bool HostHoldTick(in InteractionContext context, float deltaTime)
        {
            if (!InRange(context.PlayerPosition) || !IsAvailable(context)) { return false; }
            return OnHoldTick(context, deltaTime);
        }

        public void HostHoldEnd(in InteractionContext context)
        {
            OnHoldEnd(context);
        }

        /// <summary>Distance check against the closest point of this object's colliders, with a little slack for latency.</summary>
        public bool InRange(Vector3 playerPosition)
        {
            return DistanceTo(playerPosition) <= _maxRange * 1.5f;
        }

        public float DistanceTo(Vector3 point)
        {
            Collider c = GetComponent<Collider>();
            if (c == null) { c = GetComponentInChildren<Collider>(); }
            return c != null ? Vector3.Distance(c.ClosestPoint(point), point) : Vector3.Distance(transform.position, point);
        }

        /// <summary>Host-side hook for subclasses.</summary>
        protected virtual void OnApproved(in InteractionContext context)
        {
        }

        protected virtual void OnHoldBegin(in InteractionContext context)
        {
        }

        /// <summary>Return false to end the hold (nothing left to do).</summary>
        protected virtual bool OnHoldTick(in InteractionContext context, float deltaTime)
        {
            return true;
        }

        protected virtual void OnHoldEnd(in InteractionContext context)
        {
        }
    }
}
