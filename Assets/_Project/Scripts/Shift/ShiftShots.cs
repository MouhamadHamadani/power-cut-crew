using System.Collections;
using System.IO;
using PowerCutCrew.Buildings;
using PowerCutCrew.Core;
using PowerCutCrew.Net;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace PowerCutCrew.Shift
{
    /// <summary>
    /// Development tool for the docs (-shot shift -shotdir &lt;dir&gt;, on a bot client with graphics), each shot in English and Arabic: the lobby legend, the HUD while the shift
    /// runs, the building markers from beside the tallest building, from mid-street and from the far end, a building out of view (edge arrow), a speech bubble, the controls
    /// card and the summary. A temporary photo camera is put where the shot needs it and the markers are projected through it.
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
            _cam.fieldOfView = 60f;
            _cam.enabled = false;
            MarkerOverlay.CameraOverride = _cam;   // the markers are projected through the photo camera, not through the bot's eyes

            // the lobby legend, while the bots are still walking to the clipboard
            float nextLog = 0f;
            while (ShiftNet.Instance == null || ShiftNet.Instance.State != ShiftState.Lobby || HelpHud.Instance == null)
            {
                if (Time.realtimeSinceStartup > nextLog)
                {
                    nextLog = Time.realtimeSinceStartup + 5f;
                    Debug.Log($"[Shots] waiting for the lobby: shift {(ShiftNet.Instance != null ? ShiftNet.Instance.State.ToString() : "none")}, help {(HelpHud.Instance != null ? "bound" : "none")}");
                }
                yield return null;
            }
            yield return Both("lobby_legend");

            bool done = false, bubble = false, hud = false;
            float runningSince = -1f;
            while (!done)
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
                            bubble = true;
                            Vector3 flat = Flat(b.transform.position);
                            Place(flat + ToGenerator(flat) * 14f + Vector3.up * 3f, flat + Vector3.up * (b.Entry.HeightMeters + 1.6f), 55f);
                            _cam.enabled = true;
                            yield return Both("building_bubble");
                            _cam.enabled = false;
                            break;
                        }
                    }
                    float running = Time.time - runningSince;
                    if (hud && bubble && ((running > 14f && UrgentBuilding() != null) || running > 70f))   // a building that needs power makes the edge-arrow shot
                    {
                        yield return MarkerShots();
                        done = true;
                    }
                }
                if (shift != null && shift.State == ShiftState.Summary) { done = true; }
                yield return null;
            }

            // the summary
            while (ShiftNet.Instance != null && ShiftNet.Instance.State != ShiftState.Summary) { yield return null; }
            yield return new WaitForSeconds(1.5f);
            Aerial();
            _cam.enabled = true;
            yield return Both("shift_summary");
            Debug.Log("[Shots] shift screenshots done");
            Application.Quit();
        }

        /// <summary>Beside the tallest building, mid-street, the far end, one building out of view, the controls card.</summary>
        private IEnumerator MarkerShots()
        {
            BuildingNet tallest = null;
            Vector3 centre = Vector3.zero;
            int n = 0;
            foreach (BuildingNet b in BuildingNet.All)
            {
                if (b == null || !b.IsSpawned) { continue; }
                centre += Flat(b.transform.position);
                n++;
                if (tallest == null || b.Entry.HeightMeters > tallest.Entry.HeightMeters) { tallest = b; }
            }
            if (n == 0) { yield break; }
            centre /= n;

            // standing next to the tallest building, looking up at its roof
            Vector3 flat = Flat(tallest.transform.position);
            float roof = tallest.Entry.HeightMeters + 1.4f;
            Place(flat + ToGenerator(flat) * 6f + Vector3.up * 1.7f, flat + Vector3.up * (roof - 1.2f), 70f);
            _cam.enabled = true;
            yield return Both("markers_tall_up");

            // mid-street (about 25 m from the middle of the buildings) and the far end (about 55 m), looking along the street
            Vector3 back = centre.sqrMagnitude > 1f ? -centre.normalized : Vector3.back;
            Vector3 eye = Vector3.up * 1.7f;
            Place(centre + back * 25f + eye, centre + Vector3.up * 3f, 60f);
            yield return Both("markers_mid_street");
            Place(centre + back * 55f + eye, centre + Vector3.up * 3f, 60f);
            yield return Both("markers_far_end");

            // a building out of view: stand in the middle and look away from a building that needs power
            BuildingNet urgent = UrgentBuilding() ?? tallest;
            Vector3 u = Flat(urgent.transform.position);
            Vector3 away = u.sqrMagnitude > 1f ? -u.normalized : Vector3.back;
            Place(Vector3.up * 4f, Vector3.up * 3f + away * 20f, 55f);   // above the generator, looking away from the building that needs power
            yield return Both("marker_offscreen");
            _cam.enabled = false;

            // the controls card
            HelpHud.Instance.SetCardOpen(true);
            yield return new WaitForSeconds(0.5f);
            yield return Both("controls_card");
            HelpHud.Instance.SetCardOpen(false);

            // the interaction prompt (Arabic shaping): whenever the bot looks at something it can use
            foreach (string code in new[] { "en", "ar" })
            {
                LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(code);
                // the bot has to look at the same thing for a moment, so that the label has shaped itself in this language
                float waited = 0f, held = 0f;
                while (held < 0.4f && waited < 90f)
                {
                    held = InteractionFocus.Current != null ? held + Time.deltaTime : 0f;
                    waited += Time.deltaTime;
                    yield return null;
                }
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"interaction_prompt_{code}.png"));
                yield return new WaitForSeconds(0.3f);
            }
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
        }

        private static BuildingNet UrgentBuilding()
        {
            foreach (BuildingNet b in BuildingNet.All)
            {
                if (b != null && b.IsSpawned && (b.Marker == MarkerState.Unpowered || b.Marker == MarkerState.Angry)) { return b; }
            }
            return null;
        }

        private static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);

        /// <summary>From a street position back towards the generator at the origin.</summary>
        private static Vector3 ToGenerator(Vector3 flat) => flat.sqrMagnitude > 0.01f ? -flat.normalized : Vector3.back;

        private void Place(Vector3 position, Vector3 lookAt, float fov)
        {
            _cam.transform.position = position;
            _cam.transform.rotation = Quaternion.LookRotation(lookAt - position, Vector3.up);
            _cam.fieldOfView = fov;
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
            Place(back * 12f + Vector3.up * 14f, center + Vector3.up * 1f, 60f);
        }

        private IEnumerator Both(string name)
        {
            foreach (string code in new[] { "en", "ar" })
            {
                LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(code);
                yield return new WaitForSeconds(0.6f);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"{name}_{code}.png"));
                yield return new WaitForSeconds(0.3f);
            }
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
        }
    }
}
