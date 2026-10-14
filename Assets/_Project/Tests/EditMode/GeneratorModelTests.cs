using NUnit.Framework;
using PowerCutCrew.Generator;

namespace PowerCutCrew.Tests.EditMode
{
    public class GeneratorModelTests
    {
        private static GeneratorModel Make(float fuel = 50f) => new GeneratorModel(GeneratorSettings.Defaults, fuel);

        [Test]
        public void StartOdds_RiseWithEachFailedPull_AndAreGuaranteedOnTheFourthPull()
        {
            GeneratorModel g = Make();
            Assert.AreEqual(0.35f, g.NextStartChance, 1e-4f);
            Assert.AreEqual(PullResult.Failed, g.Pull(0.99));
            Assert.AreEqual(0.60f, g.NextStartChance, 1e-4f);
            Assert.AreEqual(PullResult.Failed, g.Pull(0.70));
            Assert.AreEqual(0.85f, g.NextStartChance, 1e-4f);
            Assert.AreEqual(PullResult.Failed, g.Pull(0.90));
            Assert.AreEqual(1f, g.NextStartChance, 1e-4f);
            Assert.AreEqual(PullResult.Started, g.Pull(0.999999), "the 4th pull always starts it");
            Assert.AreEqual(GeneratorState.Running, g.State);
            Assert.AreEqual(0, g.FailedPulls);
        }

        [Test]
        public void ColdStart_SucceedsOnlyBelowTheChance()
        {
            Assert.AreEqual(PullResult.Started, Make().Pull(0.34));
            Assert.AreEqual(PullResult.Failed, Make().Pull(0.35));
        }

        [Test]
        public void PullingWithoutFuel_DoesNotStart_AndDoesNotCountAsAFailedPull()
        {
            GeneratorModel g = Make(0f);
            Assert.AreEqual(PullResult.NoFuel, g.Pull(0.0));
            Assert.AreEqual(0, g.FailedPulls);
            Assert.AreEqual(GeneratorState.Off, g.State);
        }

        [Test]
        public void Running_PullAgain_IsIgnored_AndStopWorks()
        {
            GeneratorModel g = Make();
            g.Pull(0.0);
            Assert.AreEqual(PullResult.AlreadyRunning, g.Pull(0.0));
            Assert.IsTrue(g.Stop());
            Assert.AreEqual(GeneratorState.Off, g.State);
            Assert.IsFalse(g.Stop());
        }

        [Test]
        public void FuelBurn_IsIdlePlusPerAmp()
        {
            GeneratorModel g = Make(50f);
            g.Pull(0.0);
            g.Tick(10f, 8f);   // (0.2 + 0.05 * 8) * 10 = 6
            Assert.AreEqual(44f, g.Fuel, 1e-3f);
            Assert.AreEqual(8f, g.LoadAmps, 1e-4f);
        }

        [Test]
        public void IdleBurn_WithNoLoad()
        {
            GeneratorModel g = Make(50f);
            g.Pull(0.0);
            g.Tick(10f, 0f);   // 0.2 * 10 = 2
            Assert.AreEqual(48f, g.Fuel, 1e-3f);
        }

        [Test]
        public void RunningOutOfFuel_KillsTheEngine_ThenFuelAndAPullRestartIt()
        {
            GeneratorModel g = Make(1f);
            g.Pull(0.0);
            g.Tick(10f, 0f);   // burns 2 > 1
            Assert.AreEqual(GeneratorState.OutOfFuel, g.State);
            Assert.AreEqual(0f, g.Fuel, 1e-4f);
            Assert.AreEqual(0f, g.LoadAmps, 1e-4f);
            Assert.AreEqual(PullResult.NoFuel, g.Pull(0.0));

            g.AddFuel(10f);
            Assert.AreEqual(GeneratorState.Off, g.State, "fuel alone does not restart it");
            Assert.AreEqual(PullResult.Started, g.Pull(0.0));
        }

        [Test]
        public void AddFuel_IsClampedToTheTankCapacity()
        {
            GeneratorModel g = Make(95f);
            Assert.AreEqual(5f, g.AddFuel(20f), 1e-4f);
            Assert.AreEqual(100f, g.Fuel, 1e-4f);
            Assert.AreEqual(0f, g.AddFuel(5f), 1e-4f);
        }

        [Test]
        public void Heat_RisesOnlyAboveRatedLoad_AndCoolsBelow()
        {
            GeneratorModel g = Make(100f);
            g.Pull(0.0);
            g.Tick(10f, 13f);   // 3 A over * 1.5 = 4.5 per second
            Assert.AreEqual(45f, g.Heat, 1e-2f);
            g.Tick(5f, 10f);    // at the rating: cools 2 per second
            Assert.AreEqual(35f, g.Heat, 1e-2f);
            g.Tick(5f, 5f);
            Assert.AreEqual(25f, g.Heat, 1e-2f);
        }

        [Test]
        public void Overload_StallsTheGenerator_AndLocksPullsUntilHeatBelow60()
        {
            GeneratorModel g = Make(100f);
            g.Pull(0.0);
            for (int i = 0; i < 400 && g.State == GeneratorState.Running; i++) { g.Tick(0.1f, 14f); }   // 4 A over = 6 per second
            Assert.AreEqual(GeneratorState.Overheated, g.State);
            Assert.GreaterOrEqual(g.Heat, 100f - 0.001f);
            Assert.AreEqual(0f, g.LoadAmps, 1e-4f);
            Assert.AreEqual(PullResult.Locked, g.Pull(0.0));

            // cools at 1 per second while stalled: still locked at 61, free below 60
            while (g.Heat > 61f) { g.Tick(0.5f, 0f); }
            Assert.AreEqual(GeneratorState.Overheated, g.State);
            Assert.AreEqual(PullResult.Locked, g.Pull(0.0));
            while (g.Heat >= 60f) { g.Tick(0.5f, 0f); }
            Assert.AreEqual(GeneratorState.Off, g.State);
            Assert.AreEqual(PullResult.Started, g.Pull(0.0));
        }

        [Test]
        public void Off_CoolsAtOnePerSecond()
        {
            GeneratorModel g = Make(100f);
            g.Pull(0.0);
            g.Tick(10f, 14f);                // heat 60
            g.Stop();
            g.Tick(10f, 0f);
            Assert.AreEqual(50f, g.Heat, 1e-2f);
        }
    }
}
