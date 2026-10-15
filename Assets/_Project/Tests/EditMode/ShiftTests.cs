using NUnit.Framework;
using PowerCutCrew.Shift;

namespace PowerCutCrew.Tests.EditMode
{
    public class ShiftModelTests
    {
        private static ShiftEvent RunFor(ShiftModel m, float seconds, float step = 0.1f)
        {
            ShiftEvent all = ShiftEvent.None;
            for (float t = 0f; t < seconds - 1e-4f; t += step) { all |= m.Tick(step); }
            return all;
        }

        [Test]
        public void ItStartsInTheLobby_AndNothingHappensUntilAShiftIsStarted()
        {
            var m = new ShiftModel(600f, 3f);
            Assert.AreEqual(ShiftState.Lobby, m.State);
            Assert.AreEqual(600f, m.TimeLeft);
            Assert.AreEqual(ShiftEvent.None, RunFor(m, 30f));
            Assert.AreEqual(ShiftState.Lobby, m.State);
            Assert.AreEqual(600f, m.TimeLeft);
        }

        [Test]
        public void StartingBeginsTheCountdown3_2_1_ThenTheShiftRuns()
        {
            var m = new ShiftModel(600f, 3f);
            Assert.IsTrue(m.Start());
            Assert.AreEqual(ShiftState.Countdown, m.State);
            Assert.AreEqual(3, m.CountdownNumber);
            var shown = new System.Collections.Generic.List<int> { m.CountdownNumber };
            ShiftEvent all = ShiftEvent.None;
            for (int i = 0; i < 40 && m.State == ShiftState.Countdown; i++)
            {
                ShiftEvent e = m.Tick(0.1f);
                all |= e;
                if (m.State == ShiftState.Countdown && m.CountdownNumber != shown[shown.Count - 1]) { shown.Add(m.CountdownNumber); }
            }
            CollectionAssert.AreEqual(new[] { 3, 2, 1 }, shown);
            Assert.AreEqual(ShiftState.Running, m.State);
            Assert.IsTrue((all & ShiftEvent.Started) != 0);
            Assert.IsTrue((all & ShiftEvent.CountdownTick) != 0);
            Assert.AreEqual(0, m.CountdownNumber);
            Assert.AreEqual(600f, m.TimeLeft, 0.2f);
        }

        [Test]
        public void ASecondStartDuringAShiftIsRefused()
        {
            var m = new ShiftModel(60f, 3f);
            Assert.IsTrue(m.Start());
            Assert.IsFalse(m.Start(), "already counting down");
            RunFor(m, 3.5f);
            Assert.AreEqual(ShiftState.Running, m.State);
            Assert.IsFalse(m.Start(), "already running");
        }

        [Test]
        public void TheTimerCountsDown_AndTheShiftEndsAtZeroWithOneEndedEvent()
        {
            var m = new ShiftModel(10f, 3f);
            m.Start();
            RunFor(m, 3.2f);
            Assert.AreEqual(ShiftState.Running, m.State);
            RunFor(m, 5f);
            Assert.AreEqual(5f, m.TimeLeft, 0.3f);
            ShiftEvent e = RunFor(m, 6f);
            Assert.AreEqual(ShiftState.Summary, m.State);
            Assert.AreEqual(0f, m.TimeLeft);
            Assert.IsTrue((e & ShiftEvent.Ended) != 0);
            Assert.AreEqual(ShiftEvent.None, RunFor(m, 5f), "no second Ended");
            Assert.AreEqual(ShiftState.Summary, m.State, "stays in the summary until reset");
        }

        [Test]
        public void ResetGoesBackToTheLobby_FromAnyState_AndAShiftCanStartAgain()
        {
            var m = new ShiftModel(5f, 3f);
            m.Start();
            RunFor(m, 20f);
            Assert.AreEqual(ShiftState.Summary, m.State);
            m.Reset();
            Assert.AreEqual(ShiftState.Lobby, m.State);
            Assert.AreEqual(5f, m.TimeLeft);
            Assert.AreEqual(0, m.CountdownNumber);
            Assert.IsTrue(m.Start());
            m.Reset();   // even in the countdown
            Assert.AreEqual(ShiftState.Lobby, m.State);
        }

        [Test]
        public void UptimeIsPoweredRequestingTimeOverRequestingTime()
        {
            Assert.AreEqual(100f, ShiftModel.Uptime(0f, 0f), "nothing requested: nothing missed");
            Assert.AreEqual(50f, ShiftModel.Uptime(30f, 60f), 1e-4f);
            Assert.AreEqual(0f, ShiftModel.Uptime(0f, 80f), 1e-4f);
            Assert.AreEqual(100f, ShiftModel.Uptime(90f, 90f), 1e-4f);
            Assert.AreEqual(100f, ShiftModel.Uptime(120f, 90f), "clamped");
        }

        [TestCase(100f, ShiftVerdict.Loved)]
        [TestCase(90f, ShiftVerdict.Loved)]
        [TestCase(89.9f, ShiftVerdict.Good)]
        [TestCase(70f, ShiftVerdict.Good)]
        [TestCase(55f, ShiftVerdict.Meh)]
        [TestCase(40f, ShiftVerdict.Meh)]
        [TestCase(39.9f, ShiftVerdict.Bad)]
        [TestCase(0f, ShiftVerdict.Bad)]
        public void TheVerdictIsPickedFromTheUptime(float uptime, ShiftVerdict expected)
        {
            Assert.AreEqual(expected, ShiftModel.Verdict(uptime, 90f, 70f, 40f));
        }
    }
}
