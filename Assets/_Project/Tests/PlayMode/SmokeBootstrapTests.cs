using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerCutCrew.Tests.PlayMode
{
    public class SmokeBootstrapTests
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

        /// <summary>Leave an empty scene behind: the NGO test helpers cannot cope with the game's menu being the only loaded scene.</summary>
        [UnityTearDown]
        public IEnumerator LeaveAnEmptyScene()
        {
            Scene empty = SceneManager.CreateScene("EmptyAfterBootstrapTest");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty) { yield return SceneManager.UnloadSceneAsync(scene); }
            }
        }
    }
}
