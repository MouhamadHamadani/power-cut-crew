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
            _label.Format = text => "[E] " + text;
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

        private void OnFocusChanged(Interactable target)
        {
            _root.SetActive(target != null);
            _label.SetKey(target != null ? target.PromptKey : null);
        }
    }
}
