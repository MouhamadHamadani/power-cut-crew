using UnityEngine;
using UnityEngine.Localization.Settings;

namespace PowerCutCrew.UI
{
    /// <summary>Temporary main menu hooks: EN/AR toggle and quit.</summary>
    public class MainMenuController : MonoBehaviour
    {
        public void ToggleLanguage()
        {
            string next = LocalizationSettings.SelectedLocale.Identifier.Code == "ar" ? "en" : "ar";
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(next);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
