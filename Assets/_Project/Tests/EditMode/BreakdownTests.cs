using System.Collections.Generic;
using NUnit.Framework;
using PowerCutCrew.Breakdowns;
using PowerCutCrew.Generator;
using UnityEngine;

namespace PowerCutCrew.Tests.EditMode
{
    public class BreakdownDirectorTests
    {
        private static DirectorInputs Normal(byte active = 0) => new DirectorInputs
        {
            ShiftRunning = true, GeneratorRunning = true, FuelFraction = 0.6f, Heat = 30f, LoadAmps = 6f, RatedAmps = 10f, ActiveMask = active,
        };

        /// <summary>Settings where something happens at once, to test the gates one at a time.</summary>
        private static BreakdownSettings Certain()
        {
            BreakdownSettings s = BreakdownSettings.Defaults;
            s.SputterChancePerSecond = s.BeltChancePerSecond = s.BreakerChancePerSecond = 1f;
            return s;
        }

        private static List<(float time, BreakdownType type)> Run(BreakdownDirector d, float seconds, DirectorInputs inputs, float step = 0.1f, bool fixAfterEach = false)
        {
            var list = new List<(float, BreakdownType)>();
            float t = 0f;
            byte active = 0;
            float fixAt = float.MaxValue;
            for (; t < seconds; t += step)
            {
                if (fixAfterEach && t >= fixAt) { active = 0; fixAt = float.MaxValue; }
                inputs.ActiveMask = active;
                BreakdownType? started = d.Tick(step, inputs);
                if (started.HasValue)
                {
                    list.Add((t, started.Value));
                    active |= BreakdownTypes.Bit(started.Value);
                    fixAt = t + 20f;
                }
            }
            return list;
        }

        [Test]
        public void NothingHappensInTheGracePeriod()
        {
            var d = new BreakdownDirector(Certain(), 1);
            Assert.IsEmpty(Run(d, 59.5f, Normal()));
            Assert.AreEqual(0, d.Started);
        }

        [Test]
        public void AfterTheGracePeriodSomethingCanHappen()
        {
            var d = new BreakdownDirector(Certain(), 1);
            List<(float time, BreakdownType type)> all = Run(d, 70f, Normal());
            Assert.AreEqual(1, all.Count);
            Assert.GreaterOrEqual(all[0].time, 59f);
            Assert.Less(all[0].time, 62f, "with certain chances the first one comes as soon as the grace period is over");
        }

        [Test]
        public void TwoNewBreakdownsAreAtLeastTheMinimumGapApart()
        {
            BreakdownSettings s = Certain();
            s.MaxActive = 3;
            var d = new BreakdownDirector(s, 2);
            List<(float time, BreakdownType type)> all = Run(d, 300f, Normal(), 0.1f, true);
            Assert.GreaterOrEqual(all.Count, 3);
            for (int i = 1; i < all.Count; i++) { Assert.GreaterOrEqual(all[i].time - all[i - 1].time, 39.9f, $"breakdown {i} came too soon after {i - 1}"); }
        }

        [Test]
        public void NoMoreThanTheMaximumAreActive_AndNeverTwoOfAKind()
        {
            BreakdownSettings s = Certain();
            s.MinGapSeconds = 0f;
            var d = new BreakdownDirector(s, 3);
            byte active = 0;
            var seen = new List<BreakdownType>();
            for (float t = 0f; t < 200f; t += 0.5f)
            {
                BreakdownType? started = d.Tick(0.5f, Normal(active));
                if (!started.HasValue) { continue; }
                Assert.IsFalse(BreakdownTypes.Has(active, started.Value), "a kind that is already active cannot start again");
                active |= BreakdownTypes.Bit(started.Value);
                seen.Add(started.Value);
                Assert.LessOrEqual(BreakdownTypes.CountOf(active), 2);
            }
            Assert.AreEqual(2, seen.Count, "with nothing fixed, the third never starts");
            Assert.AreEqual(2, BreakdownTypes.CountOf(active));
        }

