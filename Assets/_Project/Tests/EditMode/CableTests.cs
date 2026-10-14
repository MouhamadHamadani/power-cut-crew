using NUnit.Framework;
using PowerCutCrew.Cables;
using PowerCutCrew.Generator;

namespace PowerCutCrew.Tests.EditMode
{
    public class CableModelTests
    {
        private static CableModel Make() => new CableModel(CableSettings.Defaults);

        private static CableEvent Run(CableModel c, float seconds, float stretch)
        {
            CableEvent last = CableEvent.None;
            for (float t = 0f; t < seconds; t += 0.02f)
            {
                CableEvent e = c.Tick(0.02f, stretch);
                if (e != CableEvent.None) { last = e; }
            }
            return last;
        }

        [Test]
        public void FreshCable_CanBeGrabbedReleasedAndGrabbedAgain()
        {
            CableModel c = Make();
            Assert.AreEqual(CableState.Fresh, c.State);
            Assert.IsTrue(c.Grab());
            Assert.AreEqual(CableState.Held, c.State);
            Assert.IsTrue(c.Release());
            Assert.AreEqual(CableState.Loose, c.State);
            Assert.IsTrue(c.Grab());
        }

        [Test]
        public void Held_StretchOverFifteenPercentForHalfASecond_Snaps()
        {
            CableModel c = Make();
            c.Grab();
            Assert.AreEqual(CableEvent.None, Run(c, 0.4f, 0.20f), "not long enough yet");
            Assert.AreEqual(CableEvent.Snapped, Run(c, 0.2f, 0.20f));
            Assert.AreEqual(CableState.Snapped, c.State);
        }

        [Test]
        public void Held_StretchBelowTheLimit_NeverSnaps()
        {
            CableModel c = Make();
            c.Grab();
            Assert.AreEqual(CableEvent.None, Run(c, 30f, 0.12f));
            Assert.AreEqual(CableState.Held, c.State);
        }

        [Test]
        public void Held_AShortSpike_ResetsTheTimer()
        {
            CableModel c = Make();
            c.Grab();
            Run(c, 0.4f, 0.3f);
            Run(c, 0.1f, 0.0f);   // relaxed
            Assert.AreEqual(CableEvent.None, Run(c, 0.4f, 0.3f), "the timer restarted");
            Assert.AreEqual(CableState.Held, c.State);
        }

        [Test]
        public void Plugged_StretchOverTenPercentForHalfASecond_PopsOut()
        {
            CableModel c = Make();
            c.Grab();
            Assert.IsTrue(c.PlugIn(2));
            Assert.AreEqual(2, c.SocketId);
            Assert.AreEqual(CableEvent.None, Run(c, 0.4f, 0.12f));
            Assert.AreEqual(CableEvent.PoppedOut, Run(c, 0.2f, 0.12f));
            Assert.AreEqual(CableState.Loose, c.State);
            Assert.AreEqual(-1, c.SocketId);
        }

        [Test]
        public void Plugged_BelowTheLimit_StaysPlugged_EvenAboveWhatWouldSnapAHeldCable()
        {
            CableModel c = Make();
            c.Grab();
            c.PlugIn(0);
            Assert.AreEqual(CableEvent.None, Run(c, 20f, 0.09f));
            Assert.AreEqual(CableState.Plugged, c.State);
        }

        [Test]
        public void PlugIn_NeedsAHeldCable_AndUnplugGivesItBackToTheHands()
        {
            CableModel c = Make();
            Assert.IsFalse(c.PlugIn(1), "a fresh cable nobody holds cannot be plugged in");
            c.Grab();
            Assert.IsTrue(c.PlugIn(1));
            Assert.IsFalse(c.Grab(), "a plugged cable cannot be grabbed, only unplugged");
            Assert.IsTrue(c.Unplug());
            Assert.AreEqual(CableState.Held, c.State);
            Assert.IsFalse(c.Unplug());
        }

        [Test]
        public void AfterASnap_ReelReadyAtThreeSeconds_AndPiecesDespawnAtEight_EachOnce()
        {
            CableModel c = Make();
            c.Grab();
            while (c.State != CableState.Snapped) { c.Tick(0.02f, 0.3f); }   // timers start at the snap

            int reel = 0, despawn = 0;
            float reelAt = -1f, despawnAt = -1f;
            float t = 0f;
            while (t < 12f)
            {
                CableEvent e = c.Tick(0.02f, 0.5f);
                t += 0.02f;
                if (e == CableEvent.ReelReady) { reel++; reelAt = t; }
                if (e == CableEvent.DespawnPieces) { despawn++; despawnAt = t; }
            }
            Assert.AreEqual(1, reel);
            Assert.AreEqual(1, despawn);
            Assert.AreEqual(3f, reelAt, 0.05f);
            Assert.AreEqual(8f, despawnAt, 0.05f);
        }

