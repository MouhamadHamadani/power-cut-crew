using PowerCutCrew.Core;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Cables
{
    /// <summary>"[E] Plug in" with the plug in hand, "[E] Unplug" with empty hands on an occupied socket.</summary>
    public class SocketInteractable : Interactable
    {
        private PowerSocket _socket;

        private PowerSocket Socket => _socket != null ? _socket : (_socket = GetComponentInParent<PowerSocket>());

        public override string CurrentPromptKey
        {
            get
            {
                PowerSocket s = Socket;
                if (s == null) { return null; }
                if (LocalHolding.Holds("plug") && !s.Occupied) { return "cable.plug_in"; }
                if (s.Occupied && LocalHolding.Item == null) { return "cable.unplug"; }
                return null;
            }
        }

        public override bool IsAvailable(in InteractionContext context)
        {
            PowerSocket s = Socket;
            if (s.Occupied) { return !NetworkPhysicsItem.PlayerHoldsAnything(context.PlayerId); }
            NetworkPhysicsItem held = NetworkPhysicsItem.HeldBy(context.PlayerId);
            CableNet cable = held != null ? held.GetComponent<CableNet>() : null;
            float distance = context.HasPose ? Vector3.Distance(context.HandPosition, s.PlugPoint.position) : float.MaxValue;
            PlugInResult r = PlugRules.Validate(cable != null, context.HasPose, distance, CableTuning.Instance.PlugReach, !s.Occupied,
                cable != null && cable.State != CableState.Snapped);
            if (r != PlugInResult.Ok) { NetMetrics.Count("plugInRejected"); }
            return r == PlugInResult.Ok;
        }

        protected override void OnApproved(in InteractionContext context)
        {
            PowerSocket s = Socket;
            if (s.Occupied) { s.PluggedCable.HostUnplug(context.PlayerId); return; }
            NetworkPhysicsItem held = NetworkPhysicsItem.HeldBy(context.PlayerId);
            held.GetComponent<CableNet>().HostPlugIn(s);
        }
    }
}
