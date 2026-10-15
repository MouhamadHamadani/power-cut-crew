using System.Collections.Generic;
using UnityEngine;

namespace PowerCutCrew.Cables
{
    /// <summary>
    /// Catmull-Rom spline through the rope points (plain C#, unit-tested). The curve passes through every control point, so the
    /// first and last samples are exactly the first and last points: the cable starts at the generator outlet and ends at the plug.
    /// </summary>
    public static class CableSpline
    {
        /// <summary>Number of samples <see cref="Sample"/> produces for this many control points.</summary>
        public static int SampleCount(int pointCount, int subdivisions)
        {
            return pointCount < 2 ? pointCount : (pointCount - 1) * Mathf.Max(1, subdivisions) + 1;
        }

        /// <summary>Uniform Catmull-Rom through <paramref name="points"/>, <paramref name="subdivisions"/> samples per interval, the end point included once.</summary>
        public static void Sample(IReadOnlyList<Vector3> points, int subdivisions, List<Vector3> output)
        {
            output.Clear();
            int n = points.Count;
            if (n == 0) { return; }
            if (n == 1) { output.Add(points[0]); return; }
            int sub = Mathf.Max(1, subdivisions);
            for (int i = 0; i < n - 1; i++)
            {
                // the phantom neighbours at both ends mirror the first / last interval, which keeps the ends exact and the curve smooth
                Vector3 p0 = i == 0 ? 2f * points[0] - points[1] : points[i - 1];
                Vector3 p1 = points[i];
                Vector3 p2 = points[i + 1];
                Vector3 p3 = i + 2 < n ? points[i + 2] : 2f * points[n - 1] - points[n - 2];
                for (int k = 0; k < sub; k++) { output.Add(Evaluate(p0, p1, p2, p3, k / (float)sub)); }
            }
            output.Add(points[n - 1]);
        }

        public static Vector3 Evaluate(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }
    }
}
