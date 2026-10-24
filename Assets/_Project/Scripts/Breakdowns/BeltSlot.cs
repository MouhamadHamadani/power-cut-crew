using System.Collections.Generic;
using PowerCutCrew.Core;
using PowerCutCrew.Net;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Breakdowns
{
    /// <summary>The belt slot on the generator: hold Interact with a spare belt in hand to fit it. One player at a time.</summary>
    public class BeltSlot : Interactable
    {
        public override string CurrentPromptKey
        {
            get
            {
                BreakdownNet net = BreakdownNet.Instance;
                if (net == null || !net.IsSpawned || !net.IsActive(BreakdownType.Belt) || !LocalHolding.Holds(SpareBelt.Kind)) { return null; }
                return base.CurrentPromptKey;
            }
        }

        public override bool IsAvailable(in InteractionContext context)
        {
            BreakdownNet net = GetComponentInParent<BreakdownNet>();
            return net != null && net.Model != null && net.Model.IsActive(BreakdownType.Belt) && SpareBelt.HeldBy(context.PlayerId) != null && net.HostCanHold(BreakdownNet.SlotId, context.PlayerId);
        }

        protected override void OnHoldBegin(in InteractionContext context)
        {
            GetComponentInParent<BreakdownNet>().HostBeginBeltFit(context.PlayerId);
        }

        protected override bool OnHoldTick(in InteractionContext context, float deltaTime)
        {
            BreakdownNet net = GetComponentInParent<BreakdownNet>();
            SpareBelt belt = SpareBelt.HeldBy(context.PlayerId);
            bool going = net.HostFitBelt(deltaTime, out bool done);
            if (done && belt != null) { BreakdownWorld.HostConsume(belt); }
            return going;
        }

        protected override void OnHoldEnd(in InteractionContext context)
        {
            GetComponentInParent<BreakdownNet>().HostEndHold(BreakdownNet.SlotId, context.PlayerId);
        }
    }
}
