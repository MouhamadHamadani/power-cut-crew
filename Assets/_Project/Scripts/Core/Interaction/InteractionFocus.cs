using System;

namespace PowerCutCrew.Core
{
    /// <summary>The interactable the local player is looking at. The player's sensor writes it, the HUD reads it.</summary>
    public static class InteractionFocus
    {
        public static Interactable Current { get; private set; }

        public static event Action<Interactable> Changed;

        public static void Set(Interactable target)
        {
            if (Current == target)
            {
                return;
            }
            Current = target;
            Changed?.Invoke(target);
        }
    }
}
