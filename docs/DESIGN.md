# Power Cut Crew — Project Knowledge Base

Updated Oct 17, 2026 · @Mouhamad
(Formerly working title "Generator Guys".)

## Overview

**Power Cut Crew** (Arabic: **شباب الموتور**, "Shabab el-Motor") is a 1–4 player online co-op physics game where friends run a neighborhood electricity generator and try to keep the lights on as blackouts get worse. Everything physical goes wrong: cables tangle, diesel spills, neighbors yell and stop paying.

| Item | Value |
|---|---|
| Title | Power Cut Crew (EN) / شباب الموتور (AR) |
| Genre | Co-op physics job-sim ("friendslop" style) |
| Players | 1–4, online, friends via Steam lobbies |
| View / art | First-person, stylized low-poly 3D |
| Platform | Steam (PC) first; console only after a hit |
| Price | $5.99–7.99 |
| Languages | English + Arabic from day one |
| Session | One shift = 10–15 minutes |
| Setting | A Lebanese/Arab neighborhood street during power cuts |

Why it exists: in Lebanon almost every home depends on the neighborhood generator ("motor"), so the theme is real and fresh, while "keep the lights on" is understood everywhere. The tone is playful chaos, never a complaint about the crisis.

## Locked decisions

These are settled; reopen only with a clear reason.

| Decision | Choice | Why |
|---|---|---|
| Concept | Generator Guys concept over the other shortlisted ideas | Highest score (22/25): fresh, clip-friendly, fits the co-op trend |
| Name | **Power Cut Crew** / **شباب الموتور** (decided Oct 11, 2026) | Clear to anyone in 3 seconds; the Arabic title is an instant in-joke for Lebanese players. Pending trademark check (see Open questions) |
| Dimension | 3D low-poly | Every 2025–26 co-op hit is simple 3D; 2D platform/arcade sells poorly on Steam |
| Platform | Steam PC | Premium games are ~78% of Steam revenue; mobile needs UA budget |
| Engine | Unity 6.3 LTS (6000.3.26f1), URP 3D | Existing skills and the Claude Code + Unity CLI pipeline |
| Multiplayer | Host-authoritative P2P via Steam lobbies, Netcode for GameObjects 2.13 + Steamworks.NET | No server costs; standard for friends co-op |
| Price band | $5.99–7.99 | Impulse price that pulls friends in (Meccha Chameleon $5.99, How to Fish $7.99) |
| Localization | English + Arabic at launch | Only ~3.5% of Steam games have Arabic; regional edge |
| Process | Prototype first, then validate with clips and wishlists before full production | Median Steam game earns ~$249; data decides, not hope |
| Overloaded cable (Oct 17) | A cable carrying more amps than its rating heats up, glows, smokes, then melts and snaps. It never refuses to plug in | Failure is funny, not frustrating; clip-worthy; every Lebanese player has seen a thin wire melt |
| Upgrade buyer (Oct 17) | The host buys upgrades | Simple authority; matches host-owned money |
| Upgrade persistence (Oct 17) | Upgrades last for one game session only; a new session starts from zero. A host "Save game" button may come later | Keeps sessions fresh; no save system needed for the prototype |
| Build order (Oct 17) | Prompt 06 readability fix → Prompt 07 breakdowns → Prompt 08 upgrades + cable ratings | Readability blocks playtests; breakdowns are MVP and the best clip source; upgrades give the "one more round" pull |

Published history for context: The Listener, Full Contact (fighting game) and Breakout are on itch.io with modest numbers; this is the first game built to a market-tested formula.

## Name and brand

**Chosen:** Power Cut Crew (English), شباب الموتور / Shabab el-Motor (Arabic). The English name is readable worldwide; the Arabic name uses the Lebanese word for the neighborhood generator, so local players get the joke instantly.

**Why the working title was dropped:** "Generator Guys" reads like a knockoff of Fall Guys / Stumble Guys, and web searches for it are flooded with "Stumble Guys generator" free-gems scam spam, which would bury the game.

**Names considered and rejected:**

| Name | Why not |
|---|---|
| Generator Guys | Knockoff feel; search results polluted by scam spam |
| Blackout Crew | Taken: Drifters: Blackout Crew (4-player VR co-op) |
| Keep the Lights On | Already used as the Steam tagline of another upcoming co-op game, Patch and Pray |
| Wired Up! | Good runner-up; more generic |
| Keep It Running | Clear but generic |
| 5 Amps | Great Lebanese joke (subscriptions are sold by the amp) but fails the "readable in 3 seconds" rule abroad |

Note: these were quick web searches, not a trademark clearance.

