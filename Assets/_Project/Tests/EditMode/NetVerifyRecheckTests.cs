using NUnit.Framework;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Tests.EditMode
{
    public class NetVerifyRecheckTests
    {
        private static string _answer;
        private static bool _registered;

        private static void Check(string report) =>
            NetVerifyExtras.Check(77, report, new ulong[0], new ushort[0], new int[0], new Vector3[0]);

        [SetUp]
        public void SetUp()
        {
            if (!_registered) { _registered = true; NetVerifyExtras.Add(() => "t=1", r => _answer); }
        }

        [TearDown]
        public void TearDown() => _answer = null;   // the checker stays registered for the whole run: it must agree again for the tests that come after

        [Test]
        public void ADifferenceIsNotCountedAtFirst_AndOnlyIfItIsStillThereHalfASecondLater()
        {
            int fail = NetMetrics.GetCount("verifyExtraFail"), ok = NetMetrics.GetCount("verifyExtraOk"), recovered = NetMetrics.GetCount("verifyExtraRecovered");
            _answer = "generator heat 86 vs host 90; ";
            Check("t=1");
            Assert.AreEqual(fail, NetMetrics.GetCount("verifyExtraFail"), "the first difference is not a failure yet");
            Assert.AreEqual(ok, NetMetrics.GetCount("verifyExtraOk"));

            _answer = null;   // the host and the client agree now
            Check(NetVerifyExtras.RecheckPrefix + "t=1");
            Assert.AreEqual(fail, NetMetrics.GetCount("verifyExtraFail"));
            Assert.AreEqual(recovered + 1, NetMetrics.GetCount("verifyExtraRecovered"));

            _answer = "cable 18 Held/-1 vs host Plugged/0; ";
            Check("t=1");
            Check(NetVerifyExtras.RecheckPrefix + "t=1");   // still different
            Assert.AreEqual(fail + 1, NetMetrics.GetCount("verifyExtraFail"));
        }

        [Test]
        public void ARecheckWithoutAFirstDifferenceIsIgnored()
        {
            int fail = NetMetrics.GetCount("verifyExtraFail"), recovered = NetMetrics.GetCount("verifyExtraRecovered");
            _answer = "anything; ";
            Check(NetVerifyExtras.RecheckPrefix + "t=1");
            Assert.AreEqual(fail, NetMetrics.GetCount("verifyExtraFail"));
            Assert.AreEqual(recovered, NetMetrics.GetCount("verifyExtraRecovered"));
        }
    }
}
