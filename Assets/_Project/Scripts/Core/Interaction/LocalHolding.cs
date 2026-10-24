using System;

namespace PowerCutCrew.Core
{
    /// <summary>Something a player can hold (jerrycan, cable plug). Implemented by the networked item.</summary>
    public interface IHeldItem
    {
        /// <summary>"can", "plug", ...</summary>
        string Kind { get; }
    }

    /// <summary>
    /// An item that does something when it is "used" (the use / hit button): the wrench swings. While the local player holds one, the throw button uses it instead
    /// of throwing it, and dropping it (Q) tosses it forward instead of letting it fall at the feet. Sits next to the item's network component.
    /// </summary>
    public interface IUsableItem
    {
        /// <summary>How fast (m/s) a drop launches the item forward.</summary>
        float DropSpeed { get; }

        /// <summary>Holder's machine, on a use press: play the local swing and say what was aimed at (the thing hit and where). False when there is nothing to ask the host.</summary>
        bool BeginUse(UnityEngine.Transform eye, out UnityEngine.Component target, out UnityEngine.Vector3 point);

        /// <summary>Host: a request from the player who holds the item. The host validates everything.</summary>
        void HostUse(int playerId, UnityEngine.Component target, UnityEngine.Vector3 point);
    }

    /// <summary>A container holding fuel (jerrycan, generator tank, drum).</summary>
    public interface IFuelContainerView
    {
        float Amount { get; }
        float Capacity { get; }
    }

    /// <summary>What the local player is holding right now, for prompts. Set by the networking layer.</summary>
    public static class LocalHolding
    {
        public static Func<IHeldItem> Provider;

        public static IHeldItem Item => Provider?.Invoke();

        public static bool Holds(string kind)
        {
            IHeldItem i = Item;
            return i != null && i.Kind == kind;
        }
    }
}