### Logo (v1, Oct 11, 2026)

Design canvas: https://claude.ai/artifact/C372YPLJt6DWDiJLyvzwTN (private until shared)

- **Main lockup (1200×675):** "POWER CUT" in chunky, slightly wobbly lamp-yellow letters with a warm glow, under a sagging street cable with a hanging glowing bulb. "CREW" in cream white sits between a cable ending in a plug (left) and a snapped, sparking cable end (right). شباب الموتور underneath in matching chunky Arabic lettering.
- **Icon (512×512):** the knotted cable holding a glowing bulb, with a red spark at the snapped end. For Steam's small capsule sizes and the app icon.
- **Colors:** night blue background #0F1630, lamp yellow #FFC93C (alternates #FFB020, #F5E663), extrude/orange #C2661A, cream #FFF4DC, cable black #05070F, spark red #FF4D3D.
- **Fonts:** Bowlby One (English), Lalezar (Arabic), both free on Google Fonts.
- **Rules:** the tangled cable is the signature motif; the cable knot alone must read at the smallest Steam capsule size; Arabic lockup is drawn to match, not a plain font swap.

## Game design

One shift: a power cut hits, buildings call for power, and the crew races to wire, fuel and fix before the street goes dark.

Core loop (one shift):
1. Power cut starts; buildings light up on the map with requests (how many amps, how urgent).
2. Run cables from the generator to rooftops and balconies; cables are physics ropes that tangle, snap and trip players.
3. Refuel with diesel jerrycans; spills can catch fire.
4. Handle breakdowns before overload: smack the right spot, swap a part, reset the breaker.
5. Neighbors call, complain, haggle and sometimes refuse to pay.
6. Shift ends; collect money; buy upgrades; take on more streets.

Systems:

| System | What it does | Status |
|---|---|---|
| Generator | Pull cord, fuel, load (amps), heat, overheat | Built (Prompts 01–04). Rated 10 A, 2 outlets |
| Cables | Physics rope generator → building socket, plug / pop-out / snap | Built (Prompt 04) |
| Buildings / subscribers | Demand windows, subscription amps, spikes, happiness, Angry, payment | Built (Prompt 05). 4 buildings: 5 + 5 + 5 + 10 A |
| Fuel | Jerrycans, fuel drum; spills and fire later | Jerrycans + drum built; spills/fire not yet |
| Shift timer + score | Lobby → Countdown → Running → Summary, uptime, money, Angry count | Built (Prompt 05) |
| Readability | Screen-space building markers, status icons, legend, controls card, first-shift tips | Built (Prompt 06) |
| Breakdowns | Typed failures with a physical fix (3 types) | Built (Prompt 07): sputtering engine, broken fan belt, tripped breaker |
| Economy / upgrades | Host buys upgrades between shifts; per-cable ratings | Prompt 08 |
| Events | Football final, wedding, heatwave, hospital, rival crew sabotage | Later |
| Voice | Proximity voice chat | Later, optional |

### UI and readability (Prompt 06)

- **Building markers** are screen-space, not world-space: one overlay canvas, one marker per building that follows a world anchor (1.2 m above the roof, at its front edge). Nothing covers them and the size barely changes with distance (full size within 14 m, 85 % at 55 m).
  Each marker shows a status icon, the amps wanted (and the spike badge) and the building name; the speech bubble stacks directly above its marker; when two markers overlap the farther one is pushed up. Quiet buildings are faded so the eye goes to the ones that need power.
- **Off screen:** a building that needs power (Unpowered) or is Angry sticks to the screen edge as a compact icon with an arrow towards it; Quiet and Powered buildings hide. A building behind you lands on the bottom edge (or the nearer side) pointing that way, never at the top;
  the icons and their arrows stay out of the HUD strip, the tip panel and the interaction prompt.
- **Far away:** beyond 40 m a marker is compact (icon and amps, half the width) and a Quiet building is only a small dot. Overlapping markers are pushed up (never below their building) and a marker that was nudged far gets a thin leader line to its building.
- **Icons instead of glyphs:** a moon (Quiet, grey), a lit bulb (Powered, lamp yellow), a flashing lightning bolt (Unpowered, spark red), an angry face (Angry, purple). Drawn by an editor script, no external assets.
- **Help:** a legend of the four icons in the lobby and countdown; a controls card on `H` (gamepad Select) that lists the actions with their current bindings (read from the Input System) and the legend, with a button to hide the corner hint "H: controls";
  first-shift tips, local to each player and each shown once per session: grab a free plug, a building in the dark, low fuel, a hot generator, and one per kind of breakdown. The legend and the card list seven icons: the four moods and the three breakdowns.
