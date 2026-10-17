using System.Collections.Generic;
using NUnit.Framework;
using PowerCutCrew.Cables;
using UnityEngine;

namespace PowerCutCrew.Tests.EditMode
{
    public class CableSplineTests
    {
        private static List<Vector3> RopeLikePoints(int count, float step)
        {
            // an arc on the ground, like a cable laid round a corner
            var pts = new List<Vector3>();
            for (int i = 0; i < count; i++)
            {
                float a = i * step / 2f;
                pts.Add(new Vector3(Mathf.Cos(a) * 2f, 0.05f, Mathf.Sin(a) * 2f));
            }
            return pts;
        }

        [Test]
        public void FirstAndLastSamples_AreExactlyTheFirstAndLastPoints()
        {
            List<Vector3> pts = RopeLikePoints(32, 0.4f);
            var samples = new List<Vector3>();
            CableSpline.Sample(pts, 4, samples);
            Assert.AreEqual(pts[0], samples[0]);
            Assert.AreEqual(pts[pts.Count - 1], samples[samples.Count - 1]);
        }

        [Test]
        public void TheCurvePassesThroughEveryControlPoint()
        {
            List<Vector3> pts = RopeLikePoints(20, 0.4f);
            var samples = new List<Vector3>();
            CableSpline.Sample(pts, 4, samples);
            for (int i = 0; i < pts.Count; i++) { Assert.That(Vector3.Distance(pts[i], samples[i * 4]), Is.LessThan(1e-5f), $"point {i}"); }
        }

        [Test]
        public void SampleCount_IsSubdivisionsPerSegmentPlusTheEndPoint()
        {
            var samples = new List<Vector3>();
            CableSpline.Sample(RopeLikePoints(32, 0.4f), 4, samples);
            Assert.AreEqual(31 * 4 + 1, samples.Count);
            Assert.AreEqual(CableSpline.SampleCount(32, 4), samples.Count);
        }

        [Test]
        public void NoGaps_ConsecutiveSamplesStayCloseTogether()
        {
            List<Vector3> pts = RopeLikePoints(32, 0.4f);
            var samples = new List<Vector3>();
            CableSpline.Sample(pts, 4, samples);
            for (int i = 1; i < samples.Count; i++)
            {
                float gap = Vector3.Distance(samples[i - 1], samples[i]);
                Assert.That(gap, Is.LessThan(0.4f / 4f * 1.5f), $"gap before sample {i}");
                Assert.That(gap, Is.GreaterThan(0f), $"duplicate sample {i}");
            }
        }

        [Test]
        public void AStraightRope_StaysStraight()
        {
            var pts = new List<Vector3>();
            for (int i = 0; i < 10; i++) { pts.Add(new Vector3(i * 0.4f, 1f, 0f)); }
            var samples = new List<Vector3>();
            CableSpline.Sample(pts, 4, samples);
            foreach (Vector3 s in samples)
            {
                Assert.That(s.y, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(s.z, Is.EqualTo(0f).Within(1e-5f));
            }
        }

        [Test]
        public void TwoPoints_GiveASimpleLine_AndOnePointOrNoneAreHandled()
        {
            var samples = new List<Vector3>();
            CableSpline.Sample(new List<Vector3> { Vector3.zero, Vector3.right }, 4, samples);
            Assert.AreEqual(5, samples.Count);
            Assert.That(samples[2].x, Is.EqualTo(0.5f).Within(1e-5f));
            CableSpline.Sample(new List<Vector3> { Vector3.up }, 4, samples);
            Assert.AreEqual(1, samples.Count);
            CableSpline.Sample(new List<Vector3>(), 4, samples);
            Assert.AreEqual(0, samples.Count);
        }
    }

    public class CableSparseSplineTests
    {
        /// <summary>A rope laid in a gentle S-curve (and a tighter arc): every 2nd point is what a client receives.</summary>
        private static Vector3 Curve(float arcLength, float radius)
        {
            float a = arcLength / radius;
            return new Vector3(Mathf.Sin(a) * radius, 0.05f, (1f - Mathf.Cos(a)) * radius);
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude < 1e-12f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t);
        }

