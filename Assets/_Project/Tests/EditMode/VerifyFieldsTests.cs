using NUnit.Framework;
using PowerCutCrew.Net;

namespace PowerCutCrew.Tests.EditMode
{
    public class VerifyFieldsTests
    {
        [Test]
        public void AChangingFieldIsSkippedOnItsOwn_TheRestOfTheReportIsStillCompared()
        {
            var v = new VerifyFields();
            v.Field("gen.state", true, false, "state differs");   // changing: would differ, but nothing to compare yet
            v.Field("gen.fuel", false, true);
            v.Field("bld0.window", false, true);
            v.Field("bld1.powered", true, true);
            Assert.AreEqual(2, v.Compared);
            Assert.AreEqual(2, v.Skipped);
            Assert.IsFalse(v.Differs);
            Assert.IsNull(v.Diff);
            Assert.AreEqual("gen.state,bld1.powered", v.SkippedSummary);
        }

        [Test]
        public void ADifferenceInAStableFieldIsReported_EvenWhenOthersAreSkipped()
        {
            var v = new VerifyFields();
            v.Field("a", true, true);
            v.Field("b", false, false, "b: 1 vs 2");
            v.Field("c", false, false);
            Assert.IsTrue(v.Differs);
            StringAssert.Contains("b: 1 vs 2", v.Diff);
            StringAssert.Contains("c", v.Diff, "the field name is the detail when none is given");
            Assert.AreEqual(2, v.Compared);
        }

        [Test]
        public void AnEmptyReport_ComparedNothing()
        {
            var v = new VerifyFields();
            Assert.AreEqual(0, v.Compared);
            Assert.AreEqual("-", v.SkippedSummary);
        }

        [Test]
        public void ChangeClock_ASteadyValueBecomesQuiet_AChangeStartsTheQuietPeriodAgain()
        {
            var c = new ChangeClock();
            c.Observe("x", "A", 10f);
            Assert.IsTrue(c.Changing("x", 11f, 3f), "just seen");
            Assert.IsFalse(c.Changing("x", 13.5f, 3f), "unchanged for 3.5 s");
            c.Observe("x", "A", 20f);
            Assert.IsFalse(c.Changing("x", 20f, 3f), "the same value again is not a change");
            c.Observe("x", "B", 21f);
            Assert.IsTrue(c.Changing("x", 22f, 3f));
            Assert.IsFalse(c.Changing("y", 22f, 3f), "a value never seen counts as quiet");
            Assert.IsTrue(c.Changing("x", 22f, 3f) && !c.Changing("y", 22f, 3f), "per field");
        }

        [Test]
        public void ARealisticBusyStreet_StillComparesMostFields()
        {
            // 4 buildings x 5 fields + generator + shift: only the ones the host just changed are skipped
            var clock = new ChangeClock();
            for (int i = 0; i < 4; i++) { clock.Observe($"bld{i}.powered", "True", 0f); }
            clock.Observe("bld2.powered", "False", 9f);   // one building just lost power
            var v = new VerifyFields();
            for (int i = 0; i < 4; i++)
            {
                v.Field($"bld{i}.powered", clock.Changing($"bld{i}.powered", 10f, 2f), true);
                v.Field($"bld{i}.window", false, true);
                v.Field($"bld{i}.angry", false, true);
            }
            Assert.AreEqual(11, v.Compared);
            Assert.AreEqual(1, v.Skipped);
        }
    }
}
