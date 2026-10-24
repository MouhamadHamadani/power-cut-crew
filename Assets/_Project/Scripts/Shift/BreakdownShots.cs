using System.Collections;
using System.IO;
using PowerCutCrew.Breakdowns;
using PowerCutCrew.Buildings;
using PowerCutCrew.Generator;
using PowerCutCrew.Net;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace PowerCutCrew.Shift
{
    /// <summary>
    /// Development tool for the docs (-shot breakdowns -shotdir &lt;dir&gt;, on the host with graphics, with -autostart and a short -shiftseconds), each shot in English and Arabic:
    /// the lobby legend with the new icons, each breakdown active with the generator's marker, a hit on the sweet spot, the belt being fitted, the breaker being reset,
    /// the controls card and the summary with the new lines. The breakdowns are forced one after the other and finished through the host's dev hooks, so the shots do not
    /// depend on the dice or on what the bots do (the breakdown bot stays out of the way in this mode).
    /// </summary>
    public class BreakdownShots : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (LaunchArgs.Get("-shot") != "breakdowns") { return; }
            var go = new GameObject("BreakdownShots");
            DontDestroyOnLoad(go);
            go.AddComponent<BreakdownShots>();
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
            _cam.fieldOfView = 60f;
            _cam.enabled = false;
            MarkerOverlay.CameraOverride = _cam;   // the markers are projected through the photo camera, not through the bot's eyes
            // the generator's panel turns to face the main camera: while a shot is taken that is the photo camera
            foreach (Camera other in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { other.tag = "Untagged"; }
            _cam.tag = "MainCamera";

            float nextLog = 0f;
            while (ShiftNet.Instance == null || ShiftNet.Instance.State != ShiftState.Lobby || HelpHud.Instance == null || BreakdownNet.Instance == null)
            {
                if (Time.realtimeSinceStartup > nextLog) { nextLog = Time.realtimeSinceStartup + 5f; Debug.Log("[Shots] waiting for the lobby"); }
                yield return null;
            }
            yield return Both("lobby_legend");

            while (ShiftNet.Instance.State != ShiftState.Running) { yield return null; }
            BreakdownNet net = BreakdownNet.Instance;
            yield return new WaitForSeconds(3f);

            // the engine coughs: a front sweet spot, so the glow shows in the overall shot
            net.HostForce(BreakdownType.Sputter, 2);
            yield return new WaitForSeconds(1.6f);
            Overall();
            _cam.enabled = true;
            yield return Both("breakdown_sputter");
            Transform spot = net.SweetSpots[2];
            _cam.transform.position = spot.position + new Vector3(0.6f, 0.5f, 1.5f);
            _cam.transform.rotation = Quaternion.LookRotation(spot.position - _cam.transform.position, Vector3.up);
            _cam.fieldOfView = 50f;
            yield return Both("breakdown_hit", () => net.HostDevHit(true));
            _cam.enabled = false;
            net.HostDevFix(BreakdownType.Sputter);
            yield return new WaitForSeconds(1.2f);

            // the fan belt is gone
            net.HostForce(BreakdownType.Belt);
            yield return new WaitForSeconds(1.6f);
            Overall();
            _cam.enabled = true;
            yield return Both("breakdown_belt");
            net.HostDevProgress(BreakdownType.Belt, BreakdownTuning.Instance.BeltFitSeconds * 0.6f);
            Transform slot = net.BeltSlot;
            _cam.transform.position = slot.position + new Vector3(0.5f, 0.8f, 1.7f);
            _cam.transform.rotation = Quaternion.LookRotation(slot.position - _cam.transform.position, Vector3.up);
            _cam.fieldOfView = 45f;
            yield return Both("breakdown_belt_fit");
            _cam.enabled = false;
            net.HostDevFix(BreakdownType.Belt);
            yield return new WaitForSeconds(1.2f);

            // the breaker tripped
            net.HostForce(BreakdownType.Breaker, -1, 1);
            yield return new WaitForSeconds(1.2f);
            Overall();
            _cam.enabled = true;
            yield return Both("breakdown_breaker");
            net.HostDevProgress(BreakdownType.Breaker, BreakdownTuning.Instance.BreakerResetSeconds * 0.6f);
            Transform lever = net.Lever;
            _cam.transform.position = lever.position + new Vector3(0.4f, 0.9f, 1.5f);
            _cam.transform.rotation = Quaternion.LookRotation(lever.position + Vector3.up * 0.1f - _cam.transform.position, Vector3.up);
            _cam.fieldOfView = 45f;
            yield return Both("breakdown_breaker_reset");
            _cam.enabled = false;
            net.HostDevFix(BreakdownType.Breaker);

            // the controls card (the legend with the three new icons)
            HelpHud.Instance.SetCardOpen(true);
            yield return new WaitForSeconds(0.5f);
            yield return Both("controls_card");
            HelpHud.Instance.SetCardOpen(false);

            // the summary
            while (ShiftNet.Instance != null && ShiftNet.Instance.State != ShiftState.Summary) { yield return null; }
            yield return new WaitForSeconds(1.5f);
            Overall();
            _cam.enabled = true;
            yield return Both("shift_summary");
            Debug.Log("[Shots] breakdown screenshots done");
            Application.Quit();
        }

        /// <summary>The generator from the front and a little above, with its marker over it.</summary>
        private void Overall()
        {
            Transform g = GeneratorNet.Instance.transform;
            Vector3 eye = g.position + new Vector3(2.4f, 2.2f, 5.2f);
            _cam.transform.position = eye;
            _cam.transform.rotation = Quaternion.LookRotation(g.position + Vector3.up * 1.7f - eye, Vector3.up);
            _cam.fieldOfView = 62f;
        }

        private IEnumerator Both(string name, System.Action trigger = null)
        {
            foreach (string code in new[] { "en", "ar" })
            {
                LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(code);
                yield return new WaitForSeconds(0.6f);
                if (trigger != null) { trigger(); yield return new WaitForSeconds(0.12f); }
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"{name}_{code}.png"));
                yield return new WaitForSeconds(0.3f);
            }
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
        }
    }
}
