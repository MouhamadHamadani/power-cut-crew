using System.Collections.Generic;
using NUnit.Framework;
using PowerCutCrew.Buildings;

namespace PowerCutCrew.Tests.EditMode
{
    public class BuildingModelTests
    {
        private static BuildingSettings Fast()
        {
            BuildingSettings s = BuildingSettings.Defaults;
            s.InitialDelayMin = 1f; s.InitialDelayMax = 1f;
            s.RequestMin = 20f; s.RequestMax = 20f;
            s.QuietMin = 10f; s.QuietMax = 10f;
            s.SpikeChance = 0f;
            return s;
        }

        /// <summary>Tick for the given seconds in 0.1 s steps; returns every event that happened.</summary>
        private static BuildingEvent Run(BuildingModel m, float seconds, bool powered, bool allowStart = true)
        {
            BuildingEvent all = BuildingEvent.None;
            for (float t = 0f; t < seconds - 1e-4f; t += 0.1f) { all |= m.Tick(0.1f, powered, allowStart, out _); }
            return all;
        }

        [Test]
        public void ASeedGivesTheSameSchedule_ADifferentSeedAnotherOne()
        {
            BuildingSettings s = BuildingSettings.Defaults;
            var a = new BuildingModel(s, 5f, 42);
            var b = new BuildingModel(s, 5f, 42);
            var c = new BuildingModel(s, 5f, 43);
            a.Start(); b.Start(); c.Start();
            var ta = new List<float>(); var tb = new List<float>(); var tc = new List<float>();
            DemandWindow la = a.Window, lb = b.Window, lc = c.Window;
            for (int i = 0; i < 6000; i++)   // 10 minutes at 0.1 s
            {
                float t = i * 0.1f;
                a.Tick(0.1f, true, true, out _); b.Tick(0.1f, true, true, out _); c.Tick(0.1f, true, true, out _);
                if (a.Window != la) { ta.Add(t); la = a.Window; }
                if (b.Window != lb) { tb.Add(t); lb = b.Window; }
                if (c.Window != lc) { tc.Add(t); lc = c.Window; }
            }
            Assert.IsNotEmpty(ta);
            CollectionAssert.AreEqual(ta, tb, "same seed, same window changes");
            CollectionAssert.AreNotEqual(ta, tc, "another seed, another schedule");
        }

        [Test]
        public void WindowsStayInsideTheConfiguredRanges()
        {
            BuildingSettings s = BuildingSettings.Defaults;
            var m = new BuildingModel(s, 5f, 7);
            m.Start();
            float windowStart = 0f;
            DemandWindow last = m.Window;
            for (int i = 0; i < 20000; i++)
            {
                float t = i * 0.1f;
                m.Tick(0.1f, true, true, out _);
                if (m.Window != last)
                {
                    float length = t - windowStart;
                    if (last == DemandWindow.Requesting) { Assert.That(length, Is.InRange(s.RequestMin - 0.3f, s.RequestMax + 0.3f)); }
                    else if (windowStart > 0f) { Assert.That(length, Is.InRange(s.QuietMin - 0.3f, s.QuietMax + 0.3f)); }
                    windowStart = t;
                    last = m.Window;
                }
            }
        }

        [Test]
        public void ItDrawsNothingWhileQuietEvenWhenPlugged_AndItsSubscriptionWhileRequesting()
        {
            var m = new BuildingModel(Fast(), 10f, 1);
            Assert.AreEqual(0f, m.Amps);
            m.Start();
            Assert.AreEqual(DemandWindow.Quiet, m.Window);
            Assert.AreEqual(0f, m.Amps, "quiet: nothing, even if plugged in");
            Run(m, 1.5f, true);
            Assert.AreEqual(DemandWindow.Requesting, m.Window);
            Assert.AreEqual(10f, m.Amps, 1e-4f);
        }

        [Test]
        public void HappinessRisesTwoPerSecondWhenPowered_FallsOnePerSecondWhenNot_AndHoldsWhileQuiet()
        {
            var m = new BuildingModel(Fast(), 5f, 1);
            m.Start();
            Run(m, 1.2f, true);                       // now requesting
            float h0 = m.Happiness;
            Run(m, 5f, true);
            Assert.AreEqual(h0 + 10f, m.Happiness, 0.3f, "+2 per second powered");
            float h1 = m.Happiness;
            Run(m, 5f, false);
            Assert.AreEqual(h1 - 5f, m.Happiness, 0.5f, "-1 per second unpowered");
            Run(m, 15f, false);                       // the request window ends (20 s) and the quiet window begins
            Assert.AreEqual(DemandWindow.Quiet, m.Window);
            float held = m.Happiness;
            Run(m, 3f, false);   // still inside the 10 s quiet window
            Assert.AreEqual(DemandWindow.Quiet, m.Window);
            Assert.AreEqual(held, m.Happiness, 1e-4f, "quiet: happiness holds");
        }

