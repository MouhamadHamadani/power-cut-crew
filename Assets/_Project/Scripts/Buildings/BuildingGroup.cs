using System.Collections.Generic;

namespace PowerCutCrew.Buildings
{
    /// <summary>The street's buildings together (plain C#, unit tested): enforces that at most <see cref="MaxRequesting"/> of them request at the same time.</summary>
    public class BuildingGroup
    {
        private readonly List<BuildingModel> _models = new List<BuildingModel>();

        public BuildingGroup(int maxRequesting)
        {
            MaxRequesting = maxRequesting;
        }

        public int MaxRequesting { get; }
        public IReadOnlyList<BuildingModel> Models => _models;
        public int Count => _models.Count;

        public void Add(BuildingModel model) => _models.Add(model);

        public int RequestingCount
        {
            get
            {
                int n = 0;
                foreach (BuildingModel m in _models)
                {
                    if (m.Requesting) { n++; }
                }
                return n;
            }
        }

        /// <summary>Total amps the buildings draw right now if all of them were powered.</summary>
        public float TotalWantedAmps
        {
            get
            {
                float sum = 0f;
                foreach (BuildingModel m in _models) { sum += m.Amps; }
                return sum;
            }
        }

        public void StartAll()
        {
            foreach (BuildingModel m in _models) { m.Start(); }
        }

        public void StopAll()
        {
            foreach (BuildingModel m in _models) { m.Stop(); }
        }

        /// <summary>
        /// Tick every building. <paramref name="powered"/>[i] is whether building i has power. Events are returned in <paramref name="events"/> (same order, cleared first).
        /// Money earned this tick is the return value.
        /// </summary>
        public float Tick(float dt, IReadOnlyList<bool> powered, BuildingEvent[] events)
        {
            float money = 0f;
            for (int i = 0; i < _models.Count; i++)
            {
                bool allow = RequestingCount < MaxRequesting;
                events[i] = _models[i].Tick(dt, powered[i], allow, out float earned);
                money += earned;
            }
            return money;
        }

        /// <summary>Over all buildings: powered Requesting seconds as a percentage of Requesting seconds (100 when nothing has requested yet).</summary>
        public float UptimePercent
        {
            get
            {
                float requesting = 0f, powered = 0f;
                foreach (BuildingModel m in _models)
                {
                    requesting += m.RequestingSeconds;
                    powered += m.PoweredRequestingSeconds;
                }
                return requesting <= 0f ? 100f : powered / requesting * 100f;
            }
        }

        public int AngryCount
        {
            get
            {
                int n = 0;
                foreach (BuildingModel m in _models)
                {
                    if (m.Angry) { n++; }
                }
                return n;
            }
        }

        public int SpikeCount
        {
            get
            {
                int n = 0;
                foreach (BuildingModel m in _models) { n += m.Spikes; }
                return n;
            }
        }
    }
}