        private static float WorstDeviation(float radius, out Vector3 firstSample, out Vector3 first, out Vector3 lastSample, out Vector3 last)
        {
            const int segments = 30;
            const float segment = 0.4f;
            var all = new List<Vector3>();
            for (int i = 0; i <= segments; i++) { all.Add(Curve(i * segment, radius)); }
            var sent = new List<Vector3>();
            for (int i = 0; i <= segments; i += 2) { sent.Add(all[i]); }
            var samples = new List<Vector3>();
            CableSpline.Sample(sent, 8, samples);
            first = all[0];
            last = all[segments];
            firstSample = samples[0];
            lastSample = samples[samples.Count - 1];
            // the spline is between the received points: how far is it from the true rope at the points that were not sent?
            float worst = 0f;
            for (int i = 1; i < segments; i += 2)
            {
                float nearest = float.MaxValue;
                for (int k = 0; k + 1 < samples.Count; k++) { nearest = Mathf.Min(nearest, DistanceToSegment(all[i], samples[k], samples[k + 1])); }   // to the curve, not just to its samples
                worst = Mathf.Max(worst, nearest);
            }
            return worst;
        }

        [TestCase(6.0f)]
        [TestCase(4.0f)]
        [TestCase(3.0f)]
        public void SplineThroughEverySecondPoint_MatchesTheEndsExactly_AndStaysWithin2cmOfARopeLaidInAGentleCurve(float radius)
        {
            float worst = WorstDeviation(radius, out Vector3 firstSample, out Vector3 first, out Vector3 lastSample, out Vector3 last);
            Debug.Log($"[CableSpline] sparse (every 2nd of 31 points), curve radius {radius} m: worst deviation {worst * 100f:F2} cm");
            Assert.AreEqual(first, firstSample);
            Assert.AreEqual(last, lastSample);
            Assert.Less(worst, 0.02f, "a spline through every second rope point must stay within 2 cm of the real rope");
        }

        [TestCase(2.0f)]
        [TestCase(1.0f)]
        [TestCase(0.6f)]
        public void TightCurves_AreReportedNotAsserted(float radius)
        {
            float worst = WorstDeviation(radius, out Vector3 firstSample, out Vector3 first, out Vector3 lastSample, out Vector3 last);
            Debug.Log($"[CableSpline] sparse (every 2nd of 31 points), TIGHT curve radius {radius} m: worst deviation {worst * 100f:F2} cm");
            Assert.AreEqual(first, firstSample);
            Assert.AreEqual(last, lastSample);
        }
    }

    public class CableTubeMeshTests
    {
        [Test]
        public void TheTubeStartsAndEndsExactlyOnTheFirstAndLastSample()
        {
            var samples = new List<Vector3>();
            CableSpline.Sample(new List<Vector3> { new Vector3(0, 1, 0), new Vector3(0.4f, 1, 0.1f), new Vector3(0.8f, 1, 0.3f), new Vector3(1.2f, 1, 0.3f) }, 4, samples);
            var tube = new CableTubeMesh(6, 0.025f);
            tube.AddTube(samples);

            int sides = tube.SidesPerRing;
            Vector3 startCentre = Vector3.zero;
            Vector3 endCentre = Vector3.zero;
            for (int s = 0; s < sides; s++)
            {
                startCentre += tube.Vertices[s];
                endCentre += tube.Vertices[(samples.Count - 1) * sides + s];
            }
            Assert.That(Vector3.Distance(startCentre / sides, samples[0]), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(endCentre / sides, samples[samples.Count - 1]), Is.LessThan(1e-5f));
            // the cap vertices sit on the end points
            Assert.AreEqual(samples.Count * sides + 2, tube.Vertices.Count);
            Assert.That(Vector3.Distance(tube.Vertices[tube.Vertices.Count - 2], samples[0]), Is.LessThan(1e-6f));
            Assert.That(Vector3.Distance(tube.Vertices[tube.Vertices.Count - 1], samples[samples.Count - 1]), Is.LessThan(1e-6f));
        }

