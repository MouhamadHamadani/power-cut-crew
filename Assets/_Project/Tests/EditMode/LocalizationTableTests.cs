using NUnit.Framework;
using UnityEditor;
using UnityEngine.Localization.Tables;

namespace PowerCutCrew.Tests.EditMode
{
    public class LocalizationTableTests
    {
        [TestCase("en")]
        [TestCase("ar")]
        public void UiTable_HasGameTitle(string locale)
        {
            string[] guids = AssetDatabase.FindAssets($"UI_{locale} t:StringTable");
            Assert.IsNotEmpty(guids, $"No UI string table for {locale}");
            var table = AssetDatabase.LoadAssetAtPath<StringTable>(AssetDatabase.GUIDToAssetPath(guids[0]));
            Assert.IsNotNull(table.GetEntry("game.title"), $"game.title missing in {locale}");
            Assert.IsNotEmpty(table.GetEntry("game.title").Value);
        }
    }
}
