using System.Collections.Generic;
using PowerCutCrew.Generator;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Buildings
{
    /// <summary>
    /// Host only: ticks the street's buildings together (the shift calls it). It feeds each building's demand into the power grid (so the generator
    /// sees the load), asks the grid whether each building has power, pays money, shows the neighbors' speech bubbles and counts the events.
    /// </summary>
    public class BuildingDirector
    {
        private readonly BuildingGroup _group;
        private readonly List<BuildingNet> _buildings = new List<BuildingNet>();
        private readonly System.Random _rng;
        private bool[] _powered;
        private bool[] _wasUnpowered;
        private BuildingEvent[] _events;

        public BuildingDirector(int seed)
        {
            _group = new BuildingGroup(BuildingTuning.Instance.MaxRequesting);
            _rng = new System.Random(seed);
            var found = new List<BuildingNet>();
            foreach (BuildingNet b in BuildingNet.All)
            {
                if (b != null && b.IsServer && b.Model != null) { found.Add(b); }
            }
            found.Sort((a, b) => a.Index.CompareTo(b.Index));
            foreach (BuildingNet b in found)
            {
                _buildings.Add(b);
                _group.Add(b.Model);
            }
            _powered = new bool[_buildings.Count];
            _wasUnpowered = new bool[_buildings.Count];
            _events = new BuildingEvent[_buildings.Count];
        }

        public BuildingGroup Group => _group;
        public IReadOnlyList<BuildingNet> Buildings => _buildings;

        /// <summary>The shift begins: fresh schedules.</summary>
        public void Begin()
        {
            _group.StartAll();
            PublishAll();
        }

        /// <summary>The shift is over (or the street is reset): nothing requests, nothing is drawn, no speech bubbles.</summary>
        public void End()
        {
            _group.StopAll();
            GeneratorNet gen = GeneratorNet.Instance;
            foreach (BuildingNet b in _buildings)
            {
                if (gen != null) { gen.Grid.SetDraw(b.Index, 0f); }
                b.HostClearBubble();
            }
            PublishAll();
        }

        private void PublishAll()
        {
            foreach (BuildingNet b in _buildings) { b.HostPublishNow(); }
        }

        /// <summary>Advance the buildings by dt. Returns the money they paid in this tick.</summary>
        public float Tick(float dt)
        {
            GeneratorNet gen = GeneratorNet.Instance;
            for (int i = 0; i < _buildings.Count; i++)
            {
                BuildingNet b = _buildings[i];
                _powered[i] = gen != null && gen.Grid.IsPowered(b.Index, gen.IsRunning);
            }
            float money = _group.Tick(dt, _powered, _events);
            for (int i = 0; i < _buildings.Count; i++)
            {
                BuildingNet b = _buildings[i];
                if (gen != null) { gen.Grid.SetDraw(b.Index, b.Model.Amps); }
                HandleEvents(b, _events[i]);

                // a Requesting building that was in the dark and now has power again: somebody re-plugged it
                bool dark = b.Model.Requesting && !_powered[i];
                if (_wasUnpowered[i] && b.Model.Requesting && _powered[i]) { NetMetrics.Count("replugs"); }
                _wasUnpowered[i] = dark;
                b.HostPublish();
            }
            return money;
        }

        private void HandleEvents(BuildingNet b, BuildingEvent e)
        {
            if (e == BuildingEvent.None) { return; }
            if ((e & BuildingEvent.SpikeStarted) != 0) { NetMetrics.Count("spikes"); }
            if ((e & BuildingEvent.BecameAngry) != 0) { NetMetrics.Count("angryBuildings"); }
            // one line at a time per building: the most important trigger wins
            if ((e & BuildingEvent.BecameAngry) != 0) { b.HostShowBubble(BubbleTrigger.Angry, _rng); }
            else if ((e & BuildingEvent.SpikeStarted) != 0) { b.HostShowBubble(BubbleTrigger.Spike, _rng); }
            else if ((e & BuildingEvent.UnpoweredLong) != 0) { b.HostShowBubble(BubbleTrigger.Unpowered, _rng); }
            else if ((e & BuildingEvent.RequestStarted) != 0) { b.HostShowBubble(BubbleTrigger.Request, _rng); }
        }
    }
}
