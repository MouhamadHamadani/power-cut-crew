# Prompt 06b: readability follow-up fixes

Work on `main` in a branch `fix/readability-followup`. Read `CLAUDE.md`. Follow its verification checklist. Small fixes only,
no gameplay changes. Merge into `main` when everything is green.

## 1. Off-screen marker for a building behind the player

In `markers_tall_up_en.png`, `marker_offscreen_en.png` and `controls_card_ar.png`, a building behind the camera shows as an edge
icon at the **top centre**, pointing **up**, and its arrow tip pokes into the HUD money ("0 $").
- A point behind the camera must be mirrored before clamping (flip the projected direction), so a building behind you lands on
  the **bottom** edge or the nearer **side** edge, with the arrow pointing that way. Never top-centre by default.
- Edge icons (and their arrows) must stay fully outside the reserved HUD strip and the tip panel. Add the tip panel area to the
  reserved rects in `MarkerLayout`.
- New `MarkerLayout` tests: directly behind, behind-left, behind-right, behind and slightly above; none ends up in a reserved rect.

## 2. Far-away markers stacking into a column

In `markers_far_end_en.png` the overlap nudge stacks four markers into a tall column that ends well below the horizon, so it is
hard to tell which marker belongs to which building.
- Beyond `CompactDistance` (tuning), draw a **compact marker**: icon + amps only, no name, about half the width. Quiet buildings
  beyond that distance show only a small grey dot.
- When a marker has been nudged more than `LeaderMinPixels` from its anchor, draw a thin leader line from the marker to the anchor.
- Nudge direction: up only (the spec), never below the anchor.
- Retake the far-end screenshots (en + ar).

## 3. Arabic text

- **UI is MSA** (CLAUDE.md). The legend and tips are in Lebanese dialect; rewrite them in MSA. Neighbour bubble lines and
  building names stay in dialect. Suggested:
  - `help.legend.quiet` لا تحتاج كهرباء الآن
  - `help.legend.powered` الكهرباء واصلة
  - `help.legend.unpowered` تحتاج كابلاً الآن!
  - `help.legend.angry` غاضبة: توقفت عن الدفع
  - `tip.unpowered` مبنى بلا كهرباء! صِل كابلاً بمقبسه
  - Review the other `tip.*` and `help.*` Arabic strings the same way.
- **Interaction prompt shaping bug:** the Arabic interaction prompt (`[E] …`) renders unshaped because RTLTMPro only shapes a
  string whose first letter is Arabic. Fix it at the source for every string, not by reordering each one: a helper in
  `PowerCutCrew.Localization` that builds "binding + text" so the Arabic text comes first in the logical order and the key
  is placed on the correct side in RTL. Use it in `InteractionPromptHud`, the controls card and the tips. Unit test it.
- Gamepad names: show short names (`LS`, `RS`, `L3`, `R3`, `A`, `B`, `X`, `Y`, `LT`, `RT`, `Select`) instead of "Left Stick Press".
- Digits: the HUD time uses Western digits while amps use Arabic-Indic digits in Arabic. Pick one rule (Arabic-Indic in Arabic)
  and apply it to the time, money, load and summary numbers.

## 4. `-verify` heat tolerance

The core-loop check flagged generator heat 92 vs 98 while heat was rising fast. Make the heat (and fuel) tolerance include the
change rate: `tolerance = base + |rate| * snapshotAgeSeconds`, numbers in tuning. Unit test it. Do not loosen other fields.

## 5. Git

Prompt 06 ended up committed straight on `main` (the `feature/readability` branch was renamed to `main`; nothing was lost).
Keep using one branch per prompt and merge with `--no-ff` like Prompts 04–05.

## Verification

1. Compile clean, no new `PowerCutCrew.*` warnings.
2. EditMode + PlayMode green (`-testFilter PowerCutCrew`).
3. Retake all Prompt 06 screenshots in en + ar. Check: no edge icon at top centre for a building behind the player, nothing
   overlaps the HUD strip or tip panel, far-end markers are readable and clearly linked to their buildings, and the Arabic
   interaction prompt is shaped (add a screenshot of it: `interaction_prompt_ar.png`).
4. Bot run host + 3 clients, `bad`, 240 s, zero errors; S2 late joiner with `-verify`: zero mismatches.
