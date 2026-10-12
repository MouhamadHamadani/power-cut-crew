using NUnit.Framework;
using PowerCutCrew.Core;

namespace PowerCutCrew.Tests.EditMode
{
    public class InteractionArbiterTests
    {
        [Test]
        public void SameTargetSameTick_FirstRequestWins()
        {
            var a = new InteractionArbiter();
            Assert.IsTrue(a.TryAcquire(10, playerId: 1));
            Assert.IsFalse(a.TryAcquire(10, playerId: 2));
            Assert.AreEqual(1, a.HolderOf(10));
        }

        [Test]
        public void SamePlayerCannotAcquireTwice()
        {
            var a = new InteractionArbiter();
            Assert.IsTrue(a.TryAcquire(10, 1));
            Assert.IsFalse(a.TryAcquire(10, 1));
        }

        [Test]
        public void ReleaseOnlyByHolder_ThenOtherPlayerCanAcquire()
        {
            var a = new InteractionArbiter();
            a.TryAcquire(10, 1);
            Assert.IsFalse(a.Release(10, 2));
            Assert.IsTrue(a.IsHeld(10));
            Assert.IsTrue(a.Release(10, 1));
            Assert.IsFalse(a.IsHeld(10));
            Assert.IsTrue(a.TryAcquire(10, 2));
        }

        [Test]
        public void DifferentTargets_AreIndependent()
        {
            var a = new InteractionArbiter();
            Assert.IsTrue(a.TryAcquire(10, 1));
            Assert.IsTrue(a.TryAcquire(11, 2));
            Assert.IsTrue(a.HoldsAnything(1));
            Assert.IsTrue(a.HoldsAnything(2));
            Assert.IsFalse(a.HoldsAnything(3));
        }

        [Test]
        public void ReleaseAll_FreesEverythingThePlayerHeld()
        {
            var a = new InteractionArbiter();
            a.TryAcquire(10, 1);
            a.TryAcquire(11, 1);
            a.TryAcquire(12, 2);
            var released = a.ReleaseAll(1);
            CollectionAssert.AreEquivalent(new ulong[] { 10, 11 }, released);
            Assert.IsFalse(a.IsHeld(10));
            Assert.IsFalse(a.IsHeld(11));
            Assert.IsTrue(a.IsHeld(12));
            Assert.IsTrue(a.TryAcquire(10, 3));
        }
    }
}
