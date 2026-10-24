using System.Collections.Generic;
using PowerCutCrew.Core;
using PowerCutCrew.Net;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Breakdowns
{
    /// <summary>The spare-parts crate next to the generator: take a belt out of it (unlimited stock, but never more than a couple lying around).</summary>
    public class SparePartsCrate : Interactable
    {
        private NetworkManager Manager => GetComponentInParent<NetworkObject>() != null ? GetComponentInParent<NetworkObject>().NetworkManager : null;

        public override string CurrentPromptKey
        {
            get
            {
                if (LocalHolding.Item != null || SpareBelt.CountIn(Manager) >= BreakdownTuning.Instance.MaxLooseBelts) { return null; }
                return base.CurrentPromptKey;
            }
        }

        public override bool IsAvailable(in InteractionContext context)
        {
            return !NetworkPhysicsItem.PlayerHoldsAnything(context.PlayerId) && SpareBelt.CountIn(Manager) < BreakdownTuning.Instance.MaxLooseBelts;
        }

        protected override void OnApproved(in InteractionContext context)
        {
            BreakdownNet net = GetComponentInParent<BreakdownNet>();
            Vector3 at = net != null && net.CrateSpawn != null ? net.CrateSpawn.position : transform.position + Vector3.up;
            BreakdownWorld.HostGiveBelt(context.PlayerId, at);
        }
    }
}