        [Test]
        public void OnlyWhileTheShiftRunsAndTheGeneratorRuns()
        {
            var d = new BreakdownDirector(Certain(), 4);
            DirectorInputs lobby = Normal();
            lobby.ShiftRunning = false;
            Assert.IsEmpty(Run(d, 200f, lobby), "no shift");
            Assert.AreEqual(0f, d.ShiftSeconds, "the grace period only counts while the shift runs");
            DirectorInputs off = Normal();
            off.GeneratorRunning = false;
            Assert.IsEmpty(Run(d, 200f, off), "the generator is off");
            Assert.Greater(d.ShiftSeconds, 199f, "the shift time still passes");
            Assert.IsNotEmpty(Run(d, 5f, Normal()), "running again: the grace period is long over");
        }

        [Test]
        public void TheChancesGrowWithLowFuelHeatAndOverload()
        {
            BreakdownSettings s = BreakdownSettings.Defaults;
            DirectorInputs n = Normal();
            Assert.AreEqual(0.004f, BreakdownDirector.Chance(s, BreakdownType.Sputter, n), 1e-6f);
            Assert.AreEqual(0.003f, BreakdownDirector.Chance(s, BreakdownType.Belt, n), 1e-6f);
            Assert.AreEqual(0.002f, BreakdownDirector.Chance(s, BreakdownType.Breaker, n), 1e-6f);

            DirectorInputs lowFuel = n;
            lowFuel.FuelFraction = 0.2f;
            Assert.AreEqual(0.012f, BreakdownDirector.Chance(s, BreakdownType.Sputter, lowFuel), 1e-6f, "x3 below 25 % fuel");
            Assert.AreEqual(0.003f, BreakdownDirector.Chance(s, BreakdownType.Belt, lowFuel), 1e-6f, "only the sputter cares about fuel");
            lowFuel.FuelFraction = 0.25f;
            Assert.AreEqual(0.004f, BreakdownDirector.Chance(s, BreakdownType.Sputter, lowFuel), 1e-6f, "exactly 25 % is not low");

            DirectorInputs hot = n;
            hot.Heat = 71f;
            Assert.AreEqual(0.009f, BreakdownDirector.Chance(s, BreakdownType.Belt, hot), 1e-6f, "x3 above heat 70");
            hot.Heat = 70f;
            Assert.AreEqual(0.003f, BreakdownDirector.Chance(s, BreakdownType.Belt, hot), 1e-6f);

            DirectorInputs over = n;
            over.LoadAmps = 10.5f;
            Assert.AreEqual(0.012f, BreakdownDirector.Chance(s, BreakdownType.Breaker, over), 1e-6f, "x6 while the load is above the rated amps");
            over.LoadAmps = 10f;
            Assert.AreEqual(0.002f, BreakdownDirector.Chance(s, BreakdownType.Breaker, over), 1e-6f);
        }

        [Test]
        public void TheSameSeedGivesTheSameBreakdowns_AnotherSeedUsuallyDoesNot()
        {
            BreakdownSettings s = BreakdownSettings.Defaults;
            string Signature(int seed)
            {
                var d = new BreakdownDirector(s, seed);
                var sb = new System.Text.StringBuilder();
                foreach ((float time, BreakdownType type) in Run(d, 600f, Normal(), 0.1f, true)) { sb.Append($"{time:F1}{type};"); }
                sb.Append(d.PickSweetSpot()).Append(d.PickOutlet(new[] { 0, 1 }, 2));
                return sb.ToString();
            }
            Assert.AreEqual(Signature(7), Signature(7));
            var different = new HashSet<string>();
            for (int seed = 1; seed <= 8; seed++) { different.Add(Signature(seed)); }
            Assert.Greater(different.Count, 4, "different seeds play different shifts");
        }

        [Test]
        public void AResetStartsTheGracePeriodAndTheDiceOver()
        {
            var d = new BreakdownDirector(BreakdownSettings.Defaults, 9);
            List<(float time, BreakdownType type)> first = Run(d, 600f, Normal(), 0.1f, true);
            d.Reset();
            List<(float time, BreakdownType type)> second = Run(d, 600f, Normal(), 0.1f, true);
            CollectionAssert.AreEqual(first, second);
            Assert.AreEqual(second.Count, d.Started);
        }

