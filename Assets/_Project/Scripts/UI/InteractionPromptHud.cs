using PowerCutCrew.Core;
using PowerCutCrew.Localization;
using UnityEngine;
using UnityEngine.Localization;

namespace PowerCutCrew.UI
{
    /// <summary>Shows the prompt of the interactable the local player looks at: "[E] Pick up" / "[E] التقط".</summary>
    public class InteractionPromptHud : MonoBehaviour
    {
        [SerializeField] private LocalizedRtlText _label;
        [SerializeField] private GameObject _root;

        private void Awake()
        {
            _label.Format = text => RtlBinding.Compose("E", text);   // Arabic: the text first, so RTLTMPro shapes it
            _root.SetActive(false);
        }

        private void OnEnable()
        {
            InteractionFocus.Changed += OnFocusChanged;
            OnFocusChanged(InteractionFocus.Current);
        }

        private void OnDisable()
        {
            InteractionFocus.Changed -= OnFocusChanged;
        }

        private string _shownKey;

        private void OnFocusChanged(Interactable target)
        {
            Refresh(target);
        }

        // the prompt can change while the player keeps looking at the same object (generator on/off, what is in hand)
        private void Update()
        {
            Refresh(InteractionFocus.Current);
        }

        private void Refresh(Interactable target)
        {
            string key = target != null ? target.CurrentPromptKey : null;
            if (key == _shownKey) { return; }
            _shownKey = key;
            _root.SetActive(key != null);
            _label.SetKey(key);
        }
    }
}
