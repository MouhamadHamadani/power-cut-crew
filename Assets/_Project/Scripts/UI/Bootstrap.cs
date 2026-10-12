using System.Collections;
using PowerCutCrew.Net;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;

namespace PowerCutCrew.UI
{
    /// <summary>
    /// Entry point: initializes Localization, creates the persistent network session, then either starts from launch
    /// arguments (bot runs, Steam invites) or loads the main menu.
    /// </summary>
    public class Bootstrap : MonoBehaviour
    {
        [SerializeField] private string _nextScene = "MainMenu";
        [SerializeField] private NetSession _sessionPrefab;

        private IEnumerator Start()
        {
            yield return LocalizationSettings.InitializationOperation;
            if (LaunchArgs.Has("-shotdir")) { gameObject.AddComponent<DevScreenshots>(); DontDestroyOnLoad(gameObject); }
            if (NetSession.Instance == null && _sessionPrefab != null) { Instantiate(_sessionPrefab); }
            if (NetSession.Instance != null && NetSession.Instance.TryAutoStart()) { yield break; }
            SceneManager.LoadScene(_nextScene);
        }
    }
}
