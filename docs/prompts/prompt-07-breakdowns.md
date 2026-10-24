# Prompt 07: breakdowns (3 types with physical fixes)

Branch: `feature/breakdowns` from `main`. Read `CLAUDE.md` and `docs/DESIGN.md` first. Follow the CLAUDE.md verification
checklist. Merge into `main` with `--no-ff` when everything is green. Do not push.

Goal: during a shift the generator breaks in three funny, readable ways, and someone has to walk over and fix each one by hand.
Design rules apply: failure is funny, not frustrating; every breakdown should make a clip; readable in 3 seconds.

## 0. Carry-over fixes from the Prompt 06b screenshots

- `shift_summary_ar.png`: the uptime line renders `٨%.١٩` instead of `١٩٫٨٪`. Fix number + percent formatting in Arabic
  (decimal separator, percent sign position, no reversal) for every formatted number on the summary and HUD. Unit test it.
- `markers_tall_up_ar.png`: another player's name tag ("لاعب ١") is drawn huge when that player stands next to the camera.
  Clamp player name tag size (min/max screen size, in `NetTuning` or `PlayerTuning`).
- `-verify` late-join coverage is thin (S2: 1 full comparison, 7 skipped). Instead of skipping the whole comparison when
  any host value is changing, skip only the changing fields and compare the rest. Report fields compared per run. Target:
  at least 5 comparisons with every stable field checked.

## 1. The three breakdowns

All rules in plain C# in `PowerCutCrew.Breakdowns` (`BreakdownModel`, `BreakdownDirector`), unit-tested. `BreakdownNet`,
interactables and items stay thin. Every number below goes into a new `BreakdownTuning` asset in `Config/Resources/`.
`PowerCutCrew.Breakdowns` may reference `Core`, `Generator` and `Net`; nothing may reference `Breakdowns` except `Shift`
(for the summary), `UI` if needed, and tests.

| Type | Key | Symptom (what everyone sees) | Effect on the game | Fix |
|---|---|---|---|---|
| Sputtering engine | `sputter` | Generator coughs, black smoke puffs, it shakes; every building's lights flicker | Output drops to 0 for `DipSeconds` (0.6) every `DipEverySeconds` (2.5); buildings count as unpowered during dips | Smack the glowing **sweet spot** on the housing with the **wrench** `HitsToFix` (3) times. The sweet spot is one of `SweetSpotCount` (4) points, picked at random per breakdown. A hit elsewhere: loud clang, wrench bounces, no progress |
| Broken fan belt | `belt` | Squeal, grey smoke from the side, the heat bar pulses red | Cooling stops (`CoolPerSecondRunning` → 0) and extra heat `BeltExtraHeatPerSecond` (3) while running | Take a **spare belt** from the spare-parts crate (Interact), carry it to the belt slot on the generator, hold Interact `BeltFitSeconds` (2) to fit it. The old belt flies out as a physics prop and despawns after `OldPartSeconds` (20) |
| Tripped breaker | `breaker` | Spark and a bang at the panel, the breaker lever flips down; one outlet's light goes red | One outlet (one with a plugged cable if any) delivers no power; its building drops | Hold Interact on the breaker lever `BreakerResetSeconds` (1.5). Reset while load is still above `BreakerHoldAmpsRatio` × rated flips it back down after 1 s (funny, and it teaches overload) |

### When breakdowns happen (`BreakdownDirector`, host only, deterministic with `-seed`)

- Only while the shift is Running and the generator is running.
- No breakdown in the first `GraceSeconds` (60) of a shift; at least `MinGapSeconds` (40) between two new breakdowns.
- At most `MaxActive` (2) at once, never two of the same type.
- Each second, each type rolls a chance:
  - `sputter`: `SputterChancePerSecond` (0.004), × `LowFuelMultiplier` (3) when fuel < 25%.
  - `belt`: `BeltChancePerSecond` (0.003), × `HotMultiplier` (3) when heat > 70.
  - `breaker`: `BreakerChancePerSecond` (0.002), × `OverloadMultiplier` (6) while load > rated amps.
- Target: a 10-minute shift sees about 3–5 breakdowns with average play. Report the actual counts from the bot runs.
- An unfixed breakdown never ends the shift on its own and never gets worse in a way that is unrecoverable.

### Generator effects

