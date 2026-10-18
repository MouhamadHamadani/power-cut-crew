using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// How far a value that keeps moving (generator heat, fuel) may differ between the host and a client report: the base tolerance plus what the value changes
    /// during the age of the client snapshot (plain C#, unit tested). The numbers are in <see cref="NetTuning"/>.
    /// </summary>
    public static class VerifyTolerance
    {
        public static float For(float baseTolerance, float ratePerSecond, float snapshotAgeSeconds)
        {
            return baseTolerance + Mathf.Abs(ratePerSecond) * Mathf.Max(0f, snapshotAgeSeconds);
        }
    }

    /// <summary>The fastest change per second a value showed over the last couple of seconds (the host samples it now and then).</summary>
    public class RateTracker
    {
        private const int Capacity = 16;
        private readonly float[] _time = new float[Capacity];
        private readonly float[] _value = new float[Capacity];
        private int _count;
        private int _next;
        private readonly float _window;
        private readonly float _minGap;

        public RateTracker(float windowSeconds = 2f, float minGapSeconds = 0.25f)
        {
            _window = windowSeconds;
            _minGap = minGapSeconds;
        }

        public void Add(float time, float value)
        {
            if (_count > 0)
            {
                int last = (_next + Capacity - 1) % Capacity;
                if (time - _time[last] < _minGap) { return; }
            }
            _time[_next] = time;
            _value[_next] = value;
            _next = (_next + 1) % Capacity;
            _count = Mathf.Min(_count + 1, Capacity);
        }

        /// <summary>The largest |change per second| between two neighbouring samples that are not older than the window (0 with fewer than two).</summary>
        public float MaxAbsRate(float now)
        {
            float best = 0f;
            for (int i = 1; i < _count; i++)
            {
                int a = (_next + Capacity - _count + i - 1) % Capacity;
                int b = (a + 1) % Capacity;
                if (now - _time[a] > _window) { continue; }
                float dt = _time[b] - _time[a];
                if (dt > 1e-4f) { best = Mathf.Max(best, Mathf.Abs(_value[b] - _value[a]) / dt); }
            }
            return best;
        }
    }
}