        [Test]
        public void EveryRingHasTheTubeRadius_AndTheMeshHasNoGaps()
        {
            var samples = new List<Vector3>();
            CableSpline.Sample(new List<Vector3> { Vector3.zero, new Vector3(0.4f, 0, 0), new Vector3(0.8f, 0.2f, 0.1f), new Vector3(1.2f, 0.2f, 0.4f) }, 4, samples);
            var tube = new CableTubeMesh(6, 0.025f);
            tube.AddTube(samples);
            for (int i = 0; i < samples.Count; i++)
            {
                for (int s = 0; s < 6; s++) { Assert.That(Vector3.Distance(tube.Vertices[i * 6 + s], samples[i]), Is.EqualTo(0.025f).Within(1e-4f)); }
            }
            // rings + triangles: 6 sides x 2 triangles per ring pair, plus two caps
            Assert.AreEqual(((samples.Count - 1) * 6 * 2 + 2 * 6) * 3, tube.Triangles.Count);
            foreach (int index in tube.Triangles) { Assert.That(index, Is.InRange(0, tube.Vertices.Count - 1)); }
        }

        [Test]
        public void TwoTubesInOneMesh_AreIndependent()
        {
            var a = new List<Vector3> { Vector3.zero, Vector3.right, Vector3.right * 2f };
            var b = new List<Vector3> { Vector3.up * 5f, Vector3.up * 5f + Vector3.right, Vector3.up * 5f + Vector3.right * 2f };
            var tube = new CableTubeMesh(6, 0.025f);
            tube.AddTube(a);
            int afterFirst = tube.Vertices.Count;
            tube.AddTube(b);
            Assert.AreEqual(afterFirst * 2, tube.Vertices.Count);
            tube.Clear();
            Assert.AreEqual(0, tube.Vertices.Count);
            Assert.AreEqual(0, tube.Triangles.Count);
        }

        [Test]
        public void ApplyUploadsToAMesh()
        {
            var tube = new CableTubeMesh(6, 0.025f);
            tube.AddTube(new List<Vector3> { Vector3.zero, Vector3.forward, Vector3.forward * 2f });
            var mesh = new Mesh();
            tube.Apply(mesh);
            Assert.AreEqual(tube.Vertices.Count, mesh.vertexCount);
            Assert.That(mesh.bounds.size.z, Is.EqualTo(2f).Within(0.01f));
            Object.DestroyImmediate(mesh);
        }
    }

    public class CableRebuildAllocationTests
    {
        private static void Rebuild(List<Vector3> points, List<Vector3> samples, CableTubeMesh tube, Mesh mesh, float t)
        {
            // a rope that moves a little every frame, the same work CableNet.RebuildTube does
            for (int i = 0; i < points.Count; i++) { points[i] = new Vector3(i * 0.4f, 0.05f + 0.02f * Mathf.Sin(t + i), 0.1f * Mathf.Cos(t + i * 0.3f)); }
            tube.Clear();
            CableSpline.Sample(points, 4, samples);
            tube.AddTube(samples);
            tube.Apply(mesh);
        }

        /// <summary>Managed heap growth while the action ran. It runs synchronously on the main thread, so the editor cannot allocate in between
        /// (a frame counter also counts everything else the editor does in that frame). A collection mid-run could only hide allocations, which the control below would show.</summary>
        private static long Allocated(System.Action action)
        {
            long before = System.GC.GetTotalMemory(false);
            action();
            return System.GC.GetTotalMemory(false) - before;
        }

