using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PowerCutCrew.Buildings
{
    /// <summary>
    /// One screen-space overlay canvas for every building marker and speech bubble. Each frame the markers' world anchors are projected with the camera,
    /// <see cref="MarkerLayout"/> places them (clamped to the edge of the free screen area for urgent buildings that are out of view, pushed up when two overlap),
    /// and the distance only changes their size a little. Beyond <see cref="BuildingTuning.CompactDistance"/> a marker is a compact one (icon and amps; a dot for a Quiet
    /// building), and a marker that was nudged far from its anchor gets a leader line to it.
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
        private Vector2[] _anchor = new Vector2[8];
        private Vector2[] _size = new Vector2[8];
        private float[] _dist = new float[8];
        private float[] _scale = new float[8];
        private int[] _slot = new int[8];

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
                _pos = new Vector2[n]; _anchor = new Vector2[n]; _size = new Vector2[n]; _dist = new float[n]; _scale = new float[n]; _slot = new int[n];
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
            float side = t.ScreenMargin * factor, top = t.TopReserved * factor, bottom = t.BottomReserved * factor;
            int placed = 0;
            for (int i = 0; i < n; i++)
            {
                BuildingMarker m = _markers[i];
                BuildingMarkerUI ui = m != null ? m.Ui : null;
                if (ui == null) { continue; }
                if (cam == null || m.Hidden) { ui.gameObject.SetActive(false); continue; }
                Vector3 world = m.AnchorPosition;
                Vector3 sp = cam.WorldToScreenPoint(world);
                bool behind = sp.z < 0f;
                MarkerPlacement spot = MarkerLayout.Place(new Vector2(sp.x, sp.y), behind, screen, side, top, bottom);
                bool urgent = m.State == MarkerState.Unpowered || m.State == MarkerState.Angry || m.State == MarkerState.Breakdown;
                float distance = Vector3.Distance(cam.transform.position, world);
                if (spot.OnScreen)
                {
                    bool far = distance > t.CompactDistance;
                    MarkerMode mode = !far ? MarkerMode.Full : (m.State == MarkerState.Quiet ? MarkerMode.Dot : MarkerMode.Far);
                    ui.gameObject.SetActive(true);
                    ui.SetMode(mode);
                    float scale = mode == MarkerMode.Dot ? 1f : MarkerLayout.Scale(distance, t.NearDistance, t.FarDistance, t.FarScale);
                    ui.Root.localScale = Vector3.one * scale;
                    ui.SetAlpha(m.State == MarkerState.Quiet ? t.QuietAlpha : 1f);
                    ui.SetPosition(spot.Position / factor);
                    ui.SetLeader(Vector2.zero);
                    if (mode == MarkerMode.Dot) { continue; }   // a dot is tiny and sits on its anchor: it neither nudges nor is nudged
                    _slot[placed] = i;
                    _anchor[placed] = spot.Position;
                    _pos[placed] = spot.Position;
                    _size[placed] = ui.Size * scale * factor;
                    _dist[placed] = distance;
                    _scale[placed] = scale;
                    placed++;
                }
                else if (urgent)
                {
                    // the icon sits further in than the free area, so its arrow stays on screen and out of the HUD strip and the tip panel
                    float reach = t.CompactReach * factor;
                    MarkerPlacement icon = MarkerLayout.Place(new Vector2(sp.x, sp.y), behind, screen, side + t.CompactInset * factor, top + reach, bottom + reach);
                    ui.gameObject.SetActive(true);
                    ui.SetMode(MarkerMode.Edge, icon.ArrowAngle);
                    ui.Root.localScale = Vector3.one;
                    ui.SetAlpha(1f);
                    ui.SetPosition(icon.Position / factor);
                    // the edge icon holds its place: the full markers move around it (distance -1: the first one placed, never moved)
                    _slot[placed] = -1;
                    _anchor[placed] = icon.Position;
                    _pos[placed] = new Vector2(icon.Position.x, icon.Position.y - ui.Size.y * 0.5f * factor);
                    _size[placed] = ui.Size * factor;
                    _dist[placed] = -1f;
                    _scale[placed] = 1f;
                    placed++;
                }
                else
                {
                    ui.gameObject.SetActive(false);
                }
            }

            float ceiling = screen.y - top;   // under the HUD strip
            for (int k = 0; k < placed; k++) { if (_slot[k] >= 0) { _pos[k].y = Mathf.Min(_pos[k].y, ceiling - _size[k].y); } }   // a box that would poke into the strip
            MarkerLayout.Separate(_pos, _dist, _size, placed, t.NudgeGap * factor, ceiling);
            for (int k = 0; k < placed; k++)
            {
                if (_slot[k] < 0) { continue; }
                BuildingMarkerUI ui = _markers[_slot[k]].Ui;
                ui.SetPosition(_pos[k] / factor);
                // nudged far from its building: a thin line leads back to the anchor
                float moved = (_pos[k].y - _anchor[k].y) / factor;
                Vector2 toAnchor = (_anchor[k] - _pos[k]) / (factor * Mathf.Max(0.01f, _scale[k]));
                ui.SetLeader(moved > t.LeaderMinPixels ? toAnchor : Vector2.zero);
            }
        }
    }
}
