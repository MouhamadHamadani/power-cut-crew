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
