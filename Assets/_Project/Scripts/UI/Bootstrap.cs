using System.Collections;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;

namespace PowerCutCrew.UI
{
    /// <summary>Entry point: initializes Localization, then loads the first real scene.</summary>
    public class Bootstrap : MonoBehaviour
    {
        [SerializeField] private string _nextScene = "MainMenu";

        private IEnumerator Start()
        {
            yield return LocalizationSettings.InitializationOperation;
            SceneManager.LoadScene(_nextScene);
        }
    }
}
