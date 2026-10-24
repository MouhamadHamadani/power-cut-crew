using RTLTMPro;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace PowerCutCrew.Localization
{
    /// <summary>Binds a localized string to an RTLTextMeshPro and swaps font + alignment for Arabic.</summary>
    [RequireComponent(typeof(RTLTextMeshPro))]
    public class LocalizedRtlText : MonoBehaviour
    {
        [SerializeField] private LocalizedString _text = new LocalizedString();
        [SerializeField] private TMP_FontAsset _englishFont;
        [SerializeField] private TMP_FontAsset _arabicFont;

        private RTLTextMeshPro _label;
        private string _last;
        private TextAlignmentOptions _baseAlignment;

        public LocalizedString Text { get => _text; set => _text = value; }

        /// <summary>Optional wrapper for the localized text, for example value => "[E] " + value.</summary>
        public System.Func<string, string> Format { get; set; }

        /// <summary>Re-apply the current string (after changing <see cref="Format"/> or the values it uses).</summary>
        public void Refresh()
        {
            if (_last != null && _label != null) { _label.text = Shown(_last); }
        }

        /// <summary>Show the UI-table string with this key (empty key clears the label). Safe to call at runtime.</summary>
        public void SetKey(string key)
        {
            bool live = isActiveAndEnabled;
            if (live) { _text.StringChanged -= OnStringChanged; }
            _text = string.IsNullOrEmpty(key) ? new LocalizedString() : new LocalizedString("UI", key);
            if (_label == null) { _label = GetComponent<RTLTextMeshPro>(); _baseAlignment = _label.alignment; }
            if (string.IsNullOrEmpty(key)) { _label.text = string.Empty; return; }
            if (live) { _text.StringChanged += OnStringChanged; }
        }
        public TMP_FontAsset EnglishFont { get => _englishFont; set => _englishFont = value; }
        public TMP_FontAsset ArabicFont { get => _arabicFont; set => _arabicFont = value; }

        private void Awake()
        {
            _label = GetComponent<RTLTextMeshPro>();
            _baseAlignment = _label.alignment;
        }

        private void OnEnable()
        {
            _text.StringChanged += OnStringChanged;
        }

        private void OnDisable()
        {
            _text.StringChanged -= OnStringChanged;
        }

        private void OnStringChanged(string value)
        {
            bool arabic = LocalizationSettings.SelectedLocale != null
                && LocalizationSettings.SelectedLocale.Identifier.Code == "ar";
            _label.font = arabic ? _arabicFont : _englishFont;
            _label.alignment = arabic ? Mirror(_baseAlignment) : _baseAlignment;
            _last = value;
            _label.text = Shown(value);
        }

        /// <summary>The formatted text, with Arabic-Indic digits in Arabic and every number in the order RTLTMPro draws it right (one rule for every number on screen).</summary>
        private string Shown(string value)
        {
            string shown = Format != null ? Format(value) : value;
            return RtlBinding.IsArabic ? ArabicDigits.Convert(ArabicNumbers.ForRtl(shown)) : shown;
        }

        private static TextAlignmentOptions Mirror(TextAlignmentOptions a)
        {
            switch (a)
            {
                case TextAlignmentOptions.Left: return TextAlignmentOptions.Right;
                case TextAlignmentOptions.Right: return TextAlignmentOptions.Left;
                case TextAlignmentOptions.TopLeft: return TextAlignmentOptions.TopRight;
                case TextAlignmentOptions.TopRight: return TextAlignmentOptions.TopLeft;
                default: return a;
            }
        }
    }
}
