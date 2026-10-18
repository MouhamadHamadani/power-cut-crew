using UnityEngine;

namespace PowerCutCrew.Buildings
{
    /// <summary>A neighbor's speech bubble. It is part of the building's screen-space marker (stacked directly above it), so this only tells the marker which line to show.</summary>
    public class BuildingBubble : MonoBehaviour
    {
        [SerializeField] private BuildingMarker _marker;

        public bool Visible => _marker != null && _marker.Ui != null && _marker.Ui.BubbleVisible;
        public string Key => _marker != null && _marker.Ui != null ? _marker.Ui.BubbleKey : null;

        public void Show(string key)
        {
            if (_marker != null && _marker.Ui != null) { _marker.Ui.SetBubble(key); }
        }

        public void Hide()
        {
            if (_marker != null && _marker.Ui != null) { _marker.Ui.SetBubble(null); }
        }
    }
}
