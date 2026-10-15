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

            // whichever happens first gets shot: a plug seated in a lit socket (close up and from 5 m), a cable swinging in somebody's hand,
            // and a snapped cable (plus a frame sequence for the GIF)
            bool pluggedDone = false, heldDone = false, snapDone = false;
            float pluggedSince = -1f;
            var lastPlug = new System.Collections.Generic.Dictionary<CableNet, Vector3>();
            string frames = Path.Combine(_dir, "snap_frames");
            while (!(pluggedDone && heldDone && snapDone))
            {
                if (!pluggedDone)
                {
                    PowerSocket socket = null;
                    foreach (PowerSocket s2 in PowerSocket.All)
                    {
                        if (s2.Occupied && s2.Powered) { socket = s2; break; }
                    }
                    pluggedSince = socket == null ? -1f : (pluggedSince < 0f ? Time.time : pluggedSince);
                    if (socket != null && Time.time - pluggedSince > 4f)   // the rope has settled by now
                    {
                        Transform pp = socket.PlugPoint;
                        Vector3 plug = pp.position;
                        Place(plug + pp.forward * 1.1f + pp.right * 0.5f + Vector3.up * 0.25f, plug + pp.forward * 0.2f);
                        yield return Shoot("cable_plugged_closeup");
                        Place(plug + pp.forward * 4.5f + pp.right * 1.2f + Vector3.up * 1.6f, plug + pp.forward * 0.8f + Vector3.down * 0.3f);
                        yield return Shoot("cable_plugged_5m");
                        pluggedDone = true;
                        continue;
                    }
                }
                if (!heldDone)
                {
                    foreach (CableNet c in CableNet.All)
                    {
                        if (c.State != CableState.Held || c.Bodies == null) { continue; }
                        Vector3 now = c.transform.position;
                        bool known = lastPlug.TryGetValue(c, out Vector3 before);
                        lastPlug[c] = now;
                        float speed = known ? (now - before).magnitude / Mathf.Max(0.001f, Time.deltaTime) : 0f;
                        if (speed > 1.5f)
                        {
                            // the last stretch of cable and the plug, from the side and a little above
                            var bounds = new Bounds(now, Vector3.zero);
                            for (int b = Mathf.Max(0, c.Bodies.Length - 14); b < c.Bodies.Length; b++) { bounds.Encapsulate(c.Bodies[b].position); }
                            Place(bounds.center + new Vector3(0.7f, 0.5f, 0.7f).normalized * (bounds.extents.magnitude * 1.7f + 2.5f), bounds.center);
                            _cam.enabled = true;
                            yield return new WaitForEndOfFrame();
                            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "cable_held_swing.png"));
                            yield return null;
                            _cam.enabled = false;
                            heldDone = true;
                            break;
                        }
                    }
                }
                if (!snapDone)
                {
                    CableNet snapped = null;
                    foreach (CableNet c in CableNet.All)
                    {
                        if (c.State == CableState.Snapped && c.Bodies != null && c.Bodies.Length > 0) { snapped = c; break; }
                    }
                    if (snapped != null)
                    {
                        // a fixed camera at the break, so the cut ends recoil and fall in front of it
                        int mid = Mathf.Clamp(snapped.SnapIndex, 0, snapped.Bodies.Length - 1);
                        Vector3 at = snapped.Bodies[mid].position;
                        _cam.fieldOfView = 60f;
                        Place(at + new Vector3(1.4f, 1.0f, 1.4f), at);
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
                        _cam.fieldOfView = 50f;
                        snapDone = true;
                    }
                }
                yield return null;
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