        [Test]
        public void ASnappedCable_CannotBeGrabbedOrPluggedIn()
        {
            CableModel c = Make();
            c.Grab();
            Run(c, 0.7f, 0.3f);
            Assert.IsTrue(c.IsBroken);
            Assert.IsFalse(c.Grab());
            Assert.IsFalse(c.PlugIn(0));
            Assert.IsFalse(c.Release());
        }
    }

    public class PlugRulesTests
    {
        [Test]
        public void ValidRequest_IsOk()
        {
            Assert.AreEqual(PlugInResult.Ok, PlugRules.Validate(true, true, 0.8f, 1.5f, true, true));
        }

        [Test]
        public void EachRuleRejects()
        {
            Assert.AreEqual(PlugInResult.NotHoldingPlug, PlugRules.Validate(false, true, 0.8f, 1.5f, true, true));
            Assert.AreEqual(PlugInResult.NoTrustedPose, PlugRules.Validate(true, false, 0.8f, 1.5f, true, true));
            Assert.AreEqual(PlugInResult.TooFar, PlugRules.Validate(true, true, 1.6f, 1.5f, true, true));
            Assert.AreEqual(PlugInResult.SocketOccupied, PlugRules.Validate(true, true, 0.8f, 1.5f, false, true));
            Assert.AreEqual(PlugInResult.CableBroken, PlugRules.Validate(true, true, 0.8f, 1.5f, true, false));
        }

        [Test]
        public void ReachIsInclusive()
        {
            Assert.AreEqual(PlugInResult.Ok, PlugRules.Validate(true, true, 1.5f, 1.5f, true, true));
        }
    }

    public class PowerGridTests
    {
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
        public void Load_IsTheSumOfTheFedSockets()
        {
            PowerGrid g = Street();
            Assert.AreEqual(0f, g.Load);
            Assert.IsTrue(g.Connect(0, 3));
            Assert.AreEqual(8f, g.Load, 1e-4f);
            Assert.IsTrue(g.Connect(1, 2));
            Assert.AreEqual(13f, g.Load, 1e-4f);
            Assert.AreEqual(2, g.FedCount);
        }

        [Test]
        public void ASocketCanOnlyBeFedOnce_AndUnknownSocketsAreRefused()
        {
            PowerGrid g = Street();
            Assert.IsTrue(g.Connect(0, 1));
            Assert.IsFalse(g.Connect(1, 1));
            Assert.IsFalse(g.Connect(1, 99));
        }

        [Test]
        public void Disconnect_RemovesTheLoad()
        {
            PowerGrid g = Street();
            g.Connect(0, 3);
            Assert.IsTrue(g.Disconnect(0));
            Assert.AreEqual(0f, g.Load);
            Assert.IsFalse(g.IsFed(3));
            Assert.IsFalse(g.Disconnect(0));
        }

        [Test]
        public void ASocketIsPoweredOnlyWhileTheGeneratorRuns()
        {
            PowerGrid g = Street();
            g.Connect(0, 2);
            Assert.IsTrue(g.IsPowered(2, true));
            Assert.IsFalse(g.IsPowered(2, false));
            Assert.IsFalse(g.IsPowered(1, true), "nothing plugged in");
        }

        [Test]
        public void TheStreetOverloadsTheRatedLoad_WithTwoCables()
        {
            PowerGrid g = Street();
            g.Connect(0, 3);
            g.Connect(1, 0);
            Assert.Greater(g.Load, GeneratorSettings.Defaults.RatedAmps);
        }
    }

    public class CoreLoopStringTests
    {
        [Test]
        public void NewKeys_ExistInBothLanguages()
        {
            string[] keys =
            {
                "gen.pull_cord", "gen.turn_off", "gen.too_hot", "gen.pour", "gen.state.off", "gen.state.running", "gen.state.out_of_fuel",
                "gen.state.overheated", "gen.fuel", "gen.heat", "gen.load_format", "fuel.fill", "cable.plug_in", "cable.unplug", "cable.take_plug",
            };
            foreach (string locale in new[] { "en", "ar" })
            {
                string[] guids = UnityEditor.AssetDatabase.FindAssets($"UI_{locale} t:StringTable");
                var table = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Localization.Tables.StringTable>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
                foreach (string key in keys)
                {
                    Assert.IsNotNull(table.GetEntry(key), $"{key} missing in {locale}");
                    Assert.IsNotEmpty(table.GetEntry(key).Value, $"{key} empty in {locale}");
                }
                Assert.IsTrue(table.GetEntry("gen.load_format").Value.Contains("{0}") && table.GetEntry("gen.load_format").Value.Contains("{1}"), $"load format placeholders in {locale}");
            }
        }
    }
}