        [Test]
        public void HappinessStartsAtSeventyAndIsClampedAtHundred()
        {
            var m = new BuildingModel(Fast(), 5f, 1);
            m.Start();
            Assert.AreEqual(70f, m.Happiness);
            Run(m, 1.1f, true);
            Run(m, 19f, true);
            Assert.That(m.Happiness, Is.LessThanOrEqualTo(100f));
            Assert.AreEqual(100f, m.Happiness, 1e-4f, "70 + 2 x 19 s would be 108");
        }

        [Test]
        public void ItTurnsAngryAtZero_AndStopsPayingForTheRestOfTheShift()
        {
            BuildingSettings s = Fast();
            s.RequestMin = 120f; s.RequestMax = 120f;
            var m = new BuildingModel(s, 5f, 1);
            m.Start();
            Run(m, 1.2f, true);
            BuildingEvent events = Run(m, 75f, false);   // 70 / 1 = 70 s to zero
            Assert.IsTrue(m.Angry);
            Assert.AreEqual(0f, m.Happiness, 1e-4f);
            Assert.IsTrue((events & BuildingEvent.BecameAngry) != 0);
            float earned = m.Earned;
            Run(m, 20f, true);                            // powered again, but angry: no payment, no recovery
            Assert.AreEqual(earned, m.Earned, 1e-4f);
            Assert.AreEqual(0f, m.Happiness, 1e-4f);
            Assert.IsTrue(m.Angry);
        }

        [Test]
        public void PaymentIsTheRatePerAmpPerMinute_OnlyWhileRequestingAndPowered()
        {
            BuildingSettings s = Fast();
            s.RatePerAmpPerMinute = 6f;
            var m = new BuildingModel(s, 5f, 1);
            m.Start();
            Run(m, 0.95f, true);                          // still quiet: nothing
            Assert.AreEqual(0f, m.Earned);
            Run(m, 0.3f, true);                           // requesting now
            float before = m.Earned;
            Run(m, 10f, true);
            Assert.AreEqual(before + 6f * 5f * (10f / 60f), m.Earned, 0.05f, "6 per amp per minute x 5 A x 10 s");
            float poweredEarned = m.Earned;
            Run(m, 3f, false);
            Assert.AreEqual(poweredEarned, m.Earned, 1e-4f, "unpowered: nothing");
        }

        [Test]
        public void ASpikeStartsOnceInsideTheWindow_DrawsOneAndAHalfTimes_AndLastsTwentySeconds()
        {
            BuildingSettings s = Fast();
            s.SpikeChance = 1f;
            s.RequestMin = 60f; s.RequestMax = 60f;
            var m = new BuildingModel(s, 10f, 5);
            m.Start();
            float spikeStart = -1f, spikeEnd = -1f;
            int started = 0;
            for (int i = 0; i < 700; i++)
            {
                float t = i * 0.1f;
                BuildingEvent e = m.Tick(0.1f, true, true, out _);
                if ((e & BuildingEvent.SpikeStarted) != 0) { started++; spikeStart = t; }
                if (m.Spiking) { Assert.AreEqual(15f, m.Amps, 1e-3f); }
                if (spikeStart >= 0f && spikeEnd < 0f && !m.Spiking) { spikeEnd = t; }
            }
            Assert.AreEqual(1, started, "one spike per window at most");
            Assert.AreEqual(1, m.Spikes);
            Assert.AreEqual(20f, spikeEnd - spikeStart, 0.3f);
        }

        [Test]
        public void WithoutAChanceThereIsNoSpike()
        {
            var m = new BuildingModel(Fast(), 10f, 5);
            m.Start();
            Run(m, 60f, true);
            Assert.AreEqual(0, m.Spikes);
        }

