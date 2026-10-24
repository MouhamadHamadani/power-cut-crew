# PowerCutCrew.Shift

Shift timer and score, the shift screen and the help.

- `ShiftModel` (Lobby, Countdown, Running, Summary; plain C#), `ShiftNet` (host-owned money, state, "Another shift"), `ShiftHud` (strip, countdown, summary), `ShiftBot`, `ShiftVerify`, `ShiftShots` (doc screenshots, `-shot shift`), `BreakdownShots` (`-shot breakdowns`).
- **Help (Prompt 06), local to each machine.** `HelpHud`: the marker legend in Lobby and Countdown, the controls card (action `Help`, H / gamepad Select, bindings read from the Input System, a button hides the
  corner hint, which is remembered in PlayerPrefs), the small "H: controls" hint during a shift, and the tips. `TipLogic` (plain C#, unit tested) decides which tip shows: plug, unpowered, fuel, overheat, each once per session
  (a new session = a new HUD; "Another shift" keeps it), one at a time. Numbers in `HelpTuning`.
- Arabic mirrors the card, the legend and the hint (rows, icons and the corner swap sides). RTLTMPro only shapes a string whose first letter is Arabic, so Arabic strings with a key name start with the Arabic words ("إغلاق: {0}").
- **Breakdowns (Prompt 07).** `BreakdownMarkers`: the generator's three screen-space markers (wrench, belt, breaker lever), drawn by the same overlay as the buildings'. `ShiftNet` starts and ends the breakdowns with the shift, makes a neighbour shout one of its two lines 1 s after a breakdown starts, and puts `BreakdownsFixed` and `AvgFixSeconds` in the summary (shown on the summary screen and compared by `-verify`). `TipLogic` has one tip per breakdown kind.
- **Arabic numbers.** Every number goes through `ArabicNumbers.ForRtl` (Localization): RTLTMPro flips a decimal, a clock time and a percentage between Arabic-range digits, so the logical string is written the way its fixer reverses it and the screen shows "١٩٫٨٪".
