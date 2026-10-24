# PowerCutCrew.Breakdowns

Typed failures of the generator, each with a physical fix someone has to walk over and do. May reference `Core`, `Generator` and `Net`; only `Shift` (neighbour lines, markers, summary), `Editor` and the tests reference it.

| Type | Symptom | Fix |
|---|---|---|
| `Sputter` | the engine coughs black smoke and shakes, output dies for 0.6 s every 2.5 s (every building flickers) | hit the glowing sweet spot (1 of 4) with the **wrench** 3 times; a hit elsewhere is a clang and nothing else |
| `Belt` | squeal, grey smoke, the heat bar pulses red, no cooling and +3 heat per second | take a **spare belt** from the crate, hold Interact 2 s at the belt slot |
| `Breaker` | spark and bang, the lever flops over, one outlet's lamp goes red and its building drops | hold Interact on the lever 1.5 s; while the street is still above the rated amps the lever falls back after 1 s |

## Plain C# (unit tested, no objects)

- `BreakdownDirector`: when they happen. Only while the shift runs and the generator runs; none in the first 60 s; at least 40 s between two new ones; at most 2 active, never two of a kind; every second each type rolls (sputter 0.004, x3 below 25 % fuel; belt 0.003, x3 above heat 70; breaker 0.002, x6 above the rated amps). Deterministic for a seed.
- `BreakdownModel`: which are active and for how long, the sweet spot and hits, the fit and reset progress, the re-trip, the fix statistics for the summary. `SputterPattern`, `SweetSpotRule`, `ItemRespawnRule` are its small helpers.
- All numbers: `BreakdownTuning` (`Config/Resources/BreakdownTuning.asset`), built into a `BreakdownSettings` for the plain classes.

## Network and objects (thin)

- `BreakdownNet` (on the generator, host owns everything): NetworkVariables for the active mask, sweet spot, hits, tripped outlet, lever and the two fix progress bars; applies the model to the generator (`OutputDip`, `CoolingBroken`) and to the grid (`SetTripped`); one-shot RPCs for the effects. `Sound` is the event the audio plugs into (no sounds in the project yet).
- `BreakdownVisuals`: smoke, sparks, shake, glowing spot, pulsing slot and lever, the outlet lamps, the progress bars, the old belt flying off (a local prop on every machine: one RPC instead of a network object for something nobody can pick up and that is gone in 20 s).
- `WrenchItem` (an `IUsableItem`: the use button swings it, Q tosses it), `SpareBelt`, `SparePartsCrate`, `BeltSlot`, `BreakerLever`; `BreakdownWorld` spawns the wrench on its hook and the belts; `BreakdownCatalog` holds the two prefabs.
- `BreakdownBot` (fixes all three through the normal input, now and then swings wide on purpose), `BreakdownVerify` (`-verify`: active mask, sweet spot, hits, outlet, lever, progress).
- Launch args: `-breakdowns off` (none), `-breakdown sputter|belt|breaker -breakdownat <s>` (that one is forced `s` seconds into the shift and again `s` seconds after each fix; the dice are off), `-shot breakdowns` (doc screenshots, in `Shift`).