        [Test]
        public void SpikesHappenInRoughlyTwentyPercentOfTheWindows()
        {
            BuildingSettings s = BuildingSettings.Defaults;
            int windows = 0, spikes = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                var m = new BuildingModel(s, 5f, seed);
                m.Start();
                for (int i = 0; i < 18000; i++)   // 30 minutes
                {
                    BuildingEvent e = m.Tick(0.1f, true, true, out _);
                    if ((e & BuildingEvent.RequestStarted) != 0) { windows++; }
                    if ((e & BuildingEvent.SpikeStarted) != 0) { spikes++; }
                }
            }
            Assert.Greater(windows, 400);
            Assert.That(spikes / (float)windows, Is.InRange(0.14f, 0.26f));
        }

        [Test]
        public void AnUnpoweredRequestingBuildingShoutsOnceAfterTenSeconds_AndAgainAfterBeingPoweredAndLosingPowerAgain()
        {
            BuildingSettings s = Fast();
            s.RequestMin = 100f; s.RequestMax = 100f;
            var m = new BuildingModel(s, 5f, 1);
            m.Start();
            Run(m, 1.2f, true);
            Assert.AreEqual(BuildingEvent.None, Run(m, 9f, false) & BuildingEvent.UnpoweredLong, "not yet 10 s");
            Assert.IsTrue((Run(m, 2f, false) & BuildingEvent.UnpoweredLong) != 0);
            Assert.IsTrue((Run(m, 5f, false) & BuildingEvent.UnpoweredLong) == 0, "only once per outage");
            Run(m, 1f, true);
            Run(m, 11f, false);
            // (a new outage reports again)
            var m2 = new BuildingModel(s, 5f, 1);
            m2.Start();
            Run(m2, 1.2f, true);
            Run(m2, 11f, false);
            Run(m2, 1f, true);
            Assert.IsTrue((Run(m2, 11f, false) & BuildingEvent.UnpoweredLong) != 0);
        }

        [Test]
        public void ARequestWaitsForAFreeSlot_AndStartsTheMomentThereIsOne()
        {
            var m = new BuildingModel(Fast(), 5f, 1);
            m.Start();
            Run(m, 5f, true, allowStart: false);
            Assert.AreEqual(DemandWindow.Quiet, m.Window, "no slot: it keeps waiting");
            BuildingEvent e = Run(m, 0.2f, true, allowStart: true);
            Assert.AreEqual(DemandWindow.Requesting, m.Window);
            Assert.IsTrue((e & BuildingEvent.RequestStarted) != 0);
        }

