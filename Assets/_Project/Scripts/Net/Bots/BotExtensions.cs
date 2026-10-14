using System;
using System.Collections.Generic;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// Extra bot behaviour supplied by higher assemblies (the generator / fuel / cable loop). It gets the first go every
    /// frame: when <see cref="Act"/> returns true it has taken over the frame, otherwise the base bot (wander, items) runs.
    /// </summary>
    public interface IBotBehaviour
    {
        bool Act(PlayerController me, ref PlayerFrame frame, float deltaTime);
    }

    public static class BotExtensions
    {
        /// <summary>Factories receive the bot's seed. Registered at startup by other assemblies.</summary>
        public static readonly List<Func<int, IBotBehaviour>> Factories = new List<Func<int, IBotBehaviour>>();
    }
}
