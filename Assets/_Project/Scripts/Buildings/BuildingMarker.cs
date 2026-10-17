using System.Globalization;
using PowerCutCrew.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PowerCutCrew.Buildings
{
    /// <summary>
    /// The marker above a building (a world-space canvas that always faces the camera and keeps a readable size from anywhere on the street):
    /// grey while Quiet, lamp yellow while Requesting and powered, flashing spark red while Requesting and unpowered, purple when Angry.
    /// It shows the amps the building wants (1.5 x during a spike) and its name.
    /// </summary>
    public class BuildingMarker : MonoBehaviour
    {
        public static readonly Color QuietColor = new Color32(0x8A, 0x8F, 0x9C, 0xFF);
        public static readonly Color PoweredColor = new Color32(0xFF, 0xC9, 0x3C, 0xFF);   // lamp yellow #FFC93C
        public static readonly Color UnpoweredColor = new Color32(0xFF, 0x4D, 0x3D, 0xFF); // spark red #FF4D3D
        public static readonly Color AngryColor = new Color32(0x7B, 0x2D, 0x8E, 0xFF);

        [SerializeField] private Image _panel;
        [SerializeField] private TMP_Text _status;
        [SerializeField] private LocalizedRtlText _amps;
        [SerializeField] private LocalizedRtlText _name;
        [SerializeField] private TMP_Text _spike;
        [Tooltip("The marker is this many metres wide per metre of distance / 10 (so it keeps about the same size on screen).")]
        [SerializeField] private float _screenScale = 0.07f;
        [SerializeField] private float _minScale = 0.6f;
        [SerializeField] private float _maxScale = 4f;

        private MarkerState _state = (MarkerState)255;
        private float _shownAmps = -1f;
        private string _shownName;
        private Camera _camera;
        private float _baseScale;   // the canvas scale from the prefab (pixels to metres); the screen scale multiplies it
        private float _cameraSearch;

        public MarkerState State => _state;

        public void Show(MarkerState state, float amps, bool spiking, string nameKey)
        {
            if (state != _state)
            {
                _state = state;
                switch (state)
                {
                    case MarkerState.Quiet: _status.text = "z"; break;
                    case MarkerState.Powered: _status.text = "ok"; break;
                    case MarkerState.Unpowered: _status.text = "!"; break;
                    default: _status.text = "x_x"; break;
                }
            }
            if (!Mathf.Approximately(amps, _shownAmps))
            {
                _shownAmps = amps;
                float shown = amps;
                _amps.Format = text => string.Format(CultureInfo.InvariantCulture, text, shown.ToString("0.#", CultureInfo.InvariantCulture));
                _amps.Refresh();
            }
            if (nameKey != _shownName)
            {
                _shownName = nameKey;
                _name.SetKey(nameKey);
            }
            _spike.gameObject.SetActive(spiking);
        }

        private void Update()
        {
            Color c = QuietColor;
            switch (_state)
            {
                case MarkerState.Powered: c = PoweredColor; break;
                case MarkerState.Unpowered: c = Color.Lerp(UnpoweredColor, new Color(0.35f, 0.05f, 0.04f), 0.5f + 0.5f * Mathf.Sin(Time.time * 12f)); break;   // flashing
                case MarkerState.Angry: c = AngryColor; break;
            }
            if (_panel != null) { _panel.color = c; }
        }

        private void LateUpdate()
        {
            if (_camera == null && Time.unscaledTime >= _cameraSearch)
            {
                _camera = Camera.main;
                _cameraSearch = Time.unscaledTime + 1f;
            }
            Camera cam = BuildingBubble.CameraOverride != null ? BuildingBubble.CameraOverride : _camera;
            if (cam == null) { return; }
            Vector3 toMarker = transform.position - cam.transform.position;
            transform.rotation = Quaternion.LookRotation(toMarker, Vector3.up);   // faces the camera
            float distance = toMarker.magnitude;
            transform.localScale = Vector3.one * (_baseScale > 0f ? _baseScale : (_baseScale = transform.localScale.x)) * Mathf.Clamp(distance * _screenScale, _minScale, _maxScale);
        }
    }
}
