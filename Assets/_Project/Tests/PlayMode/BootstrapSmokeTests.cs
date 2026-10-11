using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerCutCrew.Tests.PlayMode
{
    public class BootstrapSmokeTests
    {
        [UnityTest]
        public IEnumerator Bootstrap_LoadsMainMenuWithin5Seconds()
        {
            SceneManager.LoadScene("Bootstrap");
            float end = Time.realtimeSinceStartup + 5f;
            while (SceneManager.GetActiveScene().name != "MainMenu" && Time.realtimeSinceStartup < end)
            {
                yield return null;
            }
            Assert.AreEqual("MainMenu", SceneManager.GetActiveScene().name);
        }
    }
}
