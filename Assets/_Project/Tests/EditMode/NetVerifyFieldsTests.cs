using NUnit.Framework;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Tests.EditMode
{
    /// <summary>The host compares a client's report field by field: a field the host is still changing is skipped on its own, everything else counts.</summary>
    public class NetVerifyFieldsTests
    {
        private static bool _registered;
        private static bool _breakStable;

        private static void Check(string report) =>
            NetVerifyExtras.Check(88, report, new ulong[0], new ushort[0], new int[0], new Vector3[0]);

        [SetUp]
        public void SetUp()
        {
            _breakStable = false;
            if (_registered) { return; }
            _registered = true;
            NetVerifyExtras.AddFields(() => "fieldsTest=1", (report, v) =>
            {
                if (!report.Contains("fieldsTest=1")) { return; }
                v.Field("stable.a", false, true);
                v.Field("changing.b", true, false, "b would differ");   // the host is changing it: nothing to compare yet
                v.Field("stable.c", false, !_breakStable, "c differs");
            });
        }

        [Test]
        public void AChangingFieldIsSkipped_TheStableOnesAreStillComparedAndCounted()
        {
            int compared = NetMetrics.GetCount("verifyFieldsCompared"), skipped = NetMetrics.GetCount("verifyFieldsSkipped");
            int ok = NetMetrics.GetCount("verifyExtraOk"), fail = NetMetrics.GetCount("verifyExtraFail"), wholeSkipped = NetMetrics.GetCount("verifyExtraSkipped");
            Check("fieldsTest=1");
            Assert.GreaterOrEqual(NetMetrics.GetCount("verifyFieldsCompared") - compared, 2, "stable.a and stable.c were compared");
            Assert.AreEqual(1, NetMetrics.GetCount("verifyFieldsSkipped") - skipped, "only the changing field was skipped");
            Assert.AreEqual(ok + 1, NetMetrics.GetCount("verifyExtraOk"), "the report counts as a comparison");
            Assert.AreEqual(wholeSkipped, NetMetrics.GetCount("verifyExtraSkipped"), "it was not thrown away");
            Assert.AreEqual(fail, NetMetrics.GetCount("verifyExtraFail"));
        }

        [Test]
        public void ADifferenceInAStableFieldIsStillFoundWhileAnotherFieldIsChanging()
        {
            _breakStable = true;
            int fail = NetMetrics.GetCount("verifyExtraFail");
            Check("fieldsTest=1");
            Check(NetVerifyExtras.RecheckPrefix + "fieldsTest=1");   // still different half a second later
            Assert.AreEqual(fail + 1, NetMetrics.GetCount("verifyExtraFail"));
        }
    }
}