- **Arabic:** the UI is MSA (neighbor lines and building names stay in dialect). Numbers use Arabic-Indic digits everywhere. `RtlBinding.Compose` puts a key name next to a text so the Arabic text comes first (the key ends up on the left end of the line); the card, the legend and the corner hint mirror (rows and icons on the right, hint in the bottom right). Key names (E, Space, LMB) come from Unity's Input System and are not translated. RTLTMPro only shapes a string whose first letter is Arabic, so Arabic strings that carry a key name start with the Arabic words.

### Breakdowns (Prompt 07, MVP: 3 types)

Typed failures that hit the generator during a shift. Each has a visible, funny symptom and a physical fix someone has to walk over and do. Nothing here ever ends the shift on its own, and nothing gets worse in a way that cannot be recovered (an unfixed belt overheats the generator, which is a lockout the crew already knows, not a new one).

| Breakdown | What everybody sees | Effect on the game | Fix |
|---|---|---|---|
| **Sputtering engine** (`sputter`) | the generator coughs black smoke and shakes in puffs; every building's lights flicker; a glowing spot on the housing | output is dead for 0.6 s every 2.5 s (the first cough 1.9 s after it starts); every building counts as unpowered during a dip | hit the glowing **sweet spot** with the **wrench** 3 times. The spot is 1 of 4 points on the housing, picked at random per breakdown. A hit elsewhere: loud clang, the wrench bounces in the hand, no progress |
| **Broken fan belt** (`belt`) | squeal, grey smoke from the side, the heat bar pulses red; the belt slot pulses | no cooling while running, and +3 heat per second on top (an idle generator is overheated in about 33 s) | take a **spare belt** from the spare-parts crate (Interact, hands must be empty), carry it to the belt slot and hold Interact for 2 s. The old belt flies out of the slot and is gone after 20 s |
| **Tripped breaker** (`breaker`) | spark and a bang, the lever flops over, one outlet's lamp goes red; the lever pulses | one outlet (one with a cable plugged in, if any) delivers no power, so its building drops | hold Interact on the **lever** for 1.5 s. If the load that would come back is above the rated amps the lever stays up for 1 s and falls back (funny, and it teaches overload): unplug something first |

**When.** `BreakdownDirector` (host only, deterministic with `-seed`): only while the shift runs and the generator runs; none in the first 60 s; at least 40 s between two new ones; at most 2 active, never two of a kind. Every second each type rolls: sputter 0.004 (x3 below 25 % fuel), belt 0.003 (x3 above heat 70), breaker 0.002 (x6 while the load is above the rated amps). With average play a 10-minute shift sees about 3 to 5 (the unit test simulates 300 shifts with a low tank, a hot generator and an overload now and then, every breakdown fixed within 20 to 30 s: 4.9 per shift, 2.0 sputter, 1.4 belt, 1.5 breaker).

**Items.** The **wrench** hangs on a shelf by the generator (it goes back after 30 s when it is lost: below the floor or 20 m from its hook). It is a grabbable item that is *used* instead of thrown: RMB / LT (the use button) swings it; Q (drop) *tosses* it forward at 5 m/s instead of letting it fall, so throwing it is still possible (decision: Q with velocity, not a long press, because the throw button is the swing). The **spare belt** comes out of the crate into your hands (at most 2 belts lie around; the crate is empty-handed only). The **belt slot** and the **breaker lever** are hold interactions with one player at a time (`InteractionArbiter`).

**Readability.** The generator gets a flashing screen-space marker per active breakdown (wrench, snapped belt, breaker; off-screen arrow included), the same overlay as the buildings. The sweet spot glows lamp yellow; the belt slot and the lever pulse while they are what needs doing; a thin bar fills while somebody holds a fix. Legend, controls card, one first-shift tip per kind and two neighbour lines per kind (dialect in Arabic). Sounds are not in the project yet: `BreakdownNet.Sound` is the hook.

**Summary.** `Breakdowns fixed` and `Average fix time` are on the summary screen and in `-verify`.

### Upgrades and cable ratings (Prompt 08)

Bought by the host between shifts with shared money; they last for the game session only (a new session starts from zero).