        [Test]
        public void AnAverageTenMinuteShiftSeesAboutThreeToFiveBreakdowns()
        {
            // average play: the generator runs the whole shift, now and then it is low on fuel, hot or overloaded, and every breakdown is fixed within ~20 s
            const int runs = 300;
            int total = 0;
            var perType = new int[3];
            var rng = new System.Random(123);
            for (int seed = 1; seed <= runs; seed++)
            {
                var d = new BreakdownDirector(BreakdownSettings.Defaults, seed);
                byte active = 0;
                float fixAt = float.MaxValue;
                for (float t = 0f; t < 600f; t += 0.5f)
                {
                    if (t >= fixAt) { active = 0; fixAt = float.MaxValue; }
                    DirectorInputs i = Normal(active);
                    i.FuelFraction = (t % 150f) > 120f ? 0.2f : 0.6f;
                    i.Heat = (t % 200f) > 170f ? 80f : 30f;
                    i.LoadAmps = (t % 100f) > 80f ? 12f : 6f;
                    BreakdownType? started = d.Tick(0.5f, i);
                    if (started.HasValue)
                    {
                        total++;
                        perType[(int)started.Value]++;
                        active |= BreakdownTypes.Bit(started.Value);
                        fixAt = t + 20f + (float)rng.NextDouble() * 10f;
                    }
                }
            }
            float mean = total / (float)runs;
            Assert.GreaterOrEqual(mean, 3f, $"mean {mean:F2} per shift, sputter {perType[0]} belt {perType[1]} breaker {perType[2]}");
            Assert.LessOrEqual(mean, 5f, $"mean {mean:F2} per shift, sputter {perType[0]} belt {perType[1]} breaker {perType[2]}");
            Debug.Log($"[BreakdownTests] average play: {mean:F2} breakdowns per 10 minutes (sputter {perType[0] / (float)runs:F2}, belt {perType[1] / (float)runs:F2}, breaker {perType[2] / (float)runs:F2})");
        }

        [Test]
        public void ThePickedSweetSpotAndOutletAreInRange_AndPreferAPluggedOutlet()
        {
            var d = new BreakdownDirector(BreakdownSettings.Defaults, 5);
            for (int i = 0; i < 50; i++)
            {
                int spot = d.PickSweetSpot();
                Assert.That(spot, Is.InRange(0, 3));
                Assert.AreEqual(1, d.PickOutlet(new[] { 1 }, 2), "only outlet 1 has a cable");
                Assert.That(d.PickOutlet(new int[0], 2), Is.InRange(0, 1));
                Assert.That(d.PickOutlet(null, 2), Is.InRange(0, 1));
            }
        }
    }

    public class BreakdownModelTests
    {
        private static BreakdownModel Make() => new BreakdownModel(BreakdownSettings.Defaults);

        // ---------- sputter ----------

        [Test]
        public void ThreeHitsOnTheSweetSpotFixTheEngine()
        {
            BreakdownModel m = Make();
            Assert.IsTrue(m.Start(BreakdownType.Sputter, 2));
            Assert.AreEqual(2, m.SweetSpot);
            Assert.AreEqual(HitResult.Progress, m.Hit(2));
            Assert.AreEqual(HitResult.Progress, m.Hit(2));
            Assert.IsTrue(m.IsActive(BreakdownType.Sputter));
            Assert.AreEqual(2, m.HitsDone);
            Assert.AreEqual(HitResult.Fixed, m.Hit(2));
            Assert.IsFalse(m.IsActive(BreakdownType.Sputter));
            Assert.AreEqual(1, m.FixedCount);
        }

        [Test]
        public void AHitElsewhere_ClangsAndMakesNoProgress()
        {
            BreakdownModel m = Make();
            m.Start(BreakdownType.Sputter, 1);
            Assert.AreEqual(HitResult.Miss, m.Hit(-1), "nowhere near a sweet spot");
            Assert.AreEqual(HitResult.Miss, m.Hit(3), "another sweet spot");
            Assert.AreEqual(0, m.HitsDone);
            m.Hit(1);
            m.Hit(0);
            Assert.AreEqual(1, m.HitsDone, "a miss in between neither adds nor takes away");
        }

        [Test]
        public void HittingWithNothingBroken_JustClangs()
        {
            BreakdownModel m = Make();
            Assert.AreEqual(HitResult.NoBreakdown, m.Hit(0));
            Assert.AreEqual(0, m.FixedCount);
        }

