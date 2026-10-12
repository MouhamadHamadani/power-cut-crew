using PowerCutCrew.Core;

namespace PowerCutCrew.Net
{
    /// <summary>Pick-up rule for a NetworkPhysicsItem: free, and the player holds nothing else.</summary>
    public class ItemInteractable : Interactable
    {
        private NetworkPhysicsItem _item;

        private NetworkPhysicsItem Item => _item != null ? _item : (_item = GetComponentInParent<NetworkPhysicsItem>());

        public override bool IsAvailable(in InteractionContext context)
        {
            return !Item.IsHeld && !NetworkPhysicsItem.PlayerHoldsAnything(context.PlayerId);
        }

        protected override void OnApproved(in InteractionContext context)
        {
            Item.HostGrab(context.PlayerId);
        }
    }
}
