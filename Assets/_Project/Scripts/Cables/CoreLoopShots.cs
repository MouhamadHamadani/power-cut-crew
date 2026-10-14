using System.Collections;
using System.IO;
using PowerCutCrew.Generator;
using PowerCutCrew.Net;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace PowerCutCrew.Cables
{
    /// <summary>
    /// Development tool for the docs (-shot coreloop -shotdir &lt;dir&gt;, on a bot client): waits for the generator to run and shoots the
    /// panel in English and Arabic, then for a powered socket with a plug in it, then for a snapped cable (plus a frame sequence
    /// for a GIF). A temporary photo camera is put in front of each subject; everything is what this client sees.
    /// </summary>
    public class CoreLoopShots : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (LaunchArgs.Get("-shot") != "coreloop") { return; }
            var go = new GameObject("CoreLoopShots");
            DontDestroyOnLoad(go);
            go.AddComponent<CoreLoopShots>();
        }

        private Camera _cam;
        private string _dir;

        private IEnumerator Start()
        {
            _dir = LaunchArgs.Get("-shotdir");
            Directory.CreateDirectory(_dir);
            yield return LocalizationSettings.InitializationOperation;
            var camObject = new GameObject("PhotoCam");
            camObject.transform.SetParent(transform);
            _cam = camObject.AddComponent<Camera>();
            _cam.depth = 50;
            _cam.fieldOfView = 50f;
            _cam.clearFlags = CameraClearFlags.Skybox;
            _cam.enabled = false;

            // 1. the generator panel, English and Arabic
            GeneratorPanel panel = null;
            while (panel == null || GeneratorNet.Instance == null || GeneratorNet.Instance.State != GeneratorState.Running || GeneratorNet.Instance.Fuel < 2f)
            {
                panel = FindFirstObjectByType<GeneratorPanel>();
                yield return new WaitForSeconds(0.5f);
            }
            Transform p = panel.transform;
            Place(p.position - p.forward * 2.6f, p.position);
            yield return Shoot("generator_panel_en");
            SetLocale("ar");
            yield return new WaitForSeconds(0.4f);
            yield return Shoot("generator_panel_ar");
            SetLocale("en");

            // 2. a plug in a lit socket
            PowerSocket socket = null;
            while (socket == null)
            {
                foreach (PowerSocket s in PowerSocket.All)
                {
                    if (s.Occupied && s.Powered) { socket = s; break; }
                }
                yield return new WaitForSeconds(0.5f);
            }
            Vector3 plug = socket.PlugPoint.position;
            Place(plug + socket.transform.forward * 2.4f + Vector3.up * 0.8f, plug + Vector3.down * 0.2f);
            yield return new WaitForSeconds(0.5f);
            yield return Shoot("cable_plugged_lit_socket");

            // 3. a snapped cable on the ground, and a short frame sequence of the moment (the GIF is assembled from the frames)
            CableNet snapped = null;
            while (snapped == null)
            {
                foreach (CableNet c in CableNet.All)
                {
                    if (c.State == CableState.Snapped && c.Bodies != null && c.Bodies.Length > 0) { snapped = c; break; }
                }
                yield return null;
            }
            // a fixed camera at the break, so the cut ends recoil and fall in front of it
            int mid = Mathf.Clamp(snapped.SnapIndex, 0, snapped.Bodies.Length - 1);
            Vector3 at = snapped.Bodies[mid].position;
            _cam.fieldOfView = 60f;
            Place(at + new Vector3(2.4f, 1.5f, 2.4f), at);
            string frames = Path.Combine(_dir, "snap_frames");
            Directory.CreateDirectory(frames);
            _cam.enabled = true;
            for (int i = 0; i < 24; i++)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(frames, $"f{i:00}.png"));
                if (i == 8) { ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "cable_snapped.png")); }
                yield return new WaitForSeconds(0.12f);
            }
            _cam.enabled = false;
            yield return new WaitForSeconds(1f);
            Debug.Log("[Shots] core loop screenshots done");
            Application.Quit();
        }

        private void Place(Vector3 position, Vector3 lookAt)
        {
            _cam.transform.position = position;
            _cam.transform.rotation = Quaternion.LookRotation(lookAt - position, Vector3.up);
        }

        private IEnumerator Shoot(string name)
        {
            _cam.enabled = true;
            yield return new WaitForSeconds(0.4f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, name + ".png"));
            yield return new WaitForSeconds(0.4f);
            _cam.enabled = false;
        }

        private static void SetLocale(string code)
        {
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(code);
        }
    }
}
