using NUnit.Framework;
using PowerCutCrew.Shift;

namespace PowerCutCrew.Tests.EditMode
{
    public class TipLogicTests
    {
        private static TipInputs Calm() => new TipInputs { Active = true, FuelFraction = 1f, Heat = 0f };

        private static TipSettings Quick()
        {
            TipSettings s = TipSettings.Defaults;
            s.TipSeconds = 2f; s.GapSeconds = 0.5f;
            return s;
        }

        private static Tip? Run(TipLogic t, float seconds, TipInputs i)
        {
            Tip? started = null;
            for (float x = 0f; x < seconds - 1e-4f; x += 0.1f)
            {
                Tip? s = t.Update(0.1f, i);
                if (s.HasValue) { started = s; }
            }
            return started;
        }

        [Test]
        public void NothingHappensWhileNothingTriggers()
        {
            var t = new TipLogic(Quick());
            Assert.IsNull(Run(t, 30f, Calm()));
            Assert.IsFalse(t.Current.HasValue);
        }

        [Test]
        public void EachTriggerShowsItsTip()
        {
            Assert.AreEqual(Tip.Plug, Run(new TipLogic(Quick()), 0.2f, new TipInputs { Active = true, FuelFraction = 1f, NearFreePlug = true }));
            Assert.AreEqual(Tip.Unpowered, Run(new TipLogic(Quick()), 0.2f, new TipInputs { Active = true, FuelFraction = 1f, AnyUnpowered = true }));
            Assert.AreEqual(Tip.Fuel, Run(new TipLogic(Quick()), 0.2f, new TipInputs { Active = true, FuelFraction = 0.2f }));
            Assert.AreEqual(Tip.Overheat, Run(new TipLogic(Quick()), 0.2f, new TipInputs { Active = true, FuelFraction = 1f, Heat = 80f }));
        }

        [Test]
        public void ThresholdsAreStrict()
        {
            Assert.IsNull(Run(new TipLogic(Quick()), 1f, new TipInputs { Active = true, FuelFraction = 0.25f, Heat = 70f }));
        }

        [Test]
        public void ATipAppearsOnlyOncePerSession_EvenWhenItsTriggerComesBack()
        {
            var t = new TipLogic(Quick());
            TipInputs plug = Calm(); plug.NearFreePlug = true;
            Assert.AreEqual(Tip.Plug, Run(t, 0.5f, plug));
            Run(t, 5f, Calm());                       // it ran out
            Assert.IsFalse(t.Current.HasValue);
            Assert.IsNull(Run(t, 5f, plug), "the same tip does not come back");
            Assert.IsTrue(t.HasShown(Tip.Plug));
        }

        [Test]
        public void TipsDoNotAppearOutsideAShift_AndAreNotUsedUpThere()
        {
            var t = new TipLogic(Quick());
            TipInputs lobby = Calm(); lobby.Active = false; lobby.AnyUnpowered = true;
            Assert.IsNull(Run(t, 5f, lobby));
            Assert.IsFalse(t.HasShown(Tip.Unpowered));
            lobby.Active = true;
            Assert.AreEqual(Tip.Unpowered, Run(t, 0.5f, lobby));
        }

        [Test]
        public void OnlyOneTipAtATime_TheNextWaitsForTheGap()
        {
            var t = new TipLogic(Quick());
            TipInputs both = Calm(); both.NearFreePlug = true; both.AnyUnpowered = true;
            Assert.AreEqual(Tip.Plug, Run(t, 0.2f, both));
            Assert.AreEqual(Tip.Plug, t.Current);
            Assert.IsNull(Run(t, 1.5f, both), "still the first tip");
            Assert.AreEqual(Tip.Plug, t.Current);
            Run(t, 0.5f, both);                       // it ends
            Assert.IsFalse(t.Current.HasValue);
            Assert.AreEqual(Tip.Unpowered, Run(t, 1f, both), "the second one starts after the gap");
        }

        [Test]
        public void ATipHasASecondsLeftCountdown_AndAnotherInstanceStartsFresh()
        {
            var t = new TipLogic(Quick());
            TipInputs fuel = Calm(); fuel.FuelFraction = 0.1f;
            Run(t, 0.2f, fuel);
            Assert.That(t.SecondsLeft, Is.InRange(1.5f, 2f));
            var fresh = new TipLogic(Quick());   // a new session
            Assert.IsFalse(fresh.HasShown(Tip.Fuel));
            Assert.AreEqual(Tip.Fuel, Run(fresh, 0.2f, fuel));
        }
    
        [Test]
        public void EachBreakdownHasItsOwnTip_ShownTheFirstTimeItHappensInTheSession()
        {
            TipInputs Broken(bool sputter, bool belt, bool breaker) => new TipInputs { Active = true, FuelFraction = 1f, SputterActive = sputter, BeltActive = belt, BreakerActive = breaker };
            Assert.AreEqual(Tip.Sputter, Run(new TipLogic(Quick()), 0.2f, Broken(true, false, false)));
            Assert.AreEqual(Tip.Belt, Run(new TipLogic(Quick()), 0.2f, Broken(false, true, false)));
            Assert.AreEqual(Tip.Breaker, Run(new TipLogic(Quick()), 0.2f, Broken(false, false, true)));
            Assert.AreEqual("tip.sputter", TipLogic.Key(Tip.Sputter));
            Assert.AreEqual("tip.belt", TipLogic.Key(Tip.Belt));
            Assert.AreEqual("tip.breaker", TipLogic.Key(Tip.Breaker));

            var t = new TipLogic(Quick());
            Assert.AreEqual(Tip.Belt, Run(t, 0.2f, Broken(false, true, false)));
            Run(t, 5f, Broken(false, false, false));
            Assert.IsNull(Run(t, 5f, Broken(false, true, false)), "the second belt breakdown of the session gets no tip");
            Assert.AreEqual(Tip.Breaker, Run(t, 1f, Broken(false, true, true)), "but a kind that has not happened yet does");
            Assert.IsTrue(t.HasShown(Tip.Belt));
            Assert.IsFalse(t.HasShown(Tip.Sputter));
        }
    }
}
