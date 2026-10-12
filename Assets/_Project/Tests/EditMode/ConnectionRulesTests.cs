using NUnit.Framework;
using PowerCutCrew.Net;

namespace PowerCutCrew.Tests.EditMode
{
    public class ConnectionRulesTests
    {
        [Test]
        public void MatchingVersionWithRoom_IsApproved()
        {
            ConnectionDecision d = ConnectionRules.Evaluate(currentPlayers: 3, maxPlayers: 4, hostVersion: "0.1.0", clientVersion: "0.1.0");
            Assert.IsTrue(d.Approved);
            Assert.IsEmpty(d.ReasonKey);
        }

        [Test]
        public void FullLobby_IsRejectedWithFullReason()
        {
            ConnectionDecision d = ConnectionRules.Evaluate(4, 4, "0.1.0", "0.1.0");
            Assert.IsFalse(d.Approved);
            Assert.AreEqual(ConnectionRules.ReasonFull, d.ReasonKey);
        }

        [Test]
        public void DifferentVersion_IsRejectedWithVersionReason()
        {
            ConnectionDecision d = ConnectionRules.Evaluate(1, 4, "0.1.0", "0.0.9");
            Assert.IsFalse(d.Approved);
            Assert.AreEqual(ConnectionRules.ReasonVersion, d.ReasonKey);
        }

        [Test]
        public void FullAndWrongVersion_ReportsFullFirst()
        {
            ConnectionDecision d = ConnectionRules.Evaluate(4, 4, "0.1.0", "0.0.9");
            Assert.AreEqual(ConnectionRules.ReasonFull, d.ReasonKey);
        }

        [Test]
        public void ReasonKeys_ExistInTheUiTableForBothLanguages()
        {
            foreach (string key in new[] { ConnectionRules.ReasonFull, ConnectionRules.ReasonVersion, "net.error.host_left", "net.error.connect_failed", "net.error.steam_unavailable" })
            {
                foreach (string locale in new[] { "en", "ar" })
                {
                    string[] guids = UnityEditor.AssetDatabase.FindAssets($"UI_{locale} t:StringTable");
                    var table = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Localization.Tables.StringTable>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
                    Assert.IsNotNull(table.GetEntry(key), $"{key} missing in {locale}");
                    Assert.IsNotEmpty(table.GetEntry(key).Value, $"{key} empty in {locale}");
                }
            }
        }
    }
}
