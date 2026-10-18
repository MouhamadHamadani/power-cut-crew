using PowerCutCrew.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PowerCutCrew.Buildings
{
    /// <summary>
    /// The pieces of one building's screen-space marker (a prefab instance under the <see cref="MarkerOverlay"/> canvas): the coloured panel with the status
    /// icon, the amps, the name and the spike badge, the speech bubble stacked above it, and the compact form with an arrow that sticks to the screen edge.
    /// It only shows what it is told; <see cref="BuildingMarker"/> tells it.
    /// </summary>
    public class BuildingMarkerUI : MonoBehaviour
    {
        [SerializeField] private RectTransform _root;
        [SerializeField] private Image _panel;
        [SerializeField] private Image _icon;
        [SerializeField] private Image _arrow;
        [SerializeField] private LocalizedRtlText _amps;
        [SerializeField] private LocalizedRtlText _name;
        [SerializeField] private LocalizedRtlText _spike;
        [SerializeField] private RectTransform _bubble;
        [SerializeField] private LocalizedRtlText _bubbleText;
        [SerializeField] private CanvasGroup _group;
        [Tooltip("Quiet, Powered, Unpowered, Angry (the order of MarkerState without the numbers: index = (int)MarkerState).")]
        [SerializeField] private Sprite[] _icons = new Sprite[4];
        [SerializeField] private Vector2 _fullSize = new Vector2(360f, 150f);
        [SerializeField] private float _compactSize = 100f;
        [SerializeField] private float _bubbleHeight = 110f;
        [SerializeField] private float _bubbleGap = 12f;
        [SerializeField] private float _arrowDistance = 78f;

        private static readonly Color Dark = new Color(0.05f, 0.07f, 0.16f, 1f);
        private static readonly Color Light = new Color32(0xFF, 0xF4, 0xDC, 0xFF);

        private bool _compact;
        private bool _bubbleShown;
        private string _bubbleKey;
        private string _spikeShown;

        public RectTransform Root => _root;
        public bool Compact => _compact;
        public bool BubbleVisible => _bubbleShown;
        public string BubbleKey => _bubbleKey;
        public LocalizedRtlText NameText => _name;
        public LocalizedRtlText AmpsText => _amps;

        /// <summary>The box the marker takes on screen at scale 1, including the bubble while it is up (reference pixels).</summary>
        public Vector2 Size
        {
            get
            {
                if (_compact) { return new Vector2(_compactSize, _compactSize); }
                return _bubbleShown ? new Vector2(Mathf.Max(_fullSize.x, _bubble.sizeDelta.x), _fullSize.y + _bubbleGap + _bubbleHeight) : _fullSize;
            }
        }

        public void SetState(int state)
        {
            Sprite sprite = state >= 0 && state < _icons.Length ? _icons[state] : null;
            if (_icon != null) { _icon.sprite = sprite; _icon.enabled = sprite != null; }
            bool angry = state == 3;
            Color ink = angry ? Light : Dark;
            if (_icon != null) { _icon.color = ink; }
            SetInk(_amps, ink);
            SetInk(_name, ink);
            SetInk(_spike, ink);
        }

        private static void SetInk(LocalizedRtlText t, Color c)
        {
            if (t != null) { t.GetComponent<TMP_Text>().color = c; }
        }

        public void SetPanelColor(Color c)
        {
            if (_panel != null) { _panel.color = c; }
            if (_arrow != null) { _arrow.color = c; }
        }

        public void SetAlpha(float a)
        {
            if (_group != null) { _group.alpha = a; }
        }

        public void SetSpike(bool spiking, float multiplier)
        {
            if (_spike == null) { return; }
            _spike.gameObject.SetActive(spiking && !_compact);
            string shown = spiking ? multiplier.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : null;
            if (shown != _spikeShown && shown != null)
            {
                _spikeShown = shown;
                _spike.Format = text => string.Format(System.Globalization.CultureInfo.InvariantCulture, text, shown);
                _spike.Refresh();
            }
        }

        public void SetBubble(string key)
        {
            bool show = !string.IsNullOrEmpty(key);
            if (show && key != _bubbleKey) { _bubbleText.SetKey(key); }
            _bubbleKey = show ? key : null;
            _bubbleShown = show;
            if (_bubble != null) { _bubble.gameObject.SetActive(show && !_compact); }
        }

        /// <summary>The compact form (icon and an arrow towards the building) for a building that is off screen; <paramref name="arrowAngle"/> in degrees, 0 = right.</summary>
        public void SetCompact(bool compact, float arrowAngle = 0f)
        {
            if (compact != _compact)
            {
                _compact = compact;
                _root.pivot = compact ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 0f);
                _root.sizeDelta = compact ? new Vector2(_compactSize, _compactSize) : _fullSize;
                if (_amps != null) { _amps.gameObject.SetActive(!compact); }
                if (_name != null) { _name.gameObject.SetActive(!compact); }
                if (_spike != null && compact) { _spike.gameObject.SetActive(false); }
                if (_bubble != null) { _bubble.gameObject.SetActive(!compact && _bubbleShown); }
                if (_arrow != null) { _arrow.gameObject.SetActive(compact); }
                if (_icon != null)
                {
                    var rt = _icon.rectTransform;
                    rt.anchoredPosition = compact ? Vector2.zero : new Vector2(-125f, 20f);
                    rt.sizeDelta = compact ? new Vector2(_compactSize - 16f, _compactSize - 16f) : new Vector2(88f, 88f);
                }
            }
            if (compact && _arrow != null)
            {
                float rad = arrowAngle * Mathf.Deg2Rad;
                _arrow.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * _arrowDistance;
                _arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, arrowAngle);
            }
        }

        /// <summary>Compact markers are positioned by their centre, the full ones by the bottom middle (the pivot follows).</summary>
        public void SetPosition(Vector2 canvasPosition)
        {
            _root.anchoredPosition = canvasPosition;
        }
    }
}
