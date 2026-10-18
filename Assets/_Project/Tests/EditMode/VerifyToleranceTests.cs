using NUnit.Framework;
using PowerCutCrew.Net;

namespace PowerCutCrew.Tests.EditMode
{
    public class VerifyToleranceTests
    {
        [Test]
        public void ToleranceIsTheBasePlusWhatTheValueChangesDuringTheSnapshotAge()
        {
            Assert.AreEqual(4f, VerifyTolerance.For(4f, 0f, 1f), 1e-4f);
            Assert.AreEqual(4f + 6f * 1f, VerifyTolerance.For(4f, 6f, 1f), 1e-4f);
            Assert.AreEqual(4f + 6f * 1f, VerifyTolerance.For(4f, -6f, 1f), 1e-4f, "falling is the same as rising");
            Assert.AreEqual(1.5f, VerifyTolerance.For(1.5f, 20f, 0f), 1e-4f);
            Assert.AreEqual(4f, VerifyTolerance.For(4f, 10f, -1f), 1e-4f, "a negative age counts as none");
        }

        [Test]
        public void TheCaseOfTheBrief_Heat92Against98WhileRisingFast_IsWithinTheTolerance_ButASteadyGapIsNot()
        {
            // heat climbs about 7.5 per second at 15 A on a 10 A generator; a client report is about a second old
            float rate = 7.5f;
            Assert.LessOrEqual(98f - 92f, VerifyTolerance.For(4f, rate, 1f));
            // the same gap with a steady value is a real difference
            Assert.Greater(98f - 92f, VerifyTolerance.For(4f, 0f, 1f));
        }

        [Test]
        public void TheTrackerReportsTheFastestRecentChange()
        {
            var t = new RateTracker(2f, 0.25f);
            Assert.AreEqual(0f, t.MaxAbsRate(0f));
            t.Add(0f, 10f);
            Assert.AreEqual(0f, t.MaxAbsRate(0f), "one sample is not a rate");
            t.Add(0.5f, 14f);     // 8 per second
            t.Add(1.0f, 15f);     // 2 per second
            Assert.AreEqual(8f, t.MaxAbsRate(1f), 1e-3f);
            t.Add(1.1f, 99f);     // too soon after the last sample: ignored
            Assert.AreEqual(8f, t.MaxAbsRate(1.1f), 1e-3f);
            Assert.AreEqual(2f, t.MaxAbsRate(2.4f), 1e-3f, "the fast part is older than the window now");
            Assert.AreEqual(0f, t.MaxAbsRate(2.6f), 1e-3f, "and now every sample is older than the window");
            Assert.AreEqual(0f, t.MaxAbsRate(10f));
        }

        [Test]
        public void TheTrackerSeesFallingValuesToo_AndSurvivesWrappingAround()
        {
            var t = new RateTracker(100f, 0.1f);
            for (int i = 0; i < 40; i++) { t.Add(i * 0.2f, 100f - i * 3f); }   // 15 per second down, far more samples than it keeps
            Assert.AreEqual(15f, t.MaxAbsRate(8f), 1e-3f);
        }
    }
}
