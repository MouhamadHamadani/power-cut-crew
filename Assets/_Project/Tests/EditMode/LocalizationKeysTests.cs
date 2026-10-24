using System.Collections.Generic;
using NUnit.Framework;
using PowerCutCrew.Editor;
using UnityEditor;
using UnityEngine.Localization.Tables;

namespace PowerCutCrew.Tests.EditMode
{
    /// <summary>Every string key the setup scripts make exists in both languages with text in it (the neighbours' and the breakdowns' lines included).</summary>
    public class LocalizationKeysTests
    {
        private static StringTable Table(string locale)
        {
            string[] guids = AssetDatabase.FindAssets($"UI_{locale} t:StringTable");
            Assert.IsNotEmpty(guids, $"No UI string table for {locale}");
            return AssetDatabase.LoadAssetAtPath<StringTable>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static IEnumerable<string> AllKeys()
        {
            foreach (string k in BuildingsSetup.AllKeys) { yield return k; }
            foreach (string k in BuildingsSetup.ReadabilityKeys) { yield return k; }
            foreach (string k in BuildingsSetup.BreakdownKeys) { yield return k; }
            foreach (string trigger in BuildingsSetup.Triggers)
            {
                for (int i = 0; i < 4; i++) { yield return $"bld.line.{trigger}.{i}"; }
            }
        }

        [TestCase("en")]
        [TestCase("ar")]
        public void EveryKeyHasText(string locale)
        {
            StringTable table = Table(locale);
            var missing = new List<string>();
            foreach (string key in AllKeys())
            {
                StringTableEntry entry = table.GetEntry(key);
                if (entry == null || string.IsNullOrWhiteSpace(entry.Value)) { missing.Add(key); }
            }
            Assert.IsEmpty(missing, $"{locale}: missing or empty: {string.Join(", ", missing)}");
        }

        [Test]
        public void TheBreakdownLinesAndMarkersHaveAnArabicTextThatIsNotTheEnglishOne()
        {
            StringTable en = Table("en"), ar = Table("ar");
            foreach (string key in BuildingsSetup.BreakdownKeys)
            {
                Assert.AreNotEqual(en.GetEntry(key).Value, ar.GetEntry(key).Value, key);
                Assert.IsTrue(HasArabicLetter(ar.GetEntry(key).Value), $"{key} has no Arabic letter in the Arabic table");
            }
        }

        private static bool HasArabicLetter(string s)
        {
            foreach (char c in s) { if (c >= 'ء' && c <= 'ي') { return true; } }
            return false;
        }
    }
}
