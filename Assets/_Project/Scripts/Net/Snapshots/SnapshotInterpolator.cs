using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// Client-side buffer for snapshots of one stream. Estimates the host/client clock offset from arrival times,
    /// drops out-of-order or duplicate snapshots, and interpolates between the two snapshots around the render time.
    /// A missing snapshot just means a longer interpolation span; past the newest snapshot the last pose is held.
    /// Plain C#, unit tested.
    /// </summary>
    public class SnapshotInterpolator
    {
        private struct Frame
        {
            public ushort Seq;
            public float HostTime;
            public Vector3[] Positions;
            public Quaternion[] Rotations;
        }

        private readonly Frame[] _frames;
        private int _head = -1;
        private int _count;
        private float _offset;
        private bool _hasOffset;

        public SnapshotInterpolator(int capacity = 32)
        {
            _frames = new Frame[capacity];
        }

        public int Count => _count;
        public bool HasData => _count > 0;
        public ushort LatestSeq => _count > 0 ? _frames[_head].Seq : (ushort)0;
        public Vector3[] LatestPositions => _count > 0 ? _frames[_head].Positions : null;

        /// <summary>True when a is newer than b, with wrap-around.</summary>
        public static bool IsNewer(ushort a, ushort b) => (short)(a - b) > 0;

        /// <summary>Returns false (and ignores the snapshot) when it is a duplicate or older than the newest one.</summary>
        public bool Push(float now, ushort seq, float hostTime, Vector3[] positions, Quaternion[] rotations)
        {
            if (_count > 0 && !IsNewer(seq, _frames[_head].Seq))
            {
                return false;
            }
            float estimate = now - hostTime;
            if (!_hasOffset)
            {
                _offset = estimate;
                _hasOffset = true;
            }
            else
            {
                // adopt a lower (earlier-arriving) offset at once, drift slowly towards a higher one
                _offset = Mathf.Lerp(_offset, estimate, estimate < _offset ? 1f : 0.02f);
            }
            _head = (_head + 1) % _frames.Length;
            _frames[_head] = new Frame
            {
                Seq = seq,
                HostTime = hostTime,
                Positions = (Vector3[])positions.Clone(),
                Rotations = rotations != null ? (Quaternion[])rotations.Clone() : null,
            };
            _count = Mathf.Min(_count + 1, _frames.Length);
            return true;
        }

        /// <summary>Sample the pose for the current time minus the interpolation buffer.</summary>
        public bool Sample(float now, float bufferSeconds, Vector3[] positions, Quaternion[] rotations)
        {
            if (_count == 0) { return false; }
            float renderTime = now - _offset - bufferSeconds;
            return SampleAt(renderTime, positions, rotations);
        }

        /// <summary>Sample at an explicit host time (used by tests).</summary>
        public bool SampleAt(float hostRenderTime, Vector3[] positions, Quaternion[] rotations)
        {
            if (_count == 0) { return false; }
            // walk from newest to oldest; a = newest frame at or before the render time, b = the next newer one
            Frame older = _frames[_head];
            Frame newer = older;
            for (int k = 0; k < _count; k++)
            {
                Frame f = _frames[(_head - k + _frames.Length) % _frames.Length];
                if (f.HostTime <= hostRenderTime)
                {
                    older = f;
                    newer = k == 0 ? f : _frames[(_head - k + 1 + _frames.Length) % _frames.Length];
                    break;
                }
                older = f;
                newer = f;
            }
            float span = newer.HostTime - older.HostTime;
            float t = span > 0.0001f ? Mathf.Clamp01((hostRenderTime - older.HostTime) / span) : 1f;
            int n = Mathf.Min(positions.Length, older.Positions.Length);
            for (int i = 0; i < n; i++)
            {
                positions[i] = Vector3.Lerp(older.Positions[i], newer.Positions[i], t);
                if (rotations != null && older.Rotations != null && newer.Rotations != null)
                {
                    rotations[i] = Quaternion.Slerp(older.Rotations[i], newer.Rotations[i], t);
                }
            }
            return true;
        }
    }
}
