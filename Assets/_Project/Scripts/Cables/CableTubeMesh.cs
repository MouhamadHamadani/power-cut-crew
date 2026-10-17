using System.Collections.Generic;
using UnityEngine;

namespace PowerCutCrew.Cables
{
    /// <summary>
    /// Builds a tube (a ring of vertices around every spline sample, joined by triangles, flat caps at both ends) into a mesh that is
    /// rebuilt every frame. Several separate tubes can go into one mesh (a snapped cable is two tubes). Plain C# apart from the mesh upload.
    /// Allocation-free once running: the vertex, normal and index arrays are sized once for the longest cable (they only grow if a
    /// longer one ever comes along) and the same arrays and the same <see cref="Mesh"/> are used every frame.
    /// </summary>
    public class CableTubeMesh
    {
        /// <summary>A read-only window on one of the arrays (no copy, no allocation per call).</summary>
        private sealed class View<T> : IReadOnlyList<T>
        {
            private T[] _array;
            private int _count;

            public void Set(T[] array, int count)
            {
                _array = array;
                _count = count;
            }

            public int Count => _count;
            public T this[int index] => _array[index];

            public IEnumerator<T> GetEnumerator()
            {
                for (int i = 0; i < _count; i++) { yield return _array[i]; }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private Vector3[] _vertices;
        private Vector3[] _normals;
        private int[] _triangles;
        private int _vertexCount;
        private int _triangleCount;
        private Vector3 _min;
        private Vector3 _max;
        private readonly View<Vector3> _vertexView = new View<Vector3>();
        private readonly View<int> _triangleView = new View<int>();
        private readonly int _sides;
        private readonly float _radius;

        /// <param name="expectedSamples">The most spline samples that will ever go into the mesh at once (all tubes together); room is made for it up front.</param>
        public CableTubeMesh(int sides, float radius, int expectedSamples = 256)
        {
            _sides = Mathf.Max(3, sides);
            _radius = radius;
            int tubes = 2;   // a snapped cable is two tubes: two ring-sets of caps
            int vertices = expectedSamples * _sides + tubes * 2;
            int indices = (expectedSamples * _sides * 2 + tubes * 2 * _sides) * 3;
            _vertices = new Vector3[vertices];
            _normals = new Vector3[vertices];
            _triangles = new int[indices];
            Clear();
        }

        public IReadOnlyList<Vector3> Vertices => _vertexView;
        public IReadOnlyList<int> Triangles => _triangleView;
        public int SidesPerRing => _sides;
        public int VertexCapacity => _vertices.Length;
        public int IndexCapacity => _triangles.Length;

        public void Clear()
        {
            _vertexCount = 0;
            _triangleCount = 0;
            _min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            _max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            _vertexView.Set(_vertices, 0);
            _triangleView.Set(_triangles, 0);
        }

        private void Reserve(int extraVertices, int extraIndices)
        {
            if (_vertexCount + extraVertices > _vertices.Length)
            {
                int size = Mathf.Max(_vertices.Length * 2, _vertexCount + extraVertices);
                System.Array.Resize(ref _vertices, size);
                System.Array.Resize(ref _normals, size);
            }
            if (_triangleCount + extraIndices > _triangles.Length)
            {
                System.Array.Resize(ref _triangles, Mathf.Max(_triangles.Length * 2, _triangleCount + extraIndices));
            }
        }

        /// <summary>Add one tube along the samples. The first ring is centred exactly on the first sample, the last on the last sample.</summary>
        public void AddTube(IReadOnlyList<Vector3> samples)
        {
            int count = samples.Count;
            if (count < 2) { return; }
            Reserve(count * _sides + 2, (((count - 1) * _sides * 2) + 2 * _sides) * 3);
            int first = _vertexCount;

            Vector3 tangent = (samples[1] - samples[0]).normalized;
            if (tangent.sqrMagnitude < 0.5f) { tangent = Vector3.forward; }
            Vector3 normal = Vector3.Cross(tangent, Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up).normalized;

            for (int i = 0; i < count; i++)
            {
                Vector3 next = samples[Mathf.Min(i + 1, count - 1)];
                Vector3 prev = samples[Mathf.Max(i - 1, 0)];
                Vector3 t = (next - prev).normalized;
                if (t.sqrMagnitude < 0.5f) { t = tangent; }
                // parallel transport: carry the ring's orientation along the curve without twisting
                normal = (Quaternion.FromToRotation(tangent, t) * normal).normalized;
                tangent = t;
                Vector3 binormal = Vector3.Cross(tangent, normal);
                Vector3 centre = samples[i];
                for (int s = 0; s < _sides; s++)
                {
                    float a = s * Mathf.PI * 2f / _sides;
                    Vector3 radial = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                    Vector3 v = centre + radial * _radius;
                    _vertices[_vertexCount] = v;
                    _normals[_vertexCount] = radial;
                    _vertexCount++;
                    _min = Vector3.Min(_min, v);
                    _max = Vector3.Max(_max, v);
                }
            }

            for (int i = 0; i < count - 1; i++)
            {
                int a = first + i * _sides;
                int b = a + _sides;
                for (int s = 0; s < _sides; s++)
                {
                    int s1 = (s + 1) % _sides;
                    _triangles[_triangleCount++] = a + s; _triangles[_triangleCount++] = b + s; _triangles[_triangleCount++] = a + s1;
                    _triangles[_triangleCount++] = a + s1; _triangles[_triangleCount++] = b + s; _triangles[_triangleCount++] = b + s1;
                }
            }

            AddCap(first, samples[0], -TangentAt(samples, 0), false);
            AddCap(first + (count - 1) * _sides, samples[count - 1], TangentAt(samples, count - 1), true);
            _vertexView.Set(_vertices, _vertexCount);
            _triangleView.Set(_triangles, _triangleCount);
        }

        private static Vector3 TangentAt(IReadOnlyList<Vector3> samples, int i)
        {
            int count = samples.Count;
            Vector3 t = (samples[Mathf.Min(i + 1, count - 1)] - samples[Mathf.Max(i - 1, 0)]).normalized;
            return t.sqrMagnitude < 0.5f ? Vector3.forward : t;
        }

        private void AddCap(int ringStart, Vector3 centre, Vector3 outward, bool end)
        {
            int c = _vertexCount;
            _vertices[_vertexCount] = centre;
            _normals[_vertexCount] = outward;
            _vertexCount++;
            for (int s = 0; s < _sides; s++)
            {
                int s1 = (s + 1) % _sides;
                if (end) { _triangles[_triangleCount++] = c; _triangles[_triangleCount++] = ringStart + s; _triangles[_triangleCount++] = ringStart + s1; }
                else { _triangles[_triangleCount++] = c; _triangles[_triangleCount++] = ringStart + s1; _triangles[_triangleCount++] = ringStart + s; }
            }
        }

        /// <summary>Upload to a mesh (created once, rebuilt every frame). Uses the arrays directly: no list, no copy, no new.</summary>
        public void Apply(Mesh mesh)
        {
            mesh.Clear();
            if (_vertexCount == 0) { return; }
            mesh.SetVertices(_vertices, 0, _vertexCount);
            mesh.SetNormals(_normals, 0, _vertexCount);
            mesh.SetTriangles(_triangles, 0, _triangleCount, 0, false);
            var bounds = new Bounds();
            bounds.SetMinMax(_min, _max);
            mesh.bounds = bounds;
        }
    }
}
