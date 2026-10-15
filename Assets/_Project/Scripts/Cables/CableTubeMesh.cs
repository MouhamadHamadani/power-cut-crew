using System.Collections.Generic;
using UnityEngine;

namespace PowerCutCrew.Cables
{
    /// <summary>
    /// Builds a tube (a ring of vertices around every spline sample, joined by triangles, flat caps at both ends) into a mesh that is
    /// rebuilt every frame. Several separate tubes can go into one mesh (a snapped cable is two tubes). Plain C# apart from the mesh upload;
    /// the lists are reused so a rebuild does not allocate.
    /// </summary>
    public class CableTubeMesh
    {
        private readonly List<Vector3> _vertices = new List<Vector3>(1024);
        private readonly List<Vector3> _normals = new List<Vector3>(1024);
        private readonly List<int> _triangles = new List<int>(6000);
        private readonly int _sides;
        private readonly float _radius;

        public CableTubeMesh(int sides, float radius)
        {
            _sides = Mathf.Max(3, sides);
            _radius = radius;
        }

        public IReadOnlyList<Vector3> Vertices => _vertices;
        public IReadOnlyList<int> Triangles => _triangles;
        public int SidesPerRing => _sides;

        public void Clear()
        {
            _vertices.Clear();
            _normals.Clear();
            _triangles.Clear();
        }

        /// <summary>Add one tube along the samples. The first ring is centred exactly on the first sample, the last on the last sample.</summary>
        public void AddTube(IReadOnlyList<Vector3> samples)
        {
            int count = samples.Count;
            if (count < 2) { return; }
            int first = _vertices.Count;

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
                for (int s = 0; s < _sides; s++)
                {
                    float a = s * Mathf.PI * 2f / _sides;
                    Vector3 radial = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                    _vertices.Add(samples[i] + radial * _radius);
                    _normals.Add(radial);
                }
            }

            for (int i = 0; i < count - 1; i++)
            {
                int a = first + i * _sides;
                int b = a + _sides;
                for (int s = 0; s < _sides; s++)
                {
                    int s1 = (s + 1) % _sides;
                    _triangles.Add(a + s); _triangles.Add(b + s); _triangles.Add(a + s1);
                    _triangles.Add(a + s1); _triangles.Add(b + s); _triangles.Add(b + s1);
                }
            }

            AddCap(first, samples[0], -TangentAt(samples, 0), false);
            AddCap(first + (count - 1) * _sides, samples[count - 1], TangentAt(samples, count - 1), true);
        }

        private static Vector3 TangentAt(IReadOnlyList<Vector3> samples, int i)
        {
            int count = samples.Count;
            Vector3 t = (samples[Mathf.Min(i + 1, count - 1)] - samples[Mathf.Max(i - 1, 0)]).normalized;
            return t.sqrMagnitude < 0.5f ? Vector3.forward : t;
        }

        private void AddCap(int ringStart, Vector3 centre, Vector3 outward, bool end)
        {
            int c = _vertices.Count;
            _vertices.Add(centre);
            _normals.Add(outward);
            for (int s = 0; s < _sides; s++)
            {
                int s1 = (s + 1) % _sides;
                if (end) { _triangles.Add(c); _triangles.Add(ringStart + s); _triangles.Add(ringStart + s1); }
                else { _triangles.Add(c); _triangles.Add(ringStart + s1); _triangles.Add(ringStart + s); }
            }
        }

        /// <summary>Upload to a mesh (created once, rebuilt every frame).</summary>
        public void Apply(Mesh mesh)
        {
            mesh.Clear();
            if (_vertices.Count == 0) { return; }
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetTriangles(_triangles, 0, false);
            mesh.RecalculateBounds();
        }
    }
}