- **Generator:** outlets (cables) 2 → 3 → 4; rating 10 → 15 → 25 A; bigger fuel tank; cooling fan.
- **Each cable separately (Mouhamad's idea, Oct 17):** thin white 5 A → orange 10 A → thick black 20 A. Upgrading one cable at a time makes "which building gets the thick cable" a real choice.
- **Overload:** a cable carrying more than its rating heats up, glows, smokes, and finally melts and snaps (see Locked decisions).
- Starting tension: 25 A of demand (37.5 A in spikes) against a 10 A generator with 2 cables.

Escalation ideas (the How to Fish lesson: keep getting more absurd): bigger and louder generators, whole districts, a heatwave where every AC turns on, a wedding that must not lose power during the zaffe, the whole street watching a football final, a rival generator crew cutting your cables, and a "mega-blackout" boss shift.

Design rules:
- Failure must be funny, not frustrating.
- Every mechanic should produce a moment worth clipping.
- Readable in 3 seconds by someone who has never seen a Lebanese generator.

## Tech stack and architecture

Unity 6 URP 3D, host-authoritative online co-op over Steam, with the host simulating all physics. The repo's `CLAUDE.md` is the source of truth for technical details.

| Layer | Choice | Notes |
|---|---|---|
| Engine | Unity 6.3 LTS (6000.3.26f1), URP 3D, Mono | Dev machine: Windows 11, RTX 4060 |
| Networking | Netcode for GameObjects 2.13.3 + Unity Transport 2.7.4 | Decided during the prototype |
| Transport / lobbies | Steam P2P (community SteamNetworkingSockets transport) + Steam lobbies, Steamworks.NET | Invite friends, no dedicated servers |
| Ropes / cables | Custom host-only rope, synced via `SnapshotStream`; one spline tube mesh per cable | See `docs/NETWORKING.md` |
| Arabic text | TextMeshPro + RTLTMPro | TMP alone does not shape Arabic |
| Localization | Unity Localization package | String tables: en, ar |
| Voice (later) | Steam voice or Dissonance | Proximity falloff |
| Version control | Git + Git LFS, repo `power-cut-crew` at `C:\Dev\power-cut-crew` | Binary assets in LFS |

Networking rules:
- The host owns the simulation: generator state, cables, fuel, fires, buildings, money.
- Clients send input and interaction requests (grab, attach cable, refuel, hit); the host validates and applies.
- Players' own movement is owner-authoritative (no client prediction); everything physical is host-simulated and synced.
- Build networking into every feature, never retrofit it.

Code structure: one folder and assembly per system (Core, Generator, Cables, Buildings, Fuel, Breakdowns, Shift, Economy, Net, UI, Localization, Editor). Game rules live in plain C# classes; MonoBehaviours and network behaviours stay thin. Tunable numbers live in ScriptableObjects, never hard-coded.

## Art, audio and Arabic localization

Low-poly, colorful, slightly exaggerated: mostly bought or free packs, with unique Lebanese props made in-house. The logo palette (night blue + lamp yellow, see Name and brand) sets the tone for menus and key art.

| Need | Source | Cost |
|---|---|---|
| Base props, buildings | Kenney, Quaternius | Free (CC0) |
| Consistent city style | Synty POLYGON City / Town packs | Paid, low (zero-cost rule applies to the prototype) |
| Characters + animations | Synty characters or Quaternius; Mixamo animations | Free / low |
| Unique props (generator, jerrycans, balcony clutter, cable spaghetti, water tanks, satellite dishes) | Own pipeline: Meshy / Hunyuan3D + auto-rigging where needed | Time |
| Sound effects | Sonniss GDC bundles, freesound | Free |
| Generator rumble, sputter, breaker clicks | Record real generators in Beirut | Free, authentic |

Art direction rules: a dense street of mixed-height buildings, tangled overhead cables as the signature visual, warm lamp light against dark blackouts, and readable silhouettes for every interactive object. Avoid the bleak look of crisis photos.

Arabic rules: Modern Standard Arabic for UI text; Lebanese dialect is welcome in neighbor voice lines and jokes (the Arabic title شباب الموتور is deliberately colloquial). Test right-to-left layout in every UI screen. The Steam page ships in English and Arabic.

## Workflow with Claude Code

Mouhamad directs, playtests and curates art; Claude Code writes and verifies most of the code through the Unity CLI.

The loop:
1. In the Claude Project chat: plan the next slice and draft a self-contained Claude Code prompt (exact scope, tuning defaults, verification steps). Prompts are saved in `docs/prompts/`.
2. Run it in local Claude Code on Windows; it drives the Unity CLI and the Editor to build and verify.
3. Playtest by hand (with friends for multiplayer), note what feels wrong.
4. Bring the report back to the Project chat; update this knowledge base when a decision changes.

| Area | Claude Code | Mouhamad |
|---|---|---|
| Gameplay systems in C# | Writes and verifies | Reviews, tunes |
| Networking + lobbies | Writes scaffolding, fixes bugs | Tests with real clients |
| Scene building (grey-box) | Editor scripts | Adjusts layout |
| UI, localization, save, builds | Writes | Reviews Arabic text |
| Physics feel, difficulty, fun | Adjusts numbers on request | Decides |
| Art, models, animation | Imports, sets up materials | Picks, generates, curates |
| Trailer, clips, marketing | Drafts copy | Records, edits, posts |

### Build log

| Prompt | Content | Status |
|---|---|---|
| 01–03 | Project setup, net foundation (sessions, Steam lobbies, players, grabbable items, bots, metrics), core content | Done |
| 04 | Core loop: generator, fuel, physics cables, sockets | Done |
| 05 | Buildings, shift, neighbors, HUD, summary; plug pop-out fix | Done (Oct 17): 128 EditMode + 13 PlayMode tests green, host + 3 bots on `bad` clean |
| 06 | Readability: screen-space building markers, status icons, legend, controls card, first-shift tips | Done (Oct 18): see `docs/prompts/prompt-06-readability.md` |
| 07 | Breakdowns (3 types) | Planned |
| 08 | Upgrades between shifts + per-cable ratings and melting | Planned |

## Roadmap and validation gates

Each phase ends in a gate; miss it and rethink instead of pushing on.

1. **Prototype (weeks 1–2):** grey-box street, 4 buildings, 1 generator, fuel, 1–3 breakdowns, physics cables, 2–4 players online. Gate: friends ask for another round; 10+ funny moments captured.
2. **Clip test (weeks 3–6):** post 15–30 s clips 3x a week on TikTok, Shorts, Reddit, X, in English and Arabic. Gate: clear engagement and "where can I buy this?" comments.
3. **Steam page (weeks 6–8):** capsule art (built from the logo), short trailer, English + Arabic page. Gate: visit-to-wishlist conversion of at least 12–17%; 18–25% is good.
4. **Demo (months 3–6):** polished 15–20 minute demo, released months before a Steam Next Fest (they run each February, June and October). Gate: ~2,000 wishlists before the fest.
5. **Launch:** when wishlists justify it; around 12% of wishlists buy on day one.

Stop rule: under 1,000 wishlists three months after the Steam page goes live, despite regular clips, means stop and switch ideas.

Backup ideas if it stalls: "Wedding Crashers" (hide-and-seek at an Arab wedding, blending in by copying the guests' dabke) and an Arabic/English Jackbox-style party pack built from the quiz game.

## Market context

Steam is a hits market, and cheap social co-op games are where small teams hit hardest. Full research: New Game Market Research — Oct 2026.

- Steam grossed ~$17.7B in 2025; indies took about a quarter, but the median game earned ~$249 and ~66% earned under $1,000.
- 700+ games launch on Steam each week; only ~13% reach 50 reviews.
- Co-op games had the highest average revenue on Steam in 2025.
- Comparable hits: Meccha Chameleon ($5.99, 2 devs, ~2 months, 15M copies), How to Fish ($7.99, 6.3M copies in its first month), R.E.P.O., PEAK, Schedule I.
- Crowded: survivors-likes, deckbuilders, cozy/shop-keeper, 2D platformers, and "monster hears your microphone" co-op horror.
- Similar-theme games seen during the name check: Patch and Pray (1–4 couch co-op, keep a falling-apart space station powered), Red Wire (co-op wire-cutting bomb defusal).
- MENA: only ~3.5% of Steam games are in Arabic, while ~41% of regional players prefer localized games.

Figures are third-party estimates as of October 2026; sources are listed in the research doc.

## Open questions

- [x] Final game name → **Power Cut Crew / شباب الموتور** (Oct 11, 2026)
- [x] FishNet or Netcode for GameObjects → **Netcode for GameObjects 2.13**
- [x] Steamworks.NET or Facepunch.Steamworks → **Steamworks.NET**
- [x] Unity project location and repo name → `C:\Dev\power-cut-crew`, repo `power-cut-crew`
- [x] Who buys upgrades, and do they persist → host buys; session only (Oct 17)
- [ ] Trademark and Steam store search for "Power Cut Crew" before the store page goes up
- [ ] Logo v1 feedback: test the icon at Steam's smallest capsule size; refine before capsule art
- [ ] Steam payout and tax setup from Lebanon
- [ ] Camera: first-person only, or optional third-person for better clips
- [ ] Solo mode: bots, or a scaled-down street for one player
- [ ] Host "Save game" button for upgrades (later)
- [ ] Upgrade prices vs. shift income (tune in Prompt 08)
- [ ] Who are the 3–5 friends for weekly playtests
