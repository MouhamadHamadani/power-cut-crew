using PowerCutCrew.Core;
using PowerCutCrew.Fuel;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Generator
{
    /// <summary>The tank cap: hold E with a can in hand to pour its fuel into the generator.</summary>
    public class GeneratorTank : Interactable
    {
        public override string CurrentPromptKey
        {
            get
            {
                GeneratorNet gen = GeneratorNet.Instance;
                var item = LocalHolding.Item as NetworkPhysicsItem;
                IFuelContainerView can = item != null ? item.GetComponent<IFuelContainerView>() : null;
                if (gen == null || can == null || can.Amount <= 0.01f || gen.Fuel >= gen.Capacity - 0.01f) { return null; }
                return base.CurrentPromptKey;
            }
        }

        private static FuelCan HeldCan(int playerId)
        {
            NetworkPhysicsItem item = NetworkPhysicsItem.HeldBy(playerId);
            return item != null ? item.GetComponent<FuelCan>() : null;
        }

        public override bool IsAvailable(in InteractionContext context)
        {
            FuelCan can = HeldCan(context.PlayerId);
            GeneratorNet gen = GeneratorNet.Instance;
            return can != null && gen != null && can.HostContainer != null && !can.HostContainer.IsEmpty && gen.Model.TankSpace > 0.01f;
        }

        protected override void OnHoldBegin(in InteractionContext context)
        {
            NetMetrics.Count("pours");
        }

        protected override bool OnHoldTick(in InteractionContext context, float deltaTime)
        {
            FuelCan can = HeldCan(context.PlayerId);
            GeneratorNet gen = GeneratorNet.Instance;
            if (can == null || gen == null) { return false; }
            float wanted = FuelTuning.Instance.PourRate * deltaTime;
            float taken = can.HostTake(Mathf.Min(wanted, gen.Model.TankSpace));
            float accepted = gen.HostAddFuel(taken);
            if (accepted < taken) { can.HostAdd(taken - accepted); }
            return !can.HostContainer.IsEmpty && gen.Model.TankSpace > 0.01f;
        }
    }
}
