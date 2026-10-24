using NUnit.Framework;
using PowerCutCrew.Net;

namespace PowerCutCrew.Tests.EditMode
{
    public class NameTagScaleTests
    {
        private const float Ref = 6f, Min = 0.6f, Max = 1.4f;

        private static float OnScreen(float distance) => NameTagScale.For(distance, Ref, Min, Max) * Ref / distance;

        [Test]
        public void AtTheReferenceDistance_TheTagKeepsItsAuthoredSize()
        {
            Assert.AreEqual(1f, NameTagScale.For(6f, Ref, Min, Max), 1e-4f);
            Assert.AreEqual(1f, OnScreen(6f), 1e-4f);
        }

        [Test]
        public void NextToTheCamera_TheTagIsNotHuge()
        {
            Assert.AreEqual(Max, OnScreen(1f), 1e-4f, "capped at the largest size on screen");
            Assert.AreEqual(Max, OnScreen(0.3f), 1e-4f);
            Assert.Less(NameTagScale.For(1f, Ref, Min, Max), 0.3f, "so the tag itself shrinks");
        }

        [Test]
        public void FarAway_TheTagStaysReadable()
        {
            Assert.AreEqual(Min, OnScreen(40f), 1e-4f);
            Assert.Greater(NameTagScale.For(40f, Ref, Min, Max), 3f);
        }

        [Test]
        public void InBetween_TheTagBehavesLikeAnyWorldObject()
        {
            Assert.AreEqual(1f, NameTagScale.For(4.5f, Ref, Min, Max), 1e-4f, "within the clamp the world scale stays 1");
            Assert.AreEqual(1f, NameTagScale.For(9f, Ref, Min, Max), 1e-4f);
        }

        [Test]
        public void ADistanceOfZeroDoesNotBlowUp()
        {
            Assert.IsFalse(float.IsNaN(NameTagScale.For(0f, Ref, Min, Max)));
            Assert.IsFalse(float.IsInfinity(NameTagScale.For(0f, Ref, Min, Max)));
        }
    }
}