        [Test]
        public void TheEngineCoughsOnceEvery2AndAHalfSecondsFor06Seconds()
        {
            BreakdownModel m = Make();
            m.Start(BreakdownType.Sputter, 0);
            var dips = new List<(float from, float to)>();
            float dipStart = -1f;
            for (float t = 0f; t < 10f; t += 0.05f)
            {
                m.Tick(0.05f);
                if (m.Dipping && dipStart < 0f) { dipStart = t; }
                if (!m.Dipping && dipStart >= 0f) { dips.Add((dipStart, t)); dipStart = -1f; }
            }
            Assert.That(dips.Count, Is.InRange(3, 4), "at 1.9 s, 4.4 s, 6.9 s and 9.4 s");
            Assert.AreEqual(1.9f, dips[0].from, 0.1f);
            Assert.AreEqual(0.6f, dips[0].to - dips[0].from, 0.1f);
            Assert.AreEqual(2.5f, dips[1].from - dips[0].from, 0.1f);
            m.Hit(0); m.Hit(0); m.Hit(0);
            Assert.IsFalse(m.Dipping, "fixed: no more coughing");
        }

        [Test]
        public void SputterPatternEdgeCases()
        {
            Assert.IsFalse(SputterPattern.Dipping(0f, 2.5f, 0.6f));
            Assert.IsFalse(SputterPattern.Dipping(1.89f, 2.5f, 0.6f));
            Assert.IsTrue(SputterPattern.Dipping(1.91f, 2.5f, 0.6f));
            Assert.IsTrue(SputterPattern.Dipping(2.49f, 2.5f, 0.6f));
            Assert.IsFalse(SputterPattern.Dipping(2.51f, 2.5f, 0.6f));
            Assert.IsFalse(SputterPattern.Dipping(5f, 0f, 0.6f), "no cycle, no dips");
        }

