using PowerCutCrew.Core;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Fuel
{
    /// <summary>The refill point: hold E with a can in hand to fill it. Infinite for now.</summary>
    public class FuelDrum : Interactable
    {
        public override string CurrentPromptKey
        {
            get
            {
                IFuelContainerView view = HeldCanView();
                return view != null && view.Amount < view.Capacity - 0.01f ? base.CurrentPromptKey : null;
            }
        }

        private static IFuelContainerView HeldCanView()
        {
            var item = LocalHolding.Item as NetworkPhysicsItem;
            return item != null ? item.GetComponent<IFuelContainerView>() : null;
        }

        public override bool IsAvailable(in InteractionContext context)
        {
            FuelCan can = HostHeldCan(context.PlayerId);
            return can != null && !can.HostContainer.IsFull;
        }

        private static FuelCan HostHeldCan(int playerId)
        {
            NetworkPhysicsItem item = NetworkPhysicsItem.HeldBy(playerId);
            return item != null ? item.GetComponent<FuelCan>() : null;
        }

        protected override void OnHoldBegin(in InteractionContext context)
        {
            NetMetrics.Count("drumFills");
        }

        protected override bool OnHoldTick(in InteractionContext context, float deltaTime)
        {
            FuelCan can = HostHeldCan(context.PlayerId);
            if (can == null) { return false; }
            can.HostAdd(FuelTuning.Instance.DrumFillRate * deltaTime);
            return !can.HostContainer.IsFull;
        }
    }
}