Add the minimum hooks to `GeneratorModel` / `PowerGrid` (plain C#, unit-tested): an output-dip flag, a cooling-broken flag with
extra heat, and per-outlet trip state. `PowerGrid` must treat a tripped outlet's socket as unfed. The existing generator, cable
and building tests must stay green unchanged.

## 2. Items and interactions

- **Wrench:** a new grabbable `NetworkPhysicsItem` that spawns on a hook next to the generator; respawns there after
  `ItemRespawnSeconds` (30) if it is lost or out of bounds.
  - New Core concept `IUsableItem`: when the held item is usable, `UseHit` uses it instead of throwing it.
  - The wrench swing is a short animation on the holder's local copy. The client sends a hit **request** (target + hit point);
    the host validates range (`HitRange` 2 m), cooldown (`SwingCooldown` 0.4 s) and the sweet spot (`SweetSpotRadius` 0.25 m).
  - Throwing the wrench must still be possible: Drop with velocity, or keep throw on a long press. Pick one and document it.
- **Spare belt:** a grabbable item from the spare-parts crate (an `Interactable` next to the generator, unlimited stock,
  max `MaxLooseBelts` (2) in the world). Fitting is a hold interaction (`Interactable.IsHold`, `HoldRegistry`); it only works
  while holding a belt near the belt slot.
- **Breaker lever:** a hold `Interactable` on the generator panel.
- Exclusive things (belt slot, lever) use `InteractionArbiter`. All prompts are localized and go through `RtlBinding.Compose`.

## 3. Networking

- `BreakdownNet` (on the generator): NetworkVariables for active breakdowns (type flags), sweet spot index, sputter hits done,
  tripped outlet, and fix progress for hold fixes. Host owns everything; clients only send requests.
- Smoke, sparks, shake and flicker are driven locally from the NetworkVariables (no per-frame RPCs).
- Old belts and sparks are cosmetic; old belts may be host-simulated `NetworkPhysicsItem`s or local-only props (choose the
  cheaper one and document why).
- Late join: a joiner sees active breakdowns, sweet spot, progress and tripped outlet. Extend `-verify` with those fields.
- Bandwidth: report host upload avg/peak; it should not grow by more than 10% over the Prompt 06b S1-style run.

## 4. Readability (uses the Prompt 06 marker system)

- The generator gets a screen-space marker while it has an active breakdown, with an icon per type: wrench (`sputter`),
  belt (`belt`), breaker lever (`breaker`), flashing like an Unpowered building, off-screen edge arrow included.
- The sweet spot glows lamp yellow; the belt slot and breaker lever pulse while broken.
- Add the three icons to the legend and the controls card legend (MSA: e.g. «المحرّك يتعطّل: اضربه بالمفتاح على النقطة المضيئة»,
  «انقطع السير: ركّب سيراً جديداً», «فصل القاطع: أعِد رفعه»). English and Arabic.
- First-shift tips (TipLogic): one tip per breakdown type, the first time it happens in the session, with the right binding.
- Neighbour bubble lines (dialect welcome), 2 per type, e.g. sputter: "The lights are blinking like a disco!" /
  «الضو عم يرقص متل الديسكو!». Keep them short.
- Optional sounds: if free sounds are not in the project yet, leave clear hook points (events) and do not add packages.

## 5. Shift summary

Add `Breakdowns fixed` and `Average fix time` to `ShiftSummary`, the summary screen and `-verify`. Localized.

## 6. Bots and launch args

- Bots must fix all three types (go to the generator, fetch wrench / belt, hit the sweet spot, fit, reset), so bot runs exercise
  the whole flow over the network. Bot hits should sometimes miss the sweet spot.
- `-breakdowns off` disables them; `-breakdown <type> -breakdownat <s>` forces one at a time for tests and screenshots.
- `-shot breakdowns` takes the doc screenshots.

## Verification

1. Compile clean, no new `PowerCutCrew.*` warnings.
2. EditMode + PlayMode green (`-testFilter PowerCutCrew`). New EditMode tests: director (grace, gap, caps, no duplicates,
   multipliers, determinism by seed), each fix rule (hits, wrong spot, belt fit, breaker re-trip under overload), generator
   effects (dip, cooling, tripped outlet unfed), Arabic number formatting, per-field verify skipping. New PlayMode net test
   (host + 2 clients): a client fixes each type through requests; a late joiner sees an active breakdown.
3. Screenshots en + ar under `docs/screenshots/`: each breakdown active (with the generator marker), a hit on the sweet spot,
   fitting the belt, the breaker reset, the legend with the new icons, the summary with the new lines.
4. Bot runs, `bad`, 600 s shifts with `-autostart`:
   - B1: host + 3 bots. Zero errors and exceptions. Report breakdowns per type, fixes, average fix time, uptime, money,
     host upload avg/peak.
   - B2: host + 2 bots + late joiner at 300 s with `-verify`: zero mismatches, at least 5 comparisons.
   - B3: `-shifts 2`: the second shift starts with no active breakdowns, no loose belts, the wrench on its hook.
5. Update `docs/DESIGN.md` (breakdowns section with the final numbers), the Breakdowns / Generator / Shift READMEs and
   `docs/NETWORKING.md` (new NetworkVariables and bandwidth).