        [Test]
        public void SweetSpotRule_PicksTheNearestSpotWithinTheRadius()
        {
            var spots = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) };
            Assert.AreEqual(1, SweetSpotRule.Nearest(new Vector3(0.9f, 0.1f, 0f), spots, 0.25f));
            Assert.AreEqual(-1, SweetSpotRule.Nearest(new Vector3(0.5f, 0.5f, 0f), spots, 0.25f));
            Assert.AreEqual(0, SweetSpotRule.Nearest(new Vector3(0.25f, 0f, 0f), spots, 0.25f), "on the edge of the radius counts");
            Assert.AreEqual(0, SweetSpotRule.Nearest(new Vector3(0.4f, 0f, 0f), spots, 0.5f), "the nearer of two spots in reach");
            Assert.AreEqual(-1, SweetSpotRule.Nearest(Vector3.zero, new List<Vector3>(), 1f));
        }

        // ---------- belt ----------

        [Test]
        public void HoldingTheBeltForTwoSecondsFitsIt()
        {
            BreakdownModel m = Make();
            m.Start(BreakdownType.Belt);
            for (int i = 0; i < 19; i++) { Assert.IsFalse(m.FitBelt(0.1f)); }
            Assert.AreEqual(0.95f, m.BeltProgress, 0.02f);
            Assert.IsTrue(m.FitBelt(0.1f));
            Assert.IsFalse(m.IsActive(BreakdownType.Belt));
            Assert.AreEqual(0f, m.BeltProgress);
            Assert.AreEqual(1, m.FixedCount);
        }

        [Test]
        public void LettingGoStartsTheFittingOver()
        {
            BreakdownModel m = Make();
            m.Start(BreakdownType.Belt);
            m.FitBelt(1.5f);
            m.CancelFit();
            Assert.AreEqual(0f, m.BeltProgress);
            Assert.IsFalse(m.FitBelt(1.5f), "1.5 s in total is not enough");
            Assert.IsTrue(m.FitBelt(0.6f));
        }

        [Test]
        public void FittingABeltThatIsNotBroken_DoesNothing()
        {
            BreakdownModel m = Make();
            Assert.IsFalse(m.FitBelt(5f));
            Assert.AreEqual(0, m.FixedCount);
        }

        // ---------- breaker ----------

        [Test]
        public void ResettingTheLeverFor15SecondsFixesTheBreaker_WhenTheStreetIsNotOverloaded()
        {
            BreakdownModel m = Make();
            m.Start(BreakdownType.Breaker, 0, 1);
            Assert.AreEqual(1, m.TrippedOutlet);
            Assert.IsTrue(m.OutletTripped);
            Assert.AreEqual(ResetResult.Holding, m.ResetBreaker(1f, 8f, 10f));
            Assert.AreEqual(0.67f, m.ResetProgress, 0.02f);
            Assert.AreEqual(ResetResult.Fixed, m.ResetBreaker(0.6f, 8f, 10f));
            Assert.IsFalse(m.IsActive(BreakdownType.Breaker));
            Assert.IsFalse(m.OutletTripped);
            Assert.AreEqual(-1, m.TrippedOutlet);
        }

        [Test]
        public void ResettingWhileTheLoadIsStillAboveTheRatingFlipsItBackDownAfterASecond()
        {
            BreakdownModel m = Make();
            m.Start(BreakdownType.Breaker, 0, 0);
            Assert.AreEqual(ResetResult.WillRetrip, m.ResetBreaker(1.6f, 12f, 10f));
            Assert.IsTrue(m.LeverUp, "the lever is up for a moment");
            Assert.IsFalse(m.OutletTripped, "and the outlet has power");
            Assert.IsTrue(m.IsActive(BreakdownType.Breaker), "not fixed");
            Assert.AreEqual(ResetResult.Nothing, m.ResetBreaker(1f, 5f, 10f), "nothing to do while it is up");
            Assert.IsFalse(m.Tick(0.5f));
            Assert.IsTrue(m.LeverUp);
            Assert.IsTrue(m.Tick(0.6f), "after 1 s it trips again");
            Assert.IsFalse(m.LeverUp);
            Assert.IsTrue(m.OutletTripped);
            Assert.AreEqual(1, m.RetripCount);
            Assert.AreEqual(0, m.FixedCount);
            Assert.AreEqual(0f, m.ResetProgress, "starts over");
        }

        [Test]
        public void AfterARetripAResetThatFindsTheLoadLowEnoughWorks()
        {
            BreakdownModel m = Make();
            m.Start(BreakdownType.Breaker, 0, 0);
            m.ResetBreaker(2f, 11f, 10f);
            m.Tick(1.1f);
            Assert.AreEqual(ResetResult.Fixed, m.ResetBreaker(1.5f, 7f, 10f), "somebody unplugged a building meanwhile");
            Assert.AreEqual(1, m.FixedCount);
        }

        [Test]
        public void ALoadExactlyAtTheRatingIsFine()
        {
            BreakdownModel m = Make();
            m.Start(BreakdownType.Breaker, 0, 0);
            Assert.AreEqual(ResetResult.Fixed, m.ResetBreaker(2f, 10f, 10f));
        }

        [Test]
        public void LettingGoOfTheLeverStartsTheResetOver()
        {
            BreakdownModel m = Make();
            m.Start(BreakdownType.Breaker, 0, 0);
            m.ResetBreaker(1.2f, 5f, 10f);
            m.CancelReset();
            Assert.AreEqual(0f, m.ResetProgress);
            Assert.AreEqual(ResetResult.Holding, m.ResetBreaker(0.5f, 5f, 10f));
        }

        // ---------- the three together ----------

        [Test]
        public void TheBreakdownsAreIndependent_AndCannotStartTwice()
        {
            BreakdownModel m = Make();
            Assert.IsTrue(m.Start(BreakdownType.Sputter, 0));
            Assert.IsFalse(m.Start(BreakdownType.Sputter, 1), "already sputtering");
            Assert.AreEqual(0, m.SweetSpot, "the sweet spot did not move");
            Assert.IsTrue(m.Start(BreakdownType.Belt));
            Assert.AreEqual(2, m.ActiveCount);
            Assert.AreEqual((byte)3, m.ActiveMask);
            m.FitBelt(5f);
            Assert.AreEqual((byte)1, m.ActiveMask, "fixing the belt leaves the sputter alone");
        }

        [Test]
        public void TheStatisticsCountFixesAndAverageTheTimeTheyTook()
        {
            BreakdownModel m = Make();
            Assert.AreEqual(0f, m.AverageFixSeconds);
            m.Start(BreakdownType.Belt);
            m.Tick(10f);
            m.FitBelt(3f);
            m.Start(BreakdownType.Sputter, 0);
            m.Tick(20f);
            m.Hit(0); m.Hit(0); m.Hit(0);
            Assert.AreEqual(2, m.FixedCount);
            Assert.AreEqual(15f, m.AverageFixSeconds, 0.01f);
            m.ResetStats();
            Assert.AreEqual(0, m.FixedCount);
            Assert.AreEqual(0f, m.AverageFixSeconds);
        }

        [Test]
        public void ClearAllDropsEverythingWithoutCountingAFix()
        {
            BreakdownModel m = Make();
            m.Start(BreakdownType.Sputter, 2);
            m.Start(BreakdownType.Breaker, 0, 1);
            m.ClearAll();
            Assert.AreEqual(0, m.ActiveCount);
            Assert.AreEqual(0, m.FixedCount);
            Assert.AreEqual(-1, m.TrippedOutlet);
            Assert.IsFalse(m.Dipping);
            Assert.IsTrue(m.Start(BreakdownType.Sputter, 1), "a fresh start works");
        }

        // ---------- item respawn ----------

        [Test]
        public void ALostWrenchGoesBackToItsHookAfter30Seconds()
        {
            var rule = new ItemRespawnRule(30f, 20f, -2f);
            Vector3 hook = new Vector3(2f, 1f, 0f);
            Assert.IsFalse(rule.Tick(100f, false, hook + Vector3.right * 3f, hook), "just lying near the generator is not lost");
            Assert.IsFalse(rule.Tick(29f, false, hook + Vector3.right * 30f, hook), "far away, but not for long enough");
            Assert.IsTrue(rule.Tick(1.1f, false, hook + Vector3.right * 30f, hook), "30 s lost: back to the hook");
            Assert.IsFalse(rule.Tick(1f, false, hook, hook), "and the timer started over");
        }

        [Test]
        public void ADroppedThroughTheFloorWrenchIsLostToo_AndAHeldOneNever()
        {
            var rule = new ItemRespawnRule(30f, 20f, -2f);
            Vector3 hook = Vector3.zero;
            Assert.IsTrue(rule.Tick(31f, false, new Vector3(1f, -5f, 1f), hook));
            Assert.IsFalse(rule.Tick(100f, true, new Vector3(1f, -5f, 1f), hook), "somebody holds it");
            rule.Tick(20f, false, new Vector3(0f, -9f, 0f), hook);
            Assert.IsFalse(rule.Tick(5f, false, hook, hook), "found again: the timer resets");
            Assert.IsFalse(rule.Tick(20f, false, new Vector3(0f, -9f, 0f), hook), "20 s is not 30 s");
        }
    }

    public class BreakdownGeneratorEffectTests
    {
        private static GeneratorModel Running(float fuel = 80f)
        {
            var g = new GeneratorModel(GeneratorSettings.Defaults, fuel);
            Assert.AreEqual(PullResult.Started, g.Pull(0.0));
            return g;
        }

        [Test]
        public void ANormalGeneratorIsUnchanged_NoDipNoBrokenCooling()
        {
            GeneratorModel g = Running();
            Assert.IsTrue(g.OutputLive);
            g.Tick(1f, 6f);
            Assert.AreEqual(6f, g.LoadAmps, 1e-4f);
            Assert.AreEqual(0f, g.Heat, "cooling cannot go below zero");
        }

        [Test]
        public void DuringADipTheOutputIsDeadAndNothingIsDrawn_ThenItComesBack()
        {
            GeneratorModel g = Running();
            g.OutputDip = true;
            Assert.IsFalse(g.OutputLive);
            Assert.AreEqual(GeneratorState.Running, g.State, "still running");
            float fuelBefore = g.Fuel;
            g.Tick(0.5f, 8f);
            Assert.AreEqual(0f, g.LoadAmps, "nothing is delivered");
            Assert.Less(g.Fuel, fuelBefore, "it still burns fuel (idle)");
            g.OutputDip = false;
            g.Tick(0.1f, 8f);
            Assert.IsTrue(g.OutputLive);
            Assert.AreEqual(8f, g.LoadAmps, 1e-4f);
        }

        [Test]
        public void ADipGivesAnOverloadedGeneratorABreather()
        {
            GeneratorModel a = Running(), b = Running();
            b.OutputDip = true;
            for (int i = 0; i < 10; i++) { a.Tick(0.5f, 14f); b.Tick(0.5f, 14f); }
            Assert.Greater(a.Heat, 0f);
            Assert.AreEqual(0f, b.Heat, "no load, no heat");
        }

        [Test]
        public void ABrokenBeltStopsTheCoolingAndAddsHeat()
        {
            GeneratorModel normal = Running(), broken = Running();
            // warm both up the same way first
            for (int i = 0; i < 20; i++) { normal.Tick(0.5f, 13f); broken.Tick(0.5f, 13f); }
            Assert.AreEqual(normal.Heat, broken.Heat, 1e-3f);
            broken.CoolingBroken = true;
            broken.BrokenCoolingExtraHeat = 3f;
            float heat = normal.Heat;
            normal.Tick(2f, 6f);
            broken.Tick(2f, 6f);
            Assert.AreEqual(heat - 4f, normal.Heat, 1e-3f, "a healthy generator cools 2 per second below its rating");
            Assert.AreEqual(heat + 6f, broken.Heat, 1e-3f, "a broken belt: no cooling and 3 more per second");
        }

        [Test]
        public void ABrokenBeltOverheatsAnIdleGeneratorInAboutAHalfMinute_AndFixingStopsIt()
        {
            GeneratorModel g = Running(100f);
            g.CoolingBroken = true;
            g.BrokenCoolingExtraHeat = 3f;
            float t = 0f;
            for (; t < 60f && g.State == GeneratorState.Running; t += 0.1f) { g.Tick(0.1f, 0f); }
            Assert.AreEqual(GeneratorState.Overheated, g.State);
            Assert.That(t, Is.InRange(30f, 36f));

            GeneratorModel fixedEarly = Running(100f);
            fixedEarly.CoolingBroken = true;
            fixedEarly.BrokenCoolingExtraHeat = 3f;
            for (int i = 0; i < 100; i++) { fixedEarly.Tick(0.1f, 0f); }
            Assert.Greater(fixedEarly.Heat, 25f);
            fixedEarly.CoolingBroken = false;
            for (int i = 0; i < 100; i++) { fixedEarly.Tick(0.1f, 0f); }
            Assert.Less(fixedEarly.Heat, 25f, "the belt is on again: it cools down");
            Assert.AreEqual(GeneratorState.Running, fixedEarly.State);
        }

        [Test]
        public void ABrokenBeltDoesNothingToAStoppedGenerator()
        {
            var g = new GeneratorModel(GeneratorSettings.Defaults, 50f);
            g.CoolingBroken = true;
            g.BrokenCoolingExtraHeat = 3f;
            g.Tick(10f, 0f);
            Assert.AreEqual(0f, g.Heat);
        }

        // ---------- the grid ----------

        private static PowerGrid Street()
        {
            var g = new PowerGrid();
            g.RegisterSocket(0, 5f);
            g.RegisterSocket(1, 5f);
            g.RegisterSocket(2, 5f);
            g.RegisterSocket(3, 8f);
            return g;
        }

        [Test]
        public void ATrippedOutletsSocketIsUnfed_AndItsLoadIsGone()
        {
            PowerGrid g = Street();
            g.Connect(0, 3);
            g.Connect(1, 1);
            Assert.AreEqual(13f, g.Load, 1e-4f);
            g.SetTripped(0, true);
            Assert.IsTrue(g.IsTripped(0));
            Assert.AreEqual(0, g.TrippedOutlet);
            Assert.IsFalse(g.IsFed(3), "the big building drops");
            Assert.IsFalse(g.IsPowered(3, true));
            Assert.IsTrue(g.IsPowered(1, true), "the other outlet is fine");
            Assert.AreEqual(5f, g.Load, 1e-4f);
            Assert.AreEqual(13f, g.LoadIfNothingTripped, 1e-4f);
            Assert.AreEqual(8f, g.DrawOnOutlet(0), 1e-4f);
            Assert.AreEqual(2, g.FedCount, "the cables are still plugged in");
            Assert.IsTrue(g.IsConnected(3));
        }

        [Test]
        public void ResettingTheBreakerFeedsTheSocketAgain()
        {
            PowerGrid g = Street();
            g.Connect(1, 2);
            g.SetTripped(1, true);
            Assert.IsFalse(g.IsPowered(2, true));
            g.SetTripped(1, false);
            Assert.IsTrue(g.IsPowered(2, true));
            Assert.AreEqual(-1, g.TrippedOutlet);
            Assert.AreEqual(5f, g.Load, 1e-4f);
        }

        [Test]
        public void ASocketOfATrippedOutletCannotBeGivenToAnotherCable()
        {
            PowerGrid g = Street();
            g.Connect(0, 3);
            g.SetTripped(0, true);
            Assert.IsFalse(g.Connect(1, 3), "somebody plugged into that socket already");
        }

        [Test]
        public void AcableCanBePluggedIntoATrippedOutletAndStaysDark()
        {
            PowerGrid g = Street();
            g.SetTripped(1, true);
            Assert.IsTrue(g.Connect(1, 0));
            Assert.IsFalse(g.IsPowered(0, true));
            g.Disconnect(1);
            g.Connect(0, 0);
            Assert.IsTrue(g.IsPowered(0, true));
        }

        [Test]
        public void ResetTripsClearsThemAll_AndListsThePluggedOutlets()
        {
            PowerGrid g = Street();
            g.Connect(1, 2);
            g.Connect(0, 1);
            CollectionAssert.AreEqual(new[] { 0, 1 }, g.ConnectedOutlets());
            g.SetTripped(0, true);
            g.SetTripped(1, true);
            Assert.AreEqual(0, g.TrippedOutlet, "the lowest tripped outlet");
            g.ResetTrips();
            Assert.AreEqual(-1, g.TrippedOutlet);
            Assert.IsTrue(g.IsFed(1));
        }
    }
    public class BreakdownBotTests
    {
        private static readonly Vector3 Gen = Vector3.zero;

        [Test]
        public void AClearWayIsWalkedStraight()
        {
            Vector3 to = new Vector3(5f, 0f, 5f);
            Assert.AreEqual(to, BreakdownBot.NextWaypoint(new Vector3(3f, 0f, 8f), to, Gen, 1.35f, 2.2f));
        }

        [Test]
        public void ARouteThroughTheGeneratorGoesRoundIt()
        {
            Vector3 from = new Vector3(-1.5f, 0f, -1.6f), to = new Vector3(0.4f, 0f, 1.7f);   // from the wrench hook to the front of the generator
            Vector3 way = BreakdownBot.NextWaypoint(from, to, Gen, 1.35f, 2.2f);
            Assert.AreNotEqual(to, way);
            Assert.AreEqual(2.2f, Vector3.Distance(way, Gen), 1e-3f, "a point beside the generator");
            Assert.Greater(Vector3.Distance(way, new Vector3(-1.5f, 0f, -1.6f)), 0.5f);
            // the way from there is clear, or goes on round: it never gets worse
            Vector3 next = BreakdownBot.NextWaypoint(way, to, Gen, 1.35f, 2.2f);
            Assert.That(Vector3.Distance(next, to), Is.LessThanOrEqualTo(Vector3.Distance(way, to) + 0.01f));
        }

        [Test]
        public void TheDetourKeepsToTheSideTheBotIsOn()
        {
            Vector3 left = BreakdownBot.NextWaypoint(new Vector3(-1f, 0f, -2f), new Vector3(-1f, 0f, 2f), Gen, 1.35f, 2.2f);
            Vector3 right = BreakdownBot.NextWaypoint(new Vector3(1f, 0f, -2f), new Vector3(1f, 0f, 2f), Gen, 1.35f, 2.2f);
            Assert.Less(left.x, 0f);
            Assert.Greater(right.x, 0f);
        }

        [Test]
        public void StandingStillOrHeightDoesNotMatter()
        {
            Vector3 p = new Vector3(2f, 1.6f, 2f);
            Assert.AreEqual(p, BreakdownBot.NextWaypoint(p, p, Gen, 1.35f, 2.2f));
            Vector3 high = BreakdownBot.NextWaypoint(new Vector3(-3f, 5f, 0f), new Vector3(3f, 0f, 0f), Gen, 1.35f, 2.2f);
            Assert.AreEqual(0f, high.y, 1e-4f);
        }
    }
}
