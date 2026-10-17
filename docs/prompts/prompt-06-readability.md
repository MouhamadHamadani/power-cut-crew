# Prompt 06: readable building markers, status icons, controls card, first-shift tips

Branch: `feature/readability`. Read `CLAUDE.md` and `docs/DESIGN.md` first. Follow the verification checklist in CLAUDE.md.
Scope is readability only: no new gameplay rules, no changes to building, shift or cable logic.

## Why (playtest after Prompt 05)

1. **Building names hide behind the building.** `BuildingMarker` is a world-space canvas at `height + 1.6 m` above the roof
   centre with a centred pivot, and it scales up with distance (`distance * 0.07`, clamped 0.6–4). At about 25–30 m, the
   panel's lower half (the name row) sinks into the roof. Taller neighbouring buildings also cover it. `BuildingBubble` has
   the same problem and can overlap the marker when both scale up.
2. **The status glyphs mean nothing to players.** `BuildingMarker.Show` writes hard-coded `"z"`, `"ok"`, `"!"`, `"x_x"`
   (and `"x1.5"` for spikes). They are not localized, which breaks the Arabic rule.
3. **There are no control hints anywhere.** F1 is a dev-only debug overlay.

## 1. Screen-space building markers (replace the world-space canvas)

- Draw markers and bubbles on one **screen-space overlay canvas**, not as world-space canvases. Each marker follows a world
  anchor above its building (`height + 1.2 m`, at the front edge of the roof) through `Camera.WorldToScreenPoint`, so nothing
  covers it and it stays the same size at any distance.
- Put the placement math in a **plain C# class** (e.g. `MarkerLayout` in `PowerCutCrew.Buildings`), unit-tested. Inputs:
  screen point, behind-camera flag, screen size, margin. Outputs: on-screen position, on/off-screen flag, edge arrow angle.
- **Off-screen:** a building that is Unpowered or Angry sticks to the nearest screen edge as a compact icon with an arrow
  pointing to it. Quiet and Powered buildings simply hide when off-screen.
- **Distance:** at full size within `NearDistance`, shrinking to `FarScale` at `FarDistance` (never below readable).
  Quiet buildings draw at reduced alpha so the eye goes to the ones that need power.
- **No overlaps:** the bubble stacks directly above its own marker. When two markers overlap on screen, nudge the farther
  one up (simple vertical push in `MarkerLayout`, tested).
- `BuildingNet` keeps driving state through the same `Show(...)` API. `-verify` checks (window, powered, happiness, Angry,
  bubble) must keep passing unchanged. The marker stays in `PowerCutCrew.Buildings`; do not reference `UI`.
- Every number above (anchor offset, margins, near/far distances, scales, alpha, nudge) goes into a `TuningConfig` asset
  (extend `BuildingTuning` or add `MarkerTuning` in `Config/Resources/`).

## 2. Status icons instead of glyphs

| State | Icon | Panel colour (keep current) |
|---|---|---|
| Quiet | sleeping 💤 (or a moon) | grey |
| Powered | lit bulb | lamp yellow `#FFC93C` |
| Unpowered (requesting, no power) | flashing lightning bolt | spark red `#FF4D3D`, flashing |
| Angry | angry face | purple |

- Icons must be free: use a CC0 set (e.g. Kenney Game Icons) or draw simple shapes in an editor script. Record the source in
  `docs/THIRD_PARTY.md`. No emoji fonts unless OFL and recorded.
- Spike badge: localized format string (`bld.spike_format`, "×{0}") instead of the hard-coded `"x1.5"`.
- The amps line and the building name stay localized (`bld.amps_format`, `bld.name.N`). Arabic via RTLTMPro as now.

## 3. Legend and controls card

- **Legend:** during Lobby and Countdown, show a small panel under the lobby hint with the 4 icons and one short line each
  (`help.legend.quiet` "Doesn't need power right now", `help.legend.powered` "Has power", `help.legend.unpowered`
  "Needs a cable now!", `help.legend.angry` "Angry: stopped paying"). The same legend sits at the bottom of the controls card.
- **Controls card:** new `Help` action in `PlayerControls` (keyboard `H`, gamepad `Select`/View). Toggles a HUD card listing
  the actions from their current bindings (read display names from the input system, don't hard-code key letters):
  Move, Look, Jump, Sprint, Crouch, Interact, Grab, Use/Hit, Drop, Help. A small corner hint ("H: controls") is visible
  all the time during a shift and can be turned off on the card.
- All text localized (`help.*` keys), en + ar (MSA). The card mirrors correctly in Arabic (RTL layout, icons on the right).

## 4. First-shift tips (local, once per session)

Short tips at the bottom of the screen, each shown once per game session on each client, only when its trigger happens:

| Key | Trigger |
|---|---|
| `tip.plug` | first time this player is near a free plug: "Grab the plug, carry it to a flashing building" (with the Grab binding) |
| `tip.unpowered` | first building turns Unpowered |
| `tip.fuel` | generator fuel below 25% |
| `tip.overheat` | generator heat above the warning level |

Tip logic (which tip, when, already shown) in a plain C# class, unit-tested. Tip seconds and thresholds in tuning.
State resets with a new session, not with "Another shift".

## Verification

1. Compile clean, no new warnings from `PowerCutCrew.*`.
2. EditMode + PlayMode green (`-testFilter PowerCutCrew`), including new tests for `MarkerLayout` (on-screen, behind
   camera, edge clamp and arrow angle, overlap nudge) and the tip logic.
3. Screenshots in English **and** Arabic (extend `-shot shift` / `ShiftShots`), saved under `docs/screenshots/`:
   standing next to the tallest building looking up; mid-street (~25 m); far end of the street; one building off-screen
   (edge arrow); the controls card open; the lobby legend. In every shot each visible building's name is fully readable.
4. Bot run: host + 3 clients, `bad`, 240 s, `-autostart`, zero errors and exceptions; S2 late joiner with `-verify` passes.
5. Update `docs/DESIGN.md` (UI section) and the Buildings / UI READMEs.
