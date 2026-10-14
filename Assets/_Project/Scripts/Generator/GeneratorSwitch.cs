using PowerCutCrew.Core;
using PowerCutCrew.Fuel;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Generator
{
    /// <summary>The pull cord: pulls it when the generator is off, turns it off when it runs.</summary>
    public class GeneratorSwitch : Interactable
    {
        public override string CurrentPromptKey
        {
            get
            {
                GeneratorNet gen = GeneratorNet.Instance;
                if (gen == null) { return null; }
                switch (gen.State)
                {
                    case GeneratorState.Running: return "gen.turn_off";
                    case GeneratorState.Overheated: return "gen.too_hot";
                    default: return "gen.pull_cord";
                }
            }
        }

        protected override void OnApproved(in InteractionContext context)
        {
            GeneratorNet gen = GeneratorNet.Instance;
            if (gen.State == GeneratorState.Running) { gen.HostStop(); }
            else { gen.HostPull(context.PlayerId); }
        }
    }
}
