using PowerCutCrew.Core;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Cables
{
    /// <summary>The cable reel on a generator outlet: a fresh cable's plug can be taken into the hands with E.</summary>
    public class CableReel : Interactable
    {
        [SerializeField] private int _outlet;

        public override string CurrentPromptKey
        {
            get
            {
                CableNet cable = CableNet.ForOutlet(_outlet);
                return cable != null && cable.State == CableState.Fresh && LocalHolding.Item == null ? base.CurrentPromptKey : null;
            }
        }

        public override bool IsAvailable(in InteractionContext context)
        {
            CableNet cable = CableNet.ForOutlet(_outlet);
            return cable != null && cable.State == CableState.Fresh && !cable.Item.Locked && !NetworkPhysicsItem.PlayerHoldsAnything(context.PlayerId);
        }

        protected override void OnApproved(in InteractionContext context)
        {
            CableNet.ForOutlet(_outlet).Item.HostGrab(context.PlayerId);
        }
    }
}
