using System.Collections;
using System.IO;
using PowerCutCrew.Core;
using PowerCutCrew.Net;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace PowerCutCrew.UI
{
    /// <summary>
    /// Development tool for the docs: -shotdir &lt;dir&gt; -shot playpanel|prompt takes English and Arabic screenshots.
    /// playpanel: opens the Play panel on the main menu. prompt: waits for the interaction prompt (use with -host -bot -nograb).
    /// </summary>
    public class DevScreenshots : MonoBehaviour
    {
        private IEnumerator Start()
        {
            string dir = LaunchArgs.Get("-shotdir");
            string kind = LaunchArgs.Get("-shot", "playpanel");
            if (kind == "coreloop" || kind == "shift" || kind == "breakdowns") { yield break; }   // CoreLoopShots (Cables), ShiftShots and BreakdownShots (Shift) do those
            Directory.CreateDirectory(dir);
            yield return LocalizationSettings.InitializationOperation;
            if (kind == "playpanel")
            {
                NetPlayPanel panel = null;
                while (panel == null) { panel = FindFirstObjectByType<NetPlayPanel>(FindObjectsInactive.Include); yield return null; }
                yield return new WaitForSeconds(0.5f);
                panel.Open();
            }
            else
            {
                while (InteractionFocus.Current == null) { yield return null; }
            }
            yield return Shoot(dir, kind, "en");
            SetLocale("ar");
            if (kind == "prompt")
            {
                // wait until the prompt has been on screen continuously for a moment in Arabic, then shoot at once
                float visible = 0f;
                while (visible < 0.3f)
                {
                    visible = InteractionFocus.Current != null ? visible + Time.unscaledDeltaTime : 0f;
                    yield return null;
                }
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{kind}_ar.png"));
                yield return new WaitForSeconds(0.5f);
            }
            else
            {
                yield return Shoot(dir, kind, "ar");
            }
            Application.Quit();
        }

        private IEnumerator Shoot(string dir, string kind, string locale)
        {
            yield return new WaitForSeconds(0.8f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{kind}_{locale}.png"));
            yield return new WaitForSeconds(0.5f);
        }

        private static void SetLocale(string code)
        {
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(code);
        }
    }
}