        [Test]
        public void StopResetsEverything()
        {
            var m = new BuildingModel(Fast(), 5f, 1);
            m.Start();
            Run(m, 30f, false);
            m.Stop();
            Assert.AreEqual(DemandWindow.Quiet, m.Window);
            Assert.AreEqual(70f, m.Happiness);
            Assert.IsFalse(m.Angry);
            Assert.AreEqual(0f, m.Earned);
            Assert.AreEqual(0f, m.RequestingSeconds);
            Assert.AreEqual(0f, m.Amps);
            Assert.IsFalse(m.Running);
        }
    }

    public class BuildingGroupTests
    {
        private static BuildingGroup Street(int count, int max, BuildingSettings s)
        {
            var g = new BuildingGroup(max);
            for (int i = 0; i < count; i++) { g.Add(new BuildingModel(s, 5f, 100 + i * 17)); }
            return g;
        }

        [Test]
        public void AtMostThreeBuildingsRequestAtTheSameTime_EvenWithAnAggressiveSchedule()
        {
            BuildingSettings s = BuildingSettings.Defaults;
            s.InitialDelayMin = 0.1f; s.InitialDelayMax = 0.1f;   // all four want to start at once
            s.QuietMin = 1f; s.QuietMax = 2f;
            s.RequestMin = 30f; s.RequestMax = 60f;
            BuildingGroup g = Street(4, 3, s);
            g.StartAll();
            var powered = new[] { true, true, true, true };
            var events = new BuildingEvent[4];
            int worst = 0;
            int fourthStarted = 0;
            for (int i = 0; i < 18000; i++)
            {
                g.Tick(0.1f, powered, events);
                worst = System.Math.Max(worst, g.RequestingCount);
                if (g.RequestingCount == 3) { fourthStarted++; }
            }
            Assert.AreEqual(3, worst, "never more than 3, and 3 do happen");
            Assert.Greater(fourthStarted, 100);
        }

        [Test]
        public void TheGroupSumsUptimeMoneyAngryAndSpikes()
        {
            BuildingSettings s = BuildingSettings.Defaults;
            s.InitialDelayMin = 0.1f; s.InitialDelayMax = 0.1f;
            s.RequestMin = 100f; s.RequestMax = 100f;
            BuildingGroup g = Street(2, 3, s);
            g.StartAll();
            var events = new BuildingEvent[2];
            float money = 0f;
            for (int i = 0; i < 150; i++) { money += g.Tick(0.1f, new[] { true, false }, events); }   // building 0 powered, building 1 not, for 15 s
            Assert.AreEqual(50f, g.UptimePercent, 3f);
            Assert.Greater(money, 0f);
            Assert.AreEqual(money, g.Models[0].Earned + g.Models[1].Earned, 1e-3f);
            Assert.AreEqual(0f, g.Models[1].Earned);
            Assert.AreEqual(0, g.AngryCount, "70 / 1 = 70 s to anger: not yet");
            for (int i = 0; i < 650; i++) { g.Tick(0.1f, new[] { true, false }, events); }   // 80 s in total
            Assert.AreEqual(1, g.AngryCount);
            Assert.AreEqual(0, g.Models[0].Angry ? 1 : 0, "the powered one is happy");
        }

        [Test]
        public void UptimeIs100WhenNothingHasRequestedYet_AndTotalWantedAmpsFollowsTheWindows()
        {
            BuildingGroup g = Street(4, 3, BuildingSettings.Defaults);
            Assert.AreEqual(100f, g.UptimePercent);
            Assert.AreEqual(0f, g.TotalWantedAmps);
            g.StartAll();
            Assert.AreEqual(0f, g.TotalWantedAmps, "lobby-like start: all quiet");
        }
    }

    public class BuildingStringTests
    {
        private static UnityEngine.Localization.Tables.StringTable Table(string locale)
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets($"UI_{locale} t:StringTable");
            return UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Localization.Tables.StringTable>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static bool HasArabic(string s)
        {
            foreach (char c in s)
            {
                if (c >= 0x0600 && c <= 0x06FF) { return true; }
            }
            return false;
        }

        [Test]
        public void EveryShiftAndBuildingKeyExistsInBothLanguages_AndArabicIsArabic()
        {
            string[] keys =
            {
                "shift.start", "shift.hud.time", "shift.hud.money", "shift.hud.load", "shift.hud.lobby",
                "shift.sum.title", "shift.sum.uptime", "shift.sum.money", "shift.sum.angry", "shift.sum.overheats", "shift.sum.snaps", "shift.sum.pulls",
                "shift.sum.again", "shift.sum.menu", "shift.sum.waiting",
                "shift.verdict.loved", "shift.verdict.good", "shift.verdict.meh", "shift.verdict.bad",
                "bld.amps_format", "bld.name.0", "bld.name.1", "bld.name.2", "bld.name.3",
            };
            foreach (string locale in new[] { "en", "ar" })
            {
                var table = Table(locale);
                foreach (string key in keys)
                {
                    Assert.IsNotNull(table.GetEntry(key), $"{key} missing in {locale}");
                    Assert.IsNotEmpty(table.GetEntry(key).Value, $"{key} empty in {locale}");
                }
            }
            var ar = Table("ar");
            foreach (string key in new[] { "shift.start", "shift.sum.title", "shift.sum.again", "shift.verdict.bad", "bld.name.0", "bld.name.3" })
            {
                Assert.IsTrue(HasArabic(ar.GetEntry(key).Value), $"{key} should be Arabic");
            }
        }

        [Test]
        public void EveryReadabilityKeyExistsInBothLanguages_AndTheWordsAreArabicInArabic()
        {
            string[] formats = { "bld.spike_format", "help.binding" };
            string[] words =
            {
                "help.legend.quiet", "help.legend.powered", "help.legend.unpowered", "help.legend.angry",
                "help.act.move", "help.act.look", "help.act.jump", "help.act.sprint", "help.act.crouch",
                "help.act.interact", "help.act.grab", "help.act.usehit", "help.act.drop", "help.act.help",
                "help.mouse", "help.hint", "help.hint_hide", "help.hint_show", "help.card.title", "help.card.close",
                "tip.plug", "tip.unpowered", "tip.fuel", "tip.overheat",
            };
            foreach (string locale in new[] { "en", "ar" })
            {
                var table = Table(locale);
                foreach (string key in formats) { Assert.IsTrue(table.GetEntry(key).Value.Contains("{0}"), $"{key} needs {{0}} in {locale}"); }
                foreach (string key in words)
                {
                    Assert.IsNotNull(table.GetEntry(key), $"{key} missing in {locale}");
                    Assert.IsNotEmpty(table.GetEntry(key).Value, $"{key} empty in {locale}");
                }
            }
            var ar = Table("ar");
            foreach (string key in words) { Assert.IsTrue(HasArabic(ar.GetEntry(key).Value), $"{key} should be Arabic"); }
            Assert.IsTrue(Table("en").GetEntry("tip.plug").Value.Contains("{0}"), "the plug tip names the Grab key");
            Assert.IsTrue(ar.GetEntry("tip.plug").Value.Contains("{0}"));
        }

        [Test]
        public void FourNeighborLinesPerTrigger_InBothLanguages_AllDifferent()
        {
            foreach (string locale in new[] { "en", "ar" })
            {
                var table = Table(locale);
                foreach (string trigger in new[] { "request", "unpowered", "spike", "angry" })
                {
                    var seen = new HashSet<string>();
                    for (int i = 0; i < BuildingNet.LinesPerTrigger; i++)
                    {
                        string key = $"bld.line.{trigger}.{i}";
                        Assert.IsNotNull(table.GetEntry(key), $"{key} missing in {locale}");
                        string value = table.GetEntry(key).Value;
                        Assert.IsNotEmpty(value, $"{key} empty in {locale}");
                        Assert.IsTrue(seen.Add(value), $"{key} repeats another line in {locale}");
                        if (locale == "ar") { Assert.IsTrue(HasArabic(value), $"{key} should be Arabic"); }
                    }
                    Assert.AreEqual(4, seen.Count);
                }
            }
        }

        [Test]
        public void FormatStringsKeepTheirPlaceholders()
        {
            foreach (string locale in new[] { "en", "ar" })
            {
                var table = Table(locale);
                Assert.IsTrue(table.GetEntry("bld.amps_format").Value.Contains("{0}"), locale);
                Assert.IsTrue(table.GetEntry("shift.hud.load").Value.Contains("{0}") && table.GetEntry("shift.hud.load").Value.Contains("{1}"), locale);
                foreach (string key in new[] { "shift.hud.time", "shift.hud.money", "shift.sum.uptime", "shift.sum.money", "shift.sum.angry", "shift.sum.overheats", "shift.sum.snaps", "shift.sum.pulls" })
                {
                    Assert.IsTrue(table.GetEntry(key).Value.Contains("{0}"), $"{key} in {locale}");
                }
            }
        }

        [Test]
        public void DefaultTuning_HasTheBriefsNumbers()
        {
            BuildingTuning t = UnityEngine.Resources.Load<BuildingTuning>("BuildingTuning");
            Assert.IsNotNull(t);
            Assert.AreEqual(4, t.Buildings.Length);
            Assert.AreEqual(new[] { 5f, 5f, 5f, 10f }, new[] { t.Buildings[0].SubscriptionAmps, t.Buildings[1].SubscriptionAmps, t.Buildings[2].SubscriptionAmps, t.Buildings[3].SubscriptionAmps });
            Assert.AreEqual(60f, t.RequestMin); Assert.AreEqual(120f, t.RequestMax);
            Assert.AreEqual(30f, t.QuietMin); Assert.AreEqual(60f, t.QuietMax);
            Assert.AreEqual(3, t.MaxRequesting);
            Assert.AreEqual(0.2f, t.SpikeChance, 1e-6f); Assert.AreEqual(1.5f, t.SpikeMultiplier); Assert.AreEqual(20f, t.SpikeSeconds);
            Assert.AreEqual(70f, t.StartHappiness); Assert.AreEqual(2f, t.RisePerSecond); Assert.AreEqual(1f, t.FallPerSecond);
            Assert.AreEqual(10f, t.RatePerAmpPerMinute);
            Assert.AreEqual(10f, t.UnpoweredLineSeconds); Assert.AreEqual(3f, t.BubbleSeconds);
            var shift = UnityEngine.Resources.Load<PowerCutCrew.Shift.ShiftTuning>("ShiftTuning");
            Assert.AreEqual(600f, shift.DurationSeconds); Assert.AreEqual(3f, shift.CountdownSeconds);
        }
    }
}
