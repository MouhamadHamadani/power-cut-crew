using System.Collections.Generic;
using PowerCutCrew.Core;
using PowerCutCrew.Net;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Breakdowns
{
    /// <summary>The breaker lever on the generator panel: hold Interact to reset it after a trip. One player at a time.</summary>
    public class BreakerLever : Interactable
    {
        public override string CurrentPromptKey
        {
            get
            {
                BreakdownNet net = BreakdownNet.Instance;
                if (net == null || !net.IsSpawned || !net.IsActive(BreakdownType.Breaker) || net.LeverUp) { return null; }
                return base.CurrentPromptKey;
            }
        }

        public override bool IsAvailable(in InteractionContext context)
        {
            BreakdownNet net = GetComponentInParent<BreakdownNet>();
            return net != null && net.Model != null && net.Model.IsActive(BreakdownType.Breaker) && !net.Model.LeverUp && net.HostCanHold(BreakdownNet.LeverId, context.PlayerId);
        }

        protected override void OnHoldBegin(in InteractionContext context)
        {
            GetComponentInParent<BreakdownNet>().HostBeginReset(context.PlayerId);
        }

        protected override bool OnHoldTick(in InteractionContext context, float deltaTime)
        {
            return GetComponentInParent<BreakdownNet>().HostResetBreaker(deltaTime);
        }

        protected override void OnHoldEnd(in InteractionContext context)
        {
            GetComponentInParent<BreakdownNet>().HostEndHold(BreakdownNet.LeverId, context.PlayerId);
        }
    }
}
