# Prompt 05: buildings and shift (bot run definitions)

Only the verification runs were supplied for the repo; the rest of the brief was not pasted. Add it here when available.

## S1–S4 bot runs

Development build, `-netsim bad`, run one at a time, bot clients launched with `-batchmode -nographics`, all `PowerCutCrew.exe` processes killed between runs.
Every run must have zero errors and zero exceptions.

| Run | Setup | Checks |
|---|---|---|
| S1 | host + 3 bots, `-autostart`, `-shiftseconds 180` | Full shift to Summary. Uptime between 20% and 95% (if bots power everything or nothing the tuning is off: report it). At least one spike, one Angry building and one overheat. |
| S2 | host + 2 bots, a late joiner at 90 s of a 3-minute shift (`-verify` on the joiner) | The late joiner matches the host on time left, money, and every building's state and happiness, plus all the Prompt 04 checks. |
| S3 | host + 3 bots, `-autostart -shifts 2`, 3-minute shifts, so "Another shift" is pressed in between | The second shift starts clean: money 0, happiness 70, no leftover bubbles or held items. |
| S4 | host + 3 bots, kill the host process mid-shift | All clients return to MainMenu with the localized disconnect message. |

S1 report: host upload average and peak, plus uptime, money and Angry count.
S2 needs `-verify` extended to the shift and buildings first: state, time left, money, and each building's window, powered state, happiness, Angry flag and current bubble.
