using System.Collections;
using System.IO;
using PowerCutCrew.Buildings;
using PowerCutCrew.Net;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace PowerCutCrew.Shift
{
    /// <summary>
    /// Development tool for the docs (-shot shift -shotdir &lt;dir&gt;, on a bot client with graphics): the HUD while the shift runs, a building
    /// with a speech bubble (a temporary photo camera in front of it) and the summary screen, each in English and Arabic.
    /// </summary>
    public class ShiftShots : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (LaunchArgs.Get("-shot") != "shift") { return; }
            var go = new GameObject("ShiftShots");
            DontDestroyOnLoad(go);
            go.AddComponent<ShiftShots>();
        }

        private Camera _cam;
        private string _dir;

        private IEnumerator Start()
        {
            _dir = LaunchArgs.Get("-shotdir");
            Directory.CreateDirectory(_dir);
            yield return LocalizationSettings.InitializationOperation;
            _cam = new GameObject("PhotoCam").AddComponent<Camera>();
            _cam.transform.SetParent(transform);
            _cam.depth = 50;
            _cam.fieldOfView = 50f;
            _cam.enabled = false;

            bool hud = false, bubble = false, summary = false;
            float runningSince = -1f;
            while (!summary)
            {
                ShiftNet shift = ShiftNet.Instance;
                if (shift != null && shift.State == ShiftState.Running)
                {
                    runningSince = runningSince < 0f ? Time.time : runningSince;
                    if (!hud && Time.time - runningSince > 8f)
                    {
                        hud = true;
                        Aerial();
                        _cam.enabled = true;
                        yield return Both("shift_hud");
                        _cam.enabled = false;
                    }
                    if (!bubble)
                    {
                        foreach (BuildingNet b in BuildingNet.All)
                        {
                            if (b == null || !b.IsSpawned || !b.HasBubble || b.BubbleSecondsLeft < 2.4f) { continue; }
                            // the buildings stand on the far side of the sockets, which face the generator at the origin
                            Vector3 flat = new Vector3(b.transform.position.x, 0f, b.transform.position.z);
                            Vector3 toGen = flat.sqrMagnitude > 0.01f ? -flat.normalized : Vector3.back;
                            Vector3 at = flat + Vector3.up * 5.5f;   // the marker and the bubble float above the roof
                            Place(flat + toGen * 15f + Vector3.up * 5f, at);
                            _cam.enabled = true;
                            bubble = true;
                            yield return Both("building_bubble");
                            _cam.enabled = false;
                            break;
                        }
                    }
                }
                if (shift != null && shift.State == ShiftState.Summary) { summary = true; }
                yield return null;
            }
            yield return new WaitForSeconds(1.5f);
            Aerial();
            _cam.enabled = true;
            yield return Both("shift_summary");
            Debug.Log($"[Shots] shift screenshots done (hud {hud}, bubble {bubble})");
            Application.Quit();
        }

        private void Place(Vector3 position, Vector3 lookAt)
        {
            _cam.transform.position = position;
            _cam.transform.rotation = Quaternion.LookRotation(lookAt - position, Vector3.up);
        }

        /// <summary>The whole street from above and behind the generator.</summary>
        private void Aerial()
        {
            var center = Vector3.zero;
            int n = 0;
            foreach (BuildingNet b in BuildingNet.All) { if (b != null && b.IsSpawned) { center += b.transform.position; n++; } }
            if (n > 0) { center /= n; }
            center.y = 0f;
            Vector3 back = center.sqrMagnitude > 0.01f ? -center.normalized : Vector3.back;
            Place(back * 12f + Vector3.up * 14f, center + Vector3.up * 1f);
        }

        private IEnumerator Both(string name)
        {
            foreach (string code in new[] { "en", "ar" })
            {
                LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(code);
                yield return new WaitForSeconds(0.5f);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"{name}_{code}.png"));
                yield return new WaitForSeconds(0.3f);
            }
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
        }
    }
}
