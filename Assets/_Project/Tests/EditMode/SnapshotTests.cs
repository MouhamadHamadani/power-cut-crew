using NUnit.Framework;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Tests.EditMode
{
    public class SnapshotTests
    {
        private const float Extent = 32f;

        private static Vector3[] Positions(int n)
        {
            var p = new Vector3[n];
            for (int i = 0; i < n; i++) { p[i] = new Vector3(-20f + i * 4.7f, 0.3f + i * 0.9f, 15.5f - i * 3.1f); }
            return p;
        }

        [Test]
        public void QuantizeRoundTrip_IsWithinHalfAMillimetre()
        {
            for (float v = -31.9f; v < 32f; v += 0.37f)
            {
                float back = SnapshotCodec.Dequantize(SnapshotCodec.Quantize(v, Extent), Extent);
                Assert.AreEqual(v, back, 0.0007f, $"value {v}");
            }
        }

        [Test]
        public void EncodeDecode_RoundTripsPositionsAndRotations()
        {
            const int n = 8;
            Vector3[] pos = Positions(n);
            var rot = new Quaternion[n];
            for (int i = 0; i < n; i++) { rot[i] = Quaternion.Euler(10f * i, 33f * i - 90f, -17f * i); }
            byte[] data = SnapshotCodec.Encode(77UL, 513, 12.5f, true, pos, rot, n, Vector3.zero, Extent);
            Assert.AreEqual(SnapshotCodec.EncodedSize(n, true), data.Length);

            Assert.IsTrue(SnapshotCodec.TryReadHeader(data, out SnapshotHeader h));
            Assert.AreEqual(77UL, h.StreamKey);
            Assert.AreEqual(513, h.Seq);
            Assert.AreEqual(12.5f, h.HostTime, 1e-4f);
            Assert.IsTrue(h.Full);
            Assert.IsTrue(h.HasRotation);
            Assert.AreEqual(n, h.Count);

            var outPos = new Vector3[n];
            var outRot = new Quaternion[n];
            SnapshotCodec.DecodeBodies(data, h, outPos, outRot, Vector3.zero, Extent);
            for (int i = 0; i < n; i++)
            {
                Assert.Less(Vector3.Distance(pos[i], outPos[i]), 0.002f, $"position {i}");
                Assert.Less(Quaternion.Angle(rot[i], outRot[i]), 0.5f, $"rotation {i}");
            }
        }

        [Test]
        public void PositionsOnly_IsSmallerAndHasNoRotation()
        {
            Vector3[] pos = Positions(4);
            byte[] data = SnapshotCodec.Encode(1UL, 1, 0f, false, pos, null, 4, Vector3.zero, Extent);
            Assert.AreEqual(SnapshotCodec.HeaderSize + 4 * 6, data.Length);
            SnapshotCodec.TryReadHeader(data, out SnapshotHeader h);
            Assert.IsFalse(h.HasRotation);
            Assert.IsFalse(h.Full);
        }

        [Test]
        public void TruncatedOrEmptyData_IsRejected()
        {
            Assert.IsFalse(SnapshotCodec.TryReadHeader(null, out _));
            Assert.IsFalse(SnapshotCodec.TryReadHeader(new byte[5], out _));
            byte[] data = SnapshotCodec.Encode(1UL, 1, 0f, false, Positions(4), null, 4, Vector3.zero, Extent);
            var cut = new byte[data.Length - 3];
            System.Array.Copy(data, cut, cut.Length);
            Assert.IsFalse(SnapshotCodec.TryReadHeader(cut, out _));
        }

        [Test]
        public void Interpolation_BetweenTwoSnapshots_IsLinear()
        {
            var interp = new SnapshotInterpolator();
            interp.Push(0f, 1, 0f, new[] { new Vector3(0f, 0f, 0f) }, null);
            interp.Push(0.1f, 2, 0.1f, new[] { new Vector3(10f, 0f, 0f) }, null);
            var pos = new Vector3[1];
            Assert.IsTrue(interp.SampleAt(0.05f, pos, null));
            Assert.AreEqual(5f, pos[0].x, 1e-3f);
            interp.SampleAt(0.025f, pos, null);
            Assert.AreEqual(2.5f, pos[0].x, 1e-3f);
        }

        [Test]
        public void Interpolation_BeforeFirstAndAfterLast_HoldsTheEnds()
        {
            var interp = new SnapshotInterpolator();
            interp.Push(0f, 1, 1f, new[] { new Vector3(1f, 0f, 0f) }, null);
            interp.Push(0.1f, 2, 1.1f, new[] { new Vector3(2f, 0f, 0f) }, null);
            var pos = new Vector3[1];
            interp.SampleAt(0.5f, pos, null);
            Assert.AreEqual(1f, pos[0].x, 1e-3f);
            interp.SampleAt(5f, pos, null);
            Assert.AreEqual(2f, pos[0].x, 1e-3f);
        }

        [Test]
        public void MissingSnapshot_InterpolatesAcrossTheGap()
        {
            var interp = new SnapshotInterpolator();
            interp.Push(0f, 1, 0.0f, new[] { new Vector3(0f, 0f, 0f) }, null);
            interp.Push(0.3f, 4, 0.3f, new[] { new Vector3(9f, 0f, 0f) }, null);   // seq 2 and 3 never arrived
            var pos = new Vector3[1];
            interp.SampleAt(0.15f, pos, null);
            Assert.AreEqual(4.5f, pos[0].x, 1e-3f);
            Assert.AreEqual(2, interp.Count);
        }

        [Test]
        public void OutOfOrderAndDuplicateSnapshots_AreDropped()
        {
            var interp = new SnapshotInterpolator();
            Assert.IsTrue(interp.Push(0f, 5, 0.0f, new[] { Vector3.zero }, null));
            Assert.IsTrue(interp.Push(0.1f, 7, 0.1f, new[] { Vector3.right }, null));
            Assert.IsFalse(interp.Push(0.12f, 6, 0.05f, new[] { Vector3.up }, null));      // older than newest
            Assert.IsFalse(interp.Push(0.13f, 7, 0.1f, new[] { Vector3.up }, null));       // duplicate
            Assert.AreEqual(7, interp.LatestSeq);
            Assert.AreEqual(2, interp.Count);
        }

        [Test]
        public void SequenceNumbers_WrapAround()
        {
            Assert.IsTrue(SnapshotInterpolator.IsNewer(2, 65534));
            Assert.IsFalse(SnapshotInterpolator.IsNewer(65534, 2));
            var interp = new SnapshotInterpolator();
            interp.Push(0f, 65534, 0f, new[] { Vector3.zero }, null);
            Assert.IsTrue(interp.Push(0.1f, 1, 0.1f, new[] { Vector3.right }, null));
            Assert.AreEqual(1, interp.LatestSeq);
        }

        [Test]
        public void RotationInterpolation_TakesTheShortWay()
        {
            var interp = new SnapshotInterpolator();
            interp.Push(0f, 1, 0f, new[] { Vector3.zero }, new[] { Quaternion.Euler(0f, 0f, 0f) });
            interp.Push(0.1f, 2, 0.1f, new[] { Vector3.zero }, new[] { Quaternion.Euler(0f, 90f, 0f) });
            var pos = new Vector3[1];
            var rot = new Quaternion[1];
            interp.SampleAt(0.05f, pos, rot);
            Assert.AreEqual(45f, Quaternion.Angle(Quaternion.identity, rot[0]), 0.1f);
        }

        // ---------- delta mode (cables) ----------

        private static Vector3[] RopePoints(int n, float spacing)
        {
            var p = new Vector3[n];
            for (int i = 0; i < n; i++) { p[i] = new Vector3(3.3f + i * spacing * 0.9f, 0.05f + 0.03f * Mathf.Sin(i), -7.1f + i * spacing * 0.3f); }
            return p;
        }

        [Test]
        public void Delta_RoundTripsWithinHalfAStepPerAxis_AndTheErrorDoesNotAddUp()
        {
            const int n = 16;
            Vector3[] pos = RopePoints(n, 0.8f);
            byte[] data = SnapshotCodec.EncodeDelta(5UL, 9, 1.5f, false, pos, n, Vector3.zero, Extent);
            Assert.IsNotNull(data);
            Assert.IsTrue(SnapshotCodec.TryReadHeader(data, out SnapshotHeader h));
            Assert.IsTrue(h.Delta);
            Assert.AreEqual(n, h.Count);
            var back = new Vector3[n];
            SnapshotCodec.DecodeBodies(data, h, back, null, Vector3.zero, Extent);
            for (int i = 0; i < n; i++)
            {
                Assert.That(Mathf.Abs(back[i].x - pos[i].x), Is.LessThanOrEqualTo(SnapshotCodec.DeltaStep * 0.5f + 0.001f), $"x {i}");
                Assert.That(Mathf.Abs(back[i].y - pos[i].y), Is.LessThanOrEqualTo(SnapshotCodec.DeltaStep * 0.5f + 0.001f), $"y {i}");
                Assert.That(Mathf.Abs(back[i].z - pos[i].z), Is.LessThanOrEqualTo(SnapshotCodec.DeltaStep * 0.5f + 0.001f), $"z {i}");
            }
        }

        [Test]
        public void Delta_IsAboutFortyPercentSmaller_ForASixteenPointRope()
        {
            int normal = SnapshotCodec.EncodedSize(16, false);
            int delta = SnapshotCodec.EncodedSizeDelta(16);
            Assert.AreEqual(112, normal);
            Assert.AreEqual(67, delta);
            Assert.That(delta / (float)normal, Is.LessThan(0.62f));
            byte[] data = SnapshotCodec.EncodeDelta(5UL, 9, 1.5f, false, RopePoints(16, 0.8f), 16, Vector3.zero, Extent);
            Assert.AreEqual(delta, data.Length);
        }

        [Test]
        public void Delta_ReturnsNullWhenAStepIsTooBig_SoTheNormalEncodingTakesOver()
        {
            Vector3[] pos = RopePoints(6, 0.8f);
            pos[3] += new Vector3(0f, 2f, 0f);   // a point 2 m off its neighbour: more than 127 steps
            Assert.IsNull(SnapshotCodec.EncodeDelta(5UL, 9, 1.5f, false, pos, 6, Vector3.zero, Extent));
        }

        [Test]
        public void Delta_HeaderCarriesTheFlags_AndTruncatedDataIsRejected()
        {
            byte[] data = SnapshotCodec.EncodeDelta(5UL, 9, 1.5f, true, RopePoints(8, 0.8f), 8, Vector3.zero, Extent);
            Assert.IsTrue(SnapshotCodec.TryReadHeader(data, out SnapshotHeader h));
            Assert.IsTrue(h.Full);
            Assert.IsTrue(h.Delta);
            var cut = new byte[data.Length - 1];
            System.Array.Copy(data, cut, cut.Length);
            Assert.IsFalse(SnapshotCodec.TryReadHeader(cut, out _));
        }
    }
}
