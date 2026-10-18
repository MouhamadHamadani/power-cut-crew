# PowerCutCrew.Buildings

Building demand, subscription tier, happiness and payment, and how a building shows its state.

- `BuildingModel` / `BuildingGroup` / `BuildingDirector`: the rules (plain C#, host only). `BuildingNet`: the NetworkVariables, window lights and the marker anchor.
- **Markers (screen space).** A building has a `BuildingMarker` on a world anchor (`height + MarkerAnchorAboveRoof`, `MarkerAnchorFront` in from the middle, the front edge of the roof). The marker itself is a `BuildingMarkerUI`
  prefab instance under one overlay canvas (`MarkerOverlay`): status icon (moon / bulb / lightning bolt / angry face), amps, name, spike badge (`bld.spike_format`) and the speech bubble stacked above it
  (`BuildingBubble` only forwards the line to the marker). `Show(state, amps, spiking, nameKey)` is the same API as before.
- `MarkerLayout` (plain C#, unit tested): on-screen test with a margin and a reserved strip under the HUD, clamp to the screen edge with an arrow angle (also behind the camera), the distance scale, the vertical push for overlapping markers.
  Unpowered and Angry buildings that are off screen stick to the edge as a compact icon with an arrow; Quiet and Powered ones hide. Quiet markers are drawn at `QuietAlpha`.
- All numbers are in `BuildingTuning` (Markers section). `MarkerOverlay.CameraOverride` lets the doc screenshots project through a photo camera.
