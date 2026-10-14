using System;

namespace PowerCutCrew.Core
{
    /// <summary>Something a player can hold (jerrycan, cable plug). Implemented by the networked item.</summary>
    public interface IHeldItem
    {
        /// <summary>"can", "plug", ...</summary>
        string Kind { get; }
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
