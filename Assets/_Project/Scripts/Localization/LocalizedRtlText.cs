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
        private TextAlignmentOptions _baseAlignment;

        public LocalizedString Text { get => _text; set => _text = value; }
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
            _label.text = value;
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
