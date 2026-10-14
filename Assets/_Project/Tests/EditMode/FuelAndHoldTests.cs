using NUnit.Framework;
using PowerCutCrew.Core;
using PowerCutCrew.Fuel;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Tests.EditMode
{
    public class FuelContainerTests
    {
        [Test]
        public void AddAndTake_AreClamped()
        {
            var c = new FuelContainer(35f, 30f);
            Assert.AreEqual(5f, c.Add(10f), 1e-4f);
            Assert.IsTrue(c.IsFull);
            Assert.AreEqual(0f, c.Add(1f), 1e-4f);
            Assert.AreEqual(35f, c.Take(100f), 1e-4f);
            Assert.IsTrue(c.IsEmpty);
            Assert.AreEqual(0f, c.Take(1f), 1e-4f);
        }

        [Test]
        public void Transfer_MovesAtTheRate()
        {
            var can = new FuelContainer(35f, 35f);
            var tank = new FuelContainer(100f, 0f);
            float moved = FuelContainer.Transfer(can, tank, 10f, 0.5f);   // 10 units/s for half a second
            Assert.AreEqual(5f, moved, 1e-4f);
            Assert.AreEqual(30f, can.Amount, 1e-4f);
            Assert.AreEqual(5f, tank.Amount, 1e-4f);
        }

        [Test]
        public void Transfer_StopsWhenTheSourceIsEmpty()
        {
            var can = new FuelContainer(35f, 2f);
            var tank = new FuelContainer(100f, 0f);
            Assert.AreEqual(2f, FuelContainer.Transfer(can, tank, 10f, 1f), 1e-4f);
            Assert.AreEqual(0f, FuelContainer.Transfer(can, tank, 10f, 1f), 1e-4f);
            Assert.IsTrue(can.IsEmpty);
        }

        [Test]
        public void Transfer_StopsWhenTheTargetIsFull()
        {
            var can = new FuelContainer(35f, 35f);
            var tank = new FuelContainer(100f, 98f);
            Assert.AreEqual(2f, FuelContainer.Transfer(can, tank, 10f, 1f), 1e-4f);
            Assert.IsTrue(tank.IsFull);
            Assert.AreEqual(0f, FuelContainer.Transfer(can, tank, 10f, 1f), 1e-4f);
            Assert.AreEqual(33f, can.Amount, 1e-4f);
        }

        [Test]
        public void DrumFill_AtFifteenPerSecond_FillsAnEmptyCanInAboutTwoPointThreeSeconds()
        {
            var can = new FuelContainer(35f, 0f);
            float t = 0f;
            while (!can.IsFull && t < 10f)
            {
                can.Add(15f * 0.02f);
                t += 0.02f;
            }
            Assert.AreEqual(35f / 15f, t, 0.05f);
        }

        [Test]
        public void Fill01_TracksTheAmount()
        {
            var c = new FuelContainer(40f, 10f);
            Assert.AreEqual(0.25f, c.Fill01, 1e-4f);
        }
    }

    public class HoldKeepAliveTests
    {
        [Test]
        public void KeepAlives_KeepAHoldAlive_AndItExpiresHalfASecondAfterTheLast()
        {
            var h = new HoldKeepAlive(0.5f);
            h.Begin(1, 10, 0.0);
            Assert.IsTrue(h.KeepAlive(1, 10, 0.2));
            Assert.IsTrue(h.KeepAlive(1, 10, 0.4));
            Assert.IsEmpty(h.CollectExpired(0.8));
            Assert.AreEqual(1, h.CollectExpired(0.95).Count, "0.55 s without a keep-alive");
            Assert.IsFalse(h.IsActive(1, 10));
        }

        [Test]
        public void ExplicitEnd_RemovesTheHold()
        {
            var h = new HoldKeepAlive(0.5f);
            h.Begin(1, 10, 0.0);
            Assert.IsTrue(h.End(1, 10));
            Assert.IsFalse(h.End(1, 10));
            Assert.IsEmpty(h.CollectExpired(5.0));
        }

        [Test]
        public void KeepAliveAfterExpiry_IsRejected()
        {
            var h = new HoldKeepAlive(0.5f);
            h.Begin(1, 10, 0.0);
            h.CollectExpired(2.0);
            Assert.IsFalse(h.KeepAlive(1, 10, 2.1));
        }

        [Test]
        public void SeveralPlayersCanHoldTheSameTarget()
        {
            var h = new HoldKeepAlive(0.5f);
            h.Begin(1, 10, 0.0);
            h.Begin(2, 10, 0.0);
            h.KeepAlive(2, 10, 0.4);
            var expired = h.CollectExpired(0.7);
            Assert.AreEqual(1, expired.Count);
            Assert.AreEqual(1, expired[0].player);
            Assert.IsTrue(h.IsActive(2, 10));
        }

        [Test]
        public void RemoveAllForPlayer_DropsOnlyThatPlayersHolds()
        {
            var h = new HoldKeepAlive(0.5f);
            h.Begin(1, 10, 0.0);
            h.Begin(1, 11, 0.0);
            h.Begin(2, 10, 0.0);
            Assert.AreEqual(2, h.RemoveAllForPlayer(1).Count);
            Assert.AreEqual(1, h.Count);
        }
    }

    public class ReleasePoseRuleTests
    {
        [Test]
        public void PoseWithinTolerance_IsAccepted()
        {
            Assert.IsTrue(ReleasePoseRule.Accept(new Vector3(2f, 1.3f, 0f), new Vector3(0f, 1.3f, 0f), 2.5f));
            Assert.IsTrue(ReleasePoseRule.Accept(new Vector3(2.5f, 0f, 0f), Vector3.zero, 2.5f), "exactly at the tolerance");
        }

        [Test]
        public void PoseBeyondTolerance_IsRejected()
        {
            Assert.IsFalse(ReleasePoseRule.Accept(new Vector3(2.6f, 0f, 0f), Vector3.zero, 2.5f));
            Assert.IsFalse(ReleasePoseRule.Accept(new Vector3(100f, 0f, 0f), Vector3.zero, 2.5f));
        }

        [Test]
        public void NanPose_IsRejected()
        {
            Assert.IsFalse(ReleasePoseRule.Accept(new Vector3(float.NaN, 0f, 0f), Vector3.zero, 2.5f));
        }

        [Test]
        public void ToleranceComesFromTheTuningDefault()
        {
            var tuning = ScriptableObject.CreateInstance<NetTuning>();
            Assert.AreEqual(2.5f, tuning.ReleasePoseTolerance, 1e-4f);
            Object.DestroyImmediate(tuning);
        }

        [Test]
        public void ThrowVelocity_IsClampedToTheMaximum()
        {
            Vector3 v = ReleasePoseRule.ClampVelocity(new Vector3(0f, 0f, 500f), 10f);
            Assert.AreEqual(10f, v.magnitude, 1e-3f);
            Assert.AreEqual(Vector3.zero, ReleasePoseRule.ClampVelocity(new Vector3(float.NaN, 0f, 0f), 10f));
            Assert.AreEqual(3f, ReleasePoseRule.ClampVelocity(new Vector3(0f, 3f, 0f), 10f).magnitude, 1e-4f);
        }
    }
}
