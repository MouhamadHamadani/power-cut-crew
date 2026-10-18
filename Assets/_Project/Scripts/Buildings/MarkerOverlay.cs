using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PowerCutCrew.Buildings
{
    /// <summary>
    /// One screen-space overlay canvas for every building marker and speech bubble. Each frame the markers' world anchors are projected with the camera,
    /// <see cref="MarkerLayout"/> places them (clamped to the screen edge for urgent buildings that are out of view, pushed up when two overlap), and the
    /// distance only changes their scale a little, so a name is as readable from the far end of the street as next to the building.
    /// </summary>
    public class MarkerOverlay : MonoBehaviour
    {
        /// <summary>Doc screenshots: project through this camera instead of the main one.</summary>
        public static Camera CameraOverride;

        private static MarkerOverlay _instance;

        public static MarkerOverlay Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("BuildingMarkerOverlay");
                    _instance = go.AddComponent<MarkerOverlay>();
                    _instance.Build();
                }
                return _instance;
            }
        }

        private readonly List<BuildingMarker> _markers = new List<BuildingMarker>();
        private Canvas _canvas;
        private RectTransform _root;
        private Camera _camera;
        private float _cameraSearch;
        private Vector2[] _pos = new Vector2[8];
        private Vector2[] _size = new Vector2[8];
        private float[] _dist = new float[8];
        private int[] _slot = new int[8];
        private float[] _scale = new float[8];

        private void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;   // under the HUD (20)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root = (RectTransform)transform;
        }

        public RectTransform Root => _root;

        public BuildingMarkerUI Spawn(BuildingMarkerUI prefab)
        {
            BuildingMarkerUI ui = Instantiate(prefab, _root);
            var rt = ui.Root;
            rt.anchorMin = rt.anchorMax = Vector2.zero;   // positions are canvas units from the bottom left
            return ui;
        }

        public void Register(BuildingMarker m)
        {
            if (!_markers.Contains(m)) { _markers.Add(m); }
        }

        public void Unregister(BuildingMarker m)
        {
            _markers.Remove(m);
        }

        private void LateUpdate()
        {
            int n = _markers.Count;
            if (n == 0) { return; }
            if (_pos.Length < n)
            {
                _pos = new Vector2[n]; _size = new Vector2[n]; _dist = new float[n]; _slot = new int[n]; _scale = new float[n];
            }
            Camera cam = CameraOverride != null ? CameraOverride : _camera;
            if (cam == null && CameraOverride == null && Time.unscaledTime >= _cameraSearch)
            {
                _camera = cam = Camera.main;
                _cameraSearch = Time.unscaledTime + 1f;
            }
            BuildingTuning t = BuildingTuning.Instance;
            float factor = Mathf.Max(0.01f, _canvas.scaleFactor);
            var screen = new Vector2(Screen.width, Screen.height);
            int full = 0;
            for (int i = 0; i < n; i++)
            {
                BuildingMarker m = _markers[i];
                BuildingMarkerUI ui = m != null ? m.Ui : null;
                if (ui == null) { continue; }
                if (cam == null) { ui.gameObject.SetActive(false); continue; }
                Vector3 anchor = m.AnchorPosition;
                Vector3 sp = cam.WorldToScreenPoint(anchor);
                MarkerPlacement placed = MarkerLayout.Place(new Vector2(sp.x, sp.y), sp.z < 0f, screen, t.ScreenMargin * factor, t.TopReserved * factor);
                bool urgent = m.State == MarkerState.Unpowered || m.State == MarkerState.Angry;
                if (placed.OnScreen)
                {
                    ui.gameObject.SetActive(true);
                    ui.SetCompact(false);
                    float scale = MarkerLayout.Scale(Vector3.Distance(cam.transform.position, anchor), t.NearDistance, t.FarDistance, t.FarScale);
                    ui.Root.localScale = Vector3.one * scale;
                    ui.SetAlpha(m.State == MarkerState.Quiet ? t.QuietAlpha : 1f);
                    _slot[full] = i;
                    _pos[full] = placed.Position;
                    _size[full] = ui.Size * scale * factor;
                    _dist[full] = Vector3.Distance(cam.transform.position, anchor);
                    _scale[full] = scale;
                    full++;
                }
                else if (urgent)
                {
                    // the icon sits further in than the screen margin so its arrow stays on screen
                    MarkerPlacement icon = MarkerLayout.Place(new Vector2(sp.x, sp.y), sp.z < 0f, screen, (t.ScreenMargin + t.CompactInset) * factor, (t.TopReserved + t.CompactInset * 0.5f) * factor);
                    ui.gameObject.SetActive(true);
                    ui.SetCompact(true, icon.ArrowAngle);
                    ui.Root.localScale = Vector3.one;
                    ui.SetAlpha(1f);
                    ui.SetPosition(icon.Position / factor);
                    // the edge icon holds its place: the full markers move around it (distance -1: the first one placed, never moved)
                    _slot[full] = -1;
                    _pos[full] = new Vector2(icon.Position.x, icon.Position.y - ui.Size.y * 0.5f * factor);
                    _size[full] = ui.Size * factor;
                    _dist[full] = -1f;
                    full++;
                }
                else
                {
                    ui.gameObject.SetActive(false);
                }
            }
            float ceiling = screen.y - t.TopReserved * factor;   // under the HUD strip
            for (int k = 0; k < full; k++) { if (_slot[k] >= 0) { _pos[k].y = Mathf.Min(_pos[k].y, ceiling - _size[k].y); } }   // a tall roof near the top edge
            MarkerLayout.Separate(_pos, _dist, _size, full, t.NudgeGap * factor, ceiling);
            for (int k = 0; k < full; k++) { if (_slot[k] >= 0) { _markers[_slot[k]].Ui.SetPosition(_pos[k] / factor); } }
        }
    }
}
