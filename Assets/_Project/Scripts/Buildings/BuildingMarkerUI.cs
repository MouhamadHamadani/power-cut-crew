using PowerCutCrew.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PowerCutCrew.Buildings
{
    /// <summary>How a building's marker is drawn.</summary>
    public enum MarkerMode
    {
        /// <summary>Icon, amps, name, spike badge and the speech bubble.</summary>
        Full,
        /// <summary>Far away: icon and amps only, about half the width.</summary>
        Far,
        /// <summary>Far away and Quiet: a small dot.</summary>
        Dot,
        /// <summary>Out of view and needing attention: icon with an arrow, stuck to the screen edge.</summary>
        Edge,
    }

    /// <summary>
    /// The pieces of one building's screen-space marker (a prefab instance under the <see cref="MarkerOverlay"/> canvas): the coloured panel with the status
    /// icon, the amps, the name and the spike badge, the speech bubble stacked above it, the compact forms (far, dot, edge arrow) and the leader line to the anchor
    /// when the marker was nudged away from it. It only shows what it is told; <see cref="BuildingMarker"/> and the overlay tell it.
    /// </summary>
    public class BuildingMarkerUI : MonoBehaviour
    {
        [SerializeField] private RectTransform _root;
        [SerializeField] private Image _panel;
        [SerializeField] private Image _icon;
        [SerializeField] private Image _arrow;
        [SerializeField] private Image _dot;
        [SerializeField] private Image _leader;
        [SerializeField] private LocalizedRtlText _amps;
        [SerializeField] private LocalizedRtlText _name;
        [SerializeField] private LocalizedRtlText _spike;
        [SerializeField] private RectTransform _bubble;
        [SerializeField] private LocalizedRtlText _bubbleText;
        [SerializeField] private CanvasGroup _group;
        [Tooltip("Quiet, Powered, Unpowered, Angry (index = (int)MarkerState).")]
        [SerializeField] private Sprite[] _icons = new Sprite[4];
        [SerializeField] private Vector2 _fullSize = new Vector2(360f, 150f);
        [SerializeField] private Vector2 _farSize = new Vector2(200f, 100f);
        [SerializeField] private float _dotSize = 30f;
        [SerializeField] private float _compactSize = 100f;
        [SerializeField] private float _bubbleHeight = 110f;
        [SerializeField] private float _bubbleGap = 12f;
        [SerializeField] private float _arrowDistance = 78f;

        private static readonly Color Dark = new Color(0.05f, 0.07f, 0.16f, 1f);
        private static readonly Color Light = new Color32(0xFF, 0xF4, 0xDC, 0xFF);

        private MarkerMode _mode = MarkerMode.Full;
        private bool _bubbleShown;
        private string _bubbleKey;
        private string _spikeShown;
        private bool _spiking;
        private bool _laidOut;

        public RectTransform Root => _root;
        public MarkerMode Mode => _mode;
        public bool Compact => _mode == MarkerMode.Edge;
        public bool BubbleVisible => _bubbleShown;
        public string BubbleKey => _bubbleKey;
        public LocalizedRtlText NameText => _name;
        public LocalizedRtlText AmpsText => _amps;

        /// <summary>The box the marker takes on screen at scale 1 in its current mode, including the bubble while it is up (reference pixels).</summary>
        public Vector2 Size
        {
            get
            {
                switch (_mode)
                {
                    case MarkerMode.Edge: return new Vector2(_compactSize, _compactSize);
                    case MarkerMode.Dot: return new Vector2(_dotSize, _dotSize);
                    case MarkerMode.Far: return _farSize;
                    default: return _bubbleShown ? new Vector2(Mathf.Max(_fullSize.x, _bubble.sizeDelta.x), _fullSize.y + _bubbleGap + _bubbleHeight) : _fullSize;
                }
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
            if (_dot != null) { _dot.color = c; }
        }

        public void SetAlpha(float a)
        {
            if (_group != null) { _group.alpha = a; }
        }

        public void SetSpike(bool spiking, float multiplier)
        {
            if (_spike == null) { return; }
            _spiking = spiking;
            _spike.gameObject.SetActive(spiking && _mode == MarkerMode.Full);
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
            if (_bubble != null) { _bubble.gameObject.SetActive(show && _mode == MarkerMode.Full); }
        }

        /// <summary>Switch the form of the marker. <paramref name="arrowAngle"/> (degrees, 0 = right) is used by <see cref="MarkerMode.Edge"/>.</summary>
        public void SetMode(MarkerMode mode, float arrowAngle = 0f)
        {
            if (mode != _mode || !_laidOut)
            {
                _laidOut = true;
                _mode = mode;
                bool centered = mode == MarkerMode.Edge || mode == MarkerMode.Dot;
                _root.pivot = centered ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 0f);
                _root.sizeDelta = mode == MarkerMode.Edge ? new Vector2(_compactSize, _compactSize) : mode == MarkerMode.Dot ? new Vector2(_dotSize, _dotSize) : mode == MarkerMode.Far ? _farSize : _fullSize;
                if (_panel != null) { _panel.enabled = mode != MarkerMode.Dot; }
                if (_dot != null) { _dot.gameObject.SetActive(mode == MarkerMode.Dot); }
                if (_arrow != null) { _arrow.gameObject.SetActive(mode == MarkerMode.Edge); }
                if (_icon != null) { _icon.gameObject.SetActive(mode != MarkerMode.Dot); }
                if (_amps != null) { _amps.gameObject.SetActive(mode == MarkerMode.Full || mode == MarkerMode.Far); }
                if (_name != null) { _name.gameObject.SetActive(mode == MarkerMode.Full); }
                if (_spike != null) { _spike.gameObject.SetActive(mode == MarkerMode.Full && _spiking); }
                if (_bubble != null) { _bubble.gameObject.SetActive(mode == MarkerMode.Full && _bubbleShown); }
                if (mode == MarkerMode.Edge || mode == MarkerMode.Dot) { SetLeader(Vector2.zero); }
                if (_icon != null)
                {
                    RectTransform rt = _icon.rectTransform;
                    switch (mode)
                    {
                        case MarkerMode.Edge: rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(_compactSize - 16f, _compactSize - 16f); break;
                        case MarkerMode.Far: rt.anchoredPosition = new Vector2(-58f, 0f); rt.sizeDelta = new Vector2(64f, 64f); break;
                        default: rt.anchoredPosition = new Vector2(-125f, 20f); rt.sizeDelta = new Vector2(88f, 88f); break;
                    }
                }
                if (_amps != null)
                {
                    RectTransform rt = (RectTransform)_amps.transform;
                    if (mode == MarkerMode.Far) { rt.anchoredPosition = new Vector2(34f, 0f); rt.sizeDelta = new Vector2(120f, 70f); }
                    else { rt.anchoredPosition = new Vector2(45f, 20f); rt.sizeDelta = new Vector2(230f, 80f); }
                }
            }
            if (mode == MarkerMode.Edge && _arrow != null)
            {
                float rad = arrowAngle * Mathf.Deg2Rad;
                _arrow.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * _arrowDistance;
                _arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, arrowAngle);
            }
        }

        /// <summary>A thin line from the bottom middle of the marker to its building's anchor (<paramref name="toAnchor"/>, in the marker's own units), for a marker that was nudged away.</summary>
        public void SetLeader(Vector2 toAnchor)
        {
            if (_leader == null) { return; }
            bool on = toAnchor.sqrMagnitude > 4f && (_mode == MarkerMode.Full || _mode == MarkerMode.Far);
            if (_leader.gameObject.activeSelf != on) { _leader.gameObject.SetActive(on); }
            if (!on) { return; }
            RectTransform rt = _leader.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(3f, toAnchor.magnitude);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(toAnchor.y, toAnchor.x) * Mathf.Rad2Deg + 90f);   // the line hangs down from its pivot: turn it towards the anchor
        }

        /// <summary>Edge and dot markers are positioned by their centre, the others by the bottom middle (the pivot follows the mode).</summary>
        public void SetPosition(Vector2 canvasPosition)
        {
            _root.anchoredPosition = canvasPosition;
        }
    }
}
