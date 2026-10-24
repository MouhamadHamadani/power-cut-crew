using System.Globalization;
using UnityEngine;

namespace PowerCutCrew.Buildings
{
    /// <summary>
    /// A building's marker: this object is only the world anchor (above the roof, on its front edge); the marker itself is a screen-space UI element under the
    /// <see cref="MarkerOverlay"/> canvas, so nothing can cover it and it stays readable at any distance. Grey while Quiet, lamp yellow while Requesting and
    /// powered, flashing spark red while Requesting and unpowered, purple when Angry; it shows a status icon, the amps the building wants (1.5 x during a
    /// spike) and its name.
    /// </summary>
    public class BuildingMarker : MonoBehaviour
    {
        public static readonly Color QuietColor = new Color32(0x8A, 0x8F, 0x9C, 0xFF);
        public static readonly Color PoweredColor = new Color32(0xFF, 0xC9, 0x3C, 0xFF);   // lamp yellow #FFC93C
        public static readonly Color UnpoweredColor = new Color32(0xFF, 0x4D, 0x3D, 0xFF); // spark red #FF4D3D
        public static readonly Color AngryColor = new Color32(0x7B, 0x2D, 0x8E, 0xFF);
        /// <summary>The Unpowered marker flashes between spark red and this light tone, so the dark text stays readable in both.</summary>
        private static readonly Color FlashColor = new Color32(0xFF, 0xB4, 0x9E, 0xFF);

        [SerializeField] private BuildingMarkerUI _uiPrefab;
        [Tooltip("Not drawn until something shows it (the generator's breakdown markers).")]
        [SerializeField] private bool _startHidden;

        private BuildingMarkerUI _ui;
        private MarkerState _state = (MarkerState)255;
        private float _shownAmps = -1f;
        private string _shownName;

        /// <summary>The first index of the breakdown icons in the marker prefab's icon list (after the four building moods).</summary>
        public const int BreakdownIconBase = 4;

        private bool _hidden;
        private int _breakdownIcon = -1;

        public MarkerState State => _state;

        /// <summary>A hidden marker is not drawn at all (the generator's markers while nothing is broken).</summary>
        public bool Hidden => _hidden;
        public BuildingMarkerUI Ui => _ui;
        public Vector3 AnchorPosition => transform.position;

        private void OnEnable()
        {
            if (_uiPrefab == null) { return; }
            MarkerOverlay overlay = MarkerOverlay.Instance;
            _ui = overlay.Spawn(_uiPrefab);
            _ui.gameObject.SetActive(false);   // the overlay shows it once it knows where it is
            overlay.Register(this);
            _hidden = _startHidden;
            _state = (MarkerState)255;
            _breakdownIcon = -1;
            _shownAmps = -1f;
            _shownName = null;
        }

        private void OnDisable()
        {
            if (_ui != null) { Destroy(_ui.gameObject); _ui = null; }
        }

        public void Hide()
        {
            _hidden = true;
            if (_ui != null && _ui.gameObject.activeSelf) { _ui.gameObject.SetActive(false); }
        }

        /// <summary>A generator breakdown: a flashing marker like an unpowered building, with the breakdown's icon, a short title and what to do about it.</summary>
        public void ShowBreakdown(int iconIndex, string titleKey, string fixKey)
        {
            _hidden = false;
            if (_ui == null) { _state = MarkerState.Breakdown; return; }
            if (_state != MarkerState.Breakdown || iconIndex != _breakdownIcon)
            {
                _state = MarkerState.Breakdown;
                _breakdownIcon = iconIndex;
                _ui.SetState(BreakdownIconBase + iconIndex);
                _ui.AmpsText.Format = null;
                _ui.AmpsText.SetKey(titleKey);
                _ui.NameText.SetKey(fixKey);
                _ui.SetSpike(false, 1f);
                _shownAmps = -1f;
                _shownName = fixKey;
            }
        }

        public void Show(MarkerState state, float amps, bool spiking, string nameKey)
        {
            _hidden = false;
            if (_ui == null) { _state = state; return; }
            if (state != _state)
            {
                _state = state;
                _ui.SetState((int)state);
            }
            if (!Mathf.Approximately(amps, _shownAmps))
            {
                _shownAmps = amps;
                float shown = amps;
                _ui.AmpsText.Format = text => string.Format(CultureInfo.InvariantCulture, text, shown.ToString("0.#", CultureInfo.InvariantCulture));
                _ui.AmpsText.Refresh();
            }
            if (nameKey != _shownName)
            {
                _shownName = nameKey;
                _ui.NameText.SetKey(nameKey);
            }
            _ui.SetSpike(spiking, BuildingTuning.Instance.SpikeMultiplier);
        }

        private void Update()
        {
            if (_ui == null) { return; }
            Color c = QuietColor;
            switch (_state)
            {
                case MarkerState.Powered: c = PoweredColor; break;
                case MarkerState.Unpowered:
                case MarkerState.Breakdown: c = Color.Lerp(UnpoweredColor, FlashColor, 0.5f + 0.5f * Mathf.Sin(Time.time * BuildingTuning.Instance.FlashSpeed)); break;   // flashing
                case MarkerState.Angry: c = AngryColor; break;
            }
            _ui.SetPanelColor(c);
        }
    }
}