        [Test]
        public void TheRebuildDoesNotAllocate_AndTheProbeCanSeeAllocations()
        {
            var points = new List<Vector3>(33);
            for (int i = 0; i < 32; i++) { points.Add(Vector3.zero); }
            var samples = new List<Vector3>(CableSpline.SampleCount(32, 4) + 2);
            var tube = new CableTubeMesh(6, 0.025f, CableSpline.SampleCount(32, 4) + 2);
            var mesh = new Mesh();
            for (int i = 0; i < 5; i++) { Rebuild(points, samples, tube, mesh, i * 0.1f); }   // warm up

            long idle = Allocated(() => { });                                                  // what the probe itself costs
            long rebuilds = Allocated(() => { for (int i = 0; i < 200; i++) { Rebuild(points, samples, tube, mesh, 3f + i * 0.02f); } });
            var garbage = new List<int[]>();
            long control = Allocated(() => { for (int i = 0; i < 200; i++) { garbage.Add(new int[2048]); } });   // 1.6 MB of real garbage

            Assert.Greater(control, 1000000, $"the probe does not see allocations here (control {control} bytes)");
            Assert.LessOrEqual(rebuilds, idle + 256, $"200 rebuilds allocated about {rebuilds - idle} bytes more than nothing (idle {idle}, with rebuilds {rebuilds})");
            Assert.AreEqual(200, garbage.Count);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void ARebuildIsWellUnderAMillisecond()
        {
            var points = new List<Vector3>(33);
            for (int i = 0; i < 32; i++) { points.Add(Vector3.zero); }
            var samples = new List<Vector3>(CableSpline.SampleCount(32, 4) + 2);
            var tube = new CableTubeMesh(6, 0.025f, CableSpline.SampleCount(32, 4) + 2);
            var mesh = new Mesh();
            for (int i = 0; i < 50; i++) { Rebuild(points, samples, tube, mesh, i * 0.1f); }
            var timer = new System.Diagnostics.Stopwatch();
            double worst = 0.0, sum = 0.0;
            const int runs = 500;
            for (int i = 0; i < runs; i++)
            {
                timer.Restart();
                Rebuild(points, samples, tube, mesh, i * 0.05f);
                double ms = timer.Elapsed.TotalMilliseconds;
                sum += ms;
                worst = System.Math.Max(worst, ms);
            }
            Debug.Log($"[CableTube] {runs} rebuilds of a 32-point cable: average {sum / runs:F3} ms, worst {worst:F3} ms");
            Assert.Less(sum / runs, 0.5, "the average rebuild should be far below a millisecond");
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void TheArraysAreSizedForTheLongestCable_SoTheyNeverGrowInPlay()
        {
            int samples = CableSpline.SampleCount(33, 4);
            var tube = new CableTubeMesh(6, 0.025f, samples + 2);
            int vertexCapacity = tube.VertexCapacity;
            int indexCapacity = tube.IndexCapacity;
            var line = new List<Vector3>();
            for (int i = 0; i < samples; i++) { line.Add(new Vector3(i * 0.1f, 0f, 0f)); }
            tube.AddTube(line);
            Assert.AreEqual(vertexCapacity, tube.VertexCapacity);
            Assert.AreEqual(indexCapacity, tube.IndexCapacity);
        }
    }

    public class CableTuningInvariantTests
    {
        [Test]
        public void RopeSettingsFollowTheSettleRules()
        {
            CableTuning t = Resources.Load<CableTuning>("CableTuning");
            Assert.IsNotNull(t);
            Assert.GreaterOrEqual(t.SolverIterations, 20);
            Assert.GreaterOrEqual(t.SolverVelocityIterations, 10);
            Assert.LessOrEqual(t.PlugMass / t.SegmentMass, t.MaxPlugToSegmentMassRatio);
            Assert.LessOrEqual(t.MaxPlugToSegmentMassRatio, 10f);
            Assert.Greater(t.LinearDamping, 0f);
            Assert.Greater(t.AngularDamping, 0f);
            Assert.AreEqual(0.05f, t.SlackFraction, 1e-6f);
        }

        [Test]
        public void CableMeshIsA6SidedTubeOf25mmRadiusInCableBlack()
        {
            CableTuning t = Resources.Load<CableTuning>("CableTuning");
            Assert.AreEqual(6, t.TubeSides);
            Assert.AreEqual(0.025f, t.TubeRadius, 1e-6f);
            Assert.AreEqual(4, t.SplineSubdivisions);
            Assert.AreEqual(new Color32(0x05, 0x07, 0x0F, 0xFF), t.CableColor);
        }
    }
}
