using PowerCutCrew.Breakdowns;
using PowerCutCrew.Buildings;
using UnityEngine;

namespace PowerCutCrew.Shift
{
    /// <summary>
    /// The generator's screen-space markers: one per kind of breakdown, drawn by the same overlay as the buildings' markers (flashing like an unpowered building, with the
    /// breakdown's icon, an off-screen edge arrow and the same nudging when two are up at once). Only the kinds that are active are shown.
    /// </summary>
    public class BreakdownMarkers : MonoBehaviour
    {
        [Tooltip("In the order of BreakdownType: sputter, belt, breaker.")]
        [SerializeField] private BuildingMarker[] _markers = new BuildingMarker[BreakdownTypes.Count];

        public BuildingMarker MarkerOf(BreakdownType t) => _markers[(int)t];

        /// <summary>The marker is on screen (or has an edge arrow) for this kind of breakdown.</summary>
        public bool IsShown(BreakdownType t) => _markers[(int)t] != null && !_markers[(int)t].Hidden;

        private void Update()
        {
            BreakdownNet net = BreakdownNet.Instance;
            for (int i = 0; i < _markers.Length; i++)
            {
                BuildingMarker m = _markers[i];
                if (m == null) { continue; }
                var type = (BreakdownType)i;
                string name = BreakdownTypes.Name(type);
                if (net != null && net.IsSpawned && net.IsActive(type)) { m.ShowBreakdown(i, $"brk.marker.{name}", $"brk.marker.{name}.fix"); }
                else { m.Hide(); }
            }
        }
    }
}
