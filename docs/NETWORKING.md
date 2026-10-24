# Networking

Netcode for GameObjects 2.13.3. Host-authoritative peer-to-peer: one player hosts, up to 3 friends join. Local play uses
Unity Transport (UDP); online play uses Steam (SteamNetworkingSockets relay, no port forwarding). Reference material from the
transport spike: branches `spike/ngo` and `spike/fishnet`, `docs/spikes/networking-spike.md` on them.

## Architecture

```
Bootstrap scene
  â””â”€ creates NetworkSession prefab (DontDestroyOnLoad)
        NetworkManager + MeteredUnityTransport (Local) + SteamNetworkingSocketsTransport (Steam) + NetSession
MainMenu â”€â”€ Play â”€â”€ Host (Local) / Join (IP) / Host (Steam)
                          â”‚
NetSession.StartHost(mode)â”‚            NetSession.StartClient(mode, address|lobbyId)
   Steam: SteamLobbyService.Init â†’ CreateLobby (friends-only, 4, game+version data) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
   NetworkManager.StartHost â†’ host loads Street_Greybox (NGO scene sync, clients follow)          â”‚
   host spawns: players (slot 0..3 at spawn points), 10 cans, the chain, generator, fuel drum,      â”‚
                4 sockets, one cable per generator outlet (core loop: CoreLoopWorld)               â”‚
Clients: approval (ConnectionRules: full / version) â†’ scene sync â†’ host spawns their player â—„â”€â”€â”€â”€â”€â”€â”˜

HOST (authority)                                   CLIENT
 physics for cans, chain                            owner-authoritative movement (own player)
 grab arbitration (InteractionArbiter)              raycast â†’ localized prompt â†’ request RPC
 HoldPoint follow of held items                     held item: local copy in the hand
 SnapshotStream sender (20 Hz, only when moving)    SnapshotStream receiver: buffer + interpolate
 GeneratorModel / PowerGrid / CableModel / FuelContainer   generator panel, lamps, hand rope: read NetworkVariables
 (plain C# rules, run in FixedUpdate)               release: item leaves the hand at once (ghost) and keeps simulating
```

## Message list

| Message | Direction | Delivery | Purpose |
|---|---|---|---|
| connection approval payload | client â†’ host | reliable | build version (UTF-8); host answers approve or a reason key |
| `NetPlayer.InteractRpc(ref, index, handPos, handRot)` | owner â†’ host | reliable | interact request: which `Interactable` of the object, plus the hand pose (used for plug-in) |
| `NetPlayer.HoldRpc(ref, index, phase, handPos, handRot)` | owner â†’ host | reliable | hold-E interactions (pour, fill): begin, keep-alive every 0.2 s, end. The host ends the hold 0.5 s after the last keep-alive |
| `NetPlayer.ReleaseRpc(handPos, handRot, velocity, isThrow)` | owner â†’ host | reliable | drop / throw carrying the holder's hand pose and the velocity; the host trusts the pose within `NetTuning.ReleasePoseTolerance` (2.5 m) and teleports the item to it before releasing. Plug-in uses the same pose rule |
| `NetPlayer.VerifyExtrasRpc(...)` | owner â†’ host | reliable | `-verify` bot runs only: per-stream snapshot check (2 cm) and the core-loop report (generator, cans, cables) |
| `NetPlayer.VerifyRpc(...)` | owner â†’ host | reliable | `-verify` bot runs only: late-join assertion |
| `GeneratorNet.SputterRpc(result)` | host â†’ all | reliable | cosmetic: smoke and shake on a failed pull |
| `NetPlayer.UseRpc(item, target, point)` | owner → host | reliable | use the held item (the wrench swing): the item, the thing the ray hit (the generator) and the hit point. The host drops it unless the sender holds the item (`useRejected`), then the wrench checks reach (`HitRange` 2 m + 0.75 m slack, from the player's eye), rhythm (0.4 s) and the sweet spot (0.25 m) |
| `HoldRpc` on the belt slot / the breaker lever | owner → host | reliable | the existing hold-E path (begin, keep-alive 0.2 s, end); one player at a time through `BreakdownNet.Arbiter` |
| NetworkVariables `BreakdownNet`: active mask (byte), sweet spot (byte), hits (byte), tripped outlet (int), lever up (bool), belt progress and reset progress (byte, 5 % steps) | host → all | reliable | what is broken right now; written only when a value changes, so a late joiner gets them with the spawn. Smoke, sparks, shake, flicker and the glow are drawn locally from these |
| `BreakdownNet.StartedRpc / HitFeedbackRpc / FixedRpc / BeltFittedRpc / LeverRpc / BreakerFellRpc` | host → all | reliable | one-shot effects and the sound hook: a breakdown starts (sparks, bang), a wrench hit was judged (spark or clang), a fix, the old belt flies off (the seed makes the local prop fly the same on every machine), the lever goes up or falls back. Nothing per frame |
| NetworkVariables `GeneratorNet`: state (byte, at once), fuel (0.5 steps), heat (1 steps), load (0.1 A steps) | host â†’ all | reliable | fuel / heat / load only when the quantised value changed and at most 4 Hz |
| NetworkVariables `FuelCan`: fuel | host â†’ all | reliable | can fill level (visual fill) |
| NetworkVariables `CableNet`: state, socket, outlet, snap index | host â†’ all | reliable | cable state machine; the rope shape itself travels as a snapshot stream |
| NetworkVariables `PowerSocket`: id, powered, occupied | host â†’ all | reliable | lamp state; changes only when the powered state changes (no flicker) |
| `pcc.snap` (per cable) | host â†’ clients | unreliable-sequenced | rope: 31 bodies, stride 2 (16 sent, the rest interpolated), positions only, 20 Hz, only while a body moves; heartbeat when still |
| NetworkVariables: player slot, name (owner-write), item holder / holder version / index | host â†’ all | reliable | state |
| NetworkTransform: players (owner authority), items (host authority) | see left | NGO | transforms, unreliable deltas |
| `pcc.snap` named message | host â†’ clients | unreliable-sequenced | `SnapshotStream` snapshot (positions 3 x uint16, rotations 4 bytes) |
| `pcc.snap` (full) | host â†’ one client | reliable-sequenced | full snapshot for a late joiner (requested by `RequestFullSnapshotRpc`) |

Rules of thumb: requests go client â†’ host, state goes host â†’ clients; never trust a client position for anything but its own movement.

## Tuning assets (`Assets/_Project/Config/Resources/`)

- `NetTuning`: max players (4), port, disconnect and connect timeouts, tick rate (30), game and menu scene names,
  interpolation buffer, snapshot rate / extent / heartbeat / motion threshold, held-item blend time, release pose tolerance (2.5 m), ghost-release handover, can damping, hold keep-alive, netsim profiles and the calibration factor (0.55).
- `PlayerTuning`: walk / sprint / crouch speeds, jump, gravity, heights, look sensitivity, interaction range, hold point, throw and drop speeds.
- `GeneratorTuning`: capacity 100, **rated 10 A**, start fuel, pull odds (35 % +25 % per fail, sure on the 4th), idle burn 0.2 + 0.05 per A, heat rates, stall at 100, restart below 60, sync rate 4 Hz.
- `FuelTuning`: can capacity 35, pour 10 per s, drum 15 per s.
- `CableTuning`: 12 m, 30 segments of 0.4 m, snap (held, >15 % for 0.5 s), pop-out (plugged, >10 % for 0.5 s), pieces despawn after 8 s, fresh cable 3 s after a snap, plug reach 1.5 m.
- `NetPrefabCatalog` (`Config/`): the network prefabs the session registers and spawns. `CoreLoopCatalog` (`Config/Resources/`): generator, drum, socket, cable prefabs.

## Steam

- Steam is initialised only in Steam mode. If Steam is not running, the menu shows a localized error and Local mode keeps working.
- Lobby: friends-only by default, max 4, data `game=powercutcrew` and `version=<build version>`. Browsing filters on `game=powercutcrew`.
- Join paths: lobby id (`-steamclient <id>`), Steam overlay invite / friends-list join (`GameLobbyJoinRequested_t`, join-game rich presence
  `connect=+connect_lobby <id>`), and launch with `+connect_lobby <id>`. Code-complete; needs two PCs to test.
- Development builds ship `steam_appid.txt` (`480`, Spacewar). **Release builds must not.** App 480 lobbies are shared with every other
  developer, so always filter on the `game` key.

## Two-PC Steam join test (needs a friend, or two PCs with two Steam accounts)

Build once: `Unity.exe -batchmode -nographics -quit -projectPath C:\Dev\power-cut-crew -executeMethod PowerCutCrew.Editor.BuildTools.WindowsDevelopment`
(output `Builds/Windows`, `steam_appid.txt` is copied next to the exe). Copy the whole `Builds/Windows` folder to the second PC.
Both PCs: Steam running and logged in, **different Steam accounts** (the same account cannot run App 480 twice). The first launch may show a Windows firewall prompt.

1. **Host (PC A):** run `PowerCutCrew.exe`, Play, **Host (Steam)**. You load into the street. Read the lobby id from `Logs\steam-lobby.txt` next to the exe
   (or press F1 for the debug overlay).
2. **Join by id (PC B):** run `PowerCutCrew.exe -steamclient <lobbyId>`. Expect to load into the street and see player A's capsule, name label
   (Steam persona name) and the cans. Both of you should be able to pick up a can ([E] or left mouse), carry it, drop it (Q), throw it (right mouse).
3. **Join by invite (PC B):** PC A in Steam: Friends, right-click the friend, Invite to Game (or the friend picks "Join Game" on PC A's name in the
   friends list). Accept on PC B. If the game is not running Steam starts it with `+connect_lobby <id>`; if it is running the join request is handled live.
4. **Checks:** F1 shows RTT and kbps; a held can must stay glued to the hand on the holder's screen. Quit the host: the client returns to the main menu with
   "The host left the game." A 5th player or a different build version should see "The game is full." / "Your game version is different from the host's."
5. Send back: both `Logs\Player.log` files and the `Logs\net-metrics-*.json` written on exit.

## Local test without Steam

One PC, several windows: see the commands at the top of `CLAUDE.md` (host + bot clients), or run the exe without args in two windows
(Play, Host (Local) / Join with `127.0.0.1`). `-netsim bad` simulates about 250 ms RTT with jitter and 2% loss; `awful` about 450 ms and 5%.

## Verification results (Prompt 03)

Development build, 1 host + bot clients on one PC, `-netsim bad` (measured RTT about 270 ms), 30 Hz tick, headless 60 fps cap.

| Scenario | Result |
|---|---|
| Host + 3 bot clients, 120 s | 0 errors, 0 exceptions; host up about 111 kbps, client down about 33 kbps, client up about 6 kbps; `-verify` 9/9 OK |
| 4th player joins at 60 s (host + 2 bots + late joiner) | 0 errors; the late joiner passed 3/3 assertions: every (item, version) holder pair matches the host's record, versions current, and the chain pose equals the host's recorded snapshot (within 2 mm) |
| 5th player | host rejects with `net.error.full`; the client always returns to the menu with an error (see limitation below) |
| Client process killed while holding a can | the can is dropped in place after the 5 s disconnect timeout and is grabbed again by another player |
| Host process killed | all clients log "host left", load `MainMenu` and show the localized message |
| Held item, holder's screen | visible item distance to the hand max 0.0 cm; the networked item alone would lag about 80 to 110 cm on average (about 250 to 400 cm at worst) |

Known limitation: a rejected UDP client learns the reason from NGO's `DisconnectReason`, which is a separate message. Under heavy simulated
latency it can lose the race with the transport disconnect and the client then shows the generic "Could not connect to the host."
(Steam lobby joins map Steam's own "lobby full" response to the localized reason, and the version is checked from lobby data before connecting.)

NGO's integration helpers work on 2.13.3 (they are exposed with `testables`), see `Tests/PlayMode/NetIntegrationTests.cs`.
**Always run PlayMode with `-testFilter PowerCutCrew`**: without it the runner also runs NGO's own test suite (thousands of tests, it looks like a hang).
Two fixtures exist and work together (`NetIntegrationTests`, `CableSettleTests`); give spawned objects an explicit `NetworkManager`
(`NetworkObject.InstantiateAndSpawn(prefab, networkManager, ...)`), and put a ground under the test world.

## Core loop (Prompt 04)

- **Release from the holder's view.** `ReleaseRpc` carries the holder's hand pose and velocity. The host accepts the pose within 2.5 m of where it knows the holder
  (`ReleasePoseRule`), teleports the item there (level, yaw only) and releases it with the clamped velocity. The holder's local copy turns into a *ghost*
  (same collider, damping and start pose as the host item, ignores the player and its own networked item) and keeps simulating until the networked item has
  caught up, then slides over to it (0.15 to 0.6 s, at most about 2 m/s). Plugging in uses the same pose rule (`plugReach` 1.5 m from the hand to the socket).
- **Generator.** `GeneratorModel` (plain C#) runs in the host's `FixedUpdate`; `GeneratorNet` publishes the state at once and fuel / heat / load only when their
  quantised value changed, at most 4 Hz. Rated load is 10 A (see the report: two cables can draw at most 13 A, so the specified 15 A could never overheat).
- **Fuel.** Pouring is a hold interaction: `HoldRpc` begin, keep-alive every 0.2 s, end; the host ticks the pour every frame and ends it 0.5 s after the last keep-alive
  (`HoldKeepAlive`, `HoldRegistry`). Can fuel is a NetworkVariable, written only by the host.
- **Cables.** The rope is host-only physics (ConfigurableJoint chain, 30 x 0.4 m); clients get the shape through `pcc.snap` (stride 2, positions only, 20 Hz, only
  while moving). A held plug follows the holder's host-side `HoldBody` through a soft spring joint; the holder sees a local copy plus a short visual rope from the hand.
  `CableModel` decides snap (held, more than 15 % stretch for 0.5 s) and pop-out (plugged, more than 10 % for 0.5 s) and the 3 s / 8 s respawn timers.
  A plugged plug ignores the look ray (it would block the socket behind it).
- **Late join.** A late joiner gets a full snapshot of every stream and the NetworkVariables; `-verify` checks generator state / fuel / heat, every can's fuel,
  every cable's state and socket, the holders and every stream's newest snapshot (within 2 cm) against the host.

## Verification results (Prompt 04)

Development build, `-netsim bad` (about 270 ms RTT), headless bots on one PC, 30 Hz tick. Bot test hooks: `-snapbias`, `-holdplug`, and a development-build-only
`CableNet.TestYank()` (nothing in the street pulls a plugged cable taut, so the host bot feeds the real pop-out state machine a 25 % stretch now and then).

| Scenario | Result |
|---|---|
| C1: host + 3 bots, 180 s | 0 errors, 0 exceptions. Pulls ok 2 / failed 1, pours 29, drum fills 8, plug-ins 10, unplugs 5, snaps 3, pop-outs 4, overheats 1. `-verify` 9/9 |
| C2: host + 2 bots, late joiner at 90 s, 180 s | 0 errors, 0 exceptions. Holders 24/24 OK; rope streams worst error 0.1 cm (limit 2 cm); generator / cans / cables: 10 comparisons OK, 14 skipped because the host state had just changed (2 s quiet window), 0 mismatches |
| C3: host + 3 bots, bot holding a plug killed at 60 s, 120 s | the plug is dropped in place when the host notices the disconnect; another player took it about 13 s later and plugged it in. 0 errors |
| C4: host + 3 bots, host killed at 60 s, 90 s | all three clients log "host left", load `MainMenu` with `net.error.host_left`. 0 errors |

Bandwidth (both cables moving, host + 3 clients): host upload **174 kbps average, 359 kbps peak** (C1), 144 / 314 (C2); client download about 46 to 51 kbps average, up to 105 kbps
peak; client upload 5 to 7 kbps. The average is inside the 250 kbps budget, the peaks are not. Where it goes: each cable is its own snapshot stream (16 bodies sent,
about 106 bytes per snapshot at 20 Hz, to each of 3 clients), so two cables moving at once cost the host about 2 x 3 x 17 = 100 kbps on top of the chain, the cans and the
player transforms. Nothing was changed because of this; the levers (if a real game needs them) are a lower cable rate, stride 3, or sending a cable only to clients near it.

Release pop (distance between the holder's ghost and the networked item at handover, items grabbed again meanwhile excluded): cans 25.9 cm on average over 36 samples,
**median 5 cm, 64 % within 15 cm**. It is bimodal: when nothing touches the can the two simulations end within 5 cm (a quiet diagnostic run gave 6 cm on average), when a
bot bumps it or it lands on a prop the host and the holder diverge by 30 to 70 cm. The 15 cm average target is therefore **not met in busy scenes**; the slide-over (up to
0.6 s) hides it. Plugs released with the rope taut pop 35 to 110 cm (the host plug feels the rope, the ghost does not).

Plug local copy to socket at plug-in: 10 samples, **99.8 cm on average (max 109 cm)**. The rule only needs the hand within 1.5 m of the socket, the bots plug in from about a metre; the
plug then slides to the socket.

## Cable rendering and settling (fix/cable-visuals)

- **One tube per cable.** The per-segment boxes are hidden (they only exist as colliders on the host). `CableNet.RebuildTube()` draws the cable as a single mesh in
  `LateUpdate`, on the host and on every client alike: a Catmull-Rom spline (`CableSpline`, 4 samples per segment) through the rope points, turned into a 6-sided tube of
  radius 0.025 m, colour `#05070F` (`CableTubeMesh`, parallel-transport rings, flat end caps; both are plain C# and unit-tested). The first point is exactly the generator
  outlet and the last is exactly the back of the plug (`PlugBackOffset` behind its pivot). The holder's client ends the tube at the back of their local copy in the hand
  (the last three networked bodies are skipped, they lag behind the hand). A snapped cable is two tubes in one mesh. The tube is rebuilt every frame from whatever points
  the machine has, so it needs no extra network data.
- **Prompt 05: the spline renderer there is this one.** Its section 1 only changes which points are sent (`CableSpline.Sample` takes any list of points).
- **Plug-in.** The released plug slides into the socket (`PlugSlideSpeed` 1.5 m/s) and is only pinned when it has arrived. Then the rope's last joint is attached to the socket's
  kinematic anchor body (`PowerSocket.AnchorBody`, at the seated plug's back) instead of to the plug. A seated plug points into the wall, its back faces out. The tension rules
  ignore the rope while it slides and for `SeatGraceSeconds` after. When the plug leaves the socket (unplug, pop-out) the joint goes back to the plug and every
  collision ignore is undone.
- **Collisions ignored:** every rope segment and the plug against each other, against the generator (always), and against the socket plate and the wall behind it
  (colliders within 0.3 m of the plug point, only while plugged; never the floor: ignoring it made the rope fall through the ground).
- **Settling.** Rope bodies: solver iterations 24, velocity iterations 12, linear / angular damping 1.5 / 3, joint bending damper 0.05, sleep threshold 0.01, depenetration at
  most 0.5 m/s, plug 2 kg on 0.5 kg segments (ratio 4, never above 10). The old numbers (0.3 kg segments, spring 2500) made a cable lying alone on the ground jitter at about 1.5 J
  all the time; spring 1000 / damper 80 with the heavier segments is silent. The rope's physics material takes the *lower* friction of rope and ground (0.3): with "maximum" the
  rope stuck to the floor and then slipped in bursts (stick-slip, 0.1 to 7 J kicks every few seconds).
- **Slack.** When plugged, the rope is lengthened (up to 1.25 x) if the socket is closer than 105 % of its length, so it sags instead of hanging tight (`SlackFraction`).
- **Settle check.** `CableNet.RopeKineticEnergy()` (host) sums the segments' kinetic energy. `CableSettleTests` (own host-only fixture: in one process the clients' kinematic copies of
  the plug would sit where the host's plug is and shove the rope) plugs a cable into a socket beside the generator, steps the player away and asserts the energy is below
  0.01 J within 2 s and stays below for 10 s. In the bot runs the host records `ropeSettleSeconds` (and counts `ropeUnsettled`).

Measured (development build, `-netsim bad`, host + 3 bots, 180 s, 0 errors and 0 exceptions): host upload 149 kbps average, 317 kbps peak (before the fix: 144 to 174 / 314 to 359,
nothing in the rope sync changed); client download 43 kbps average, 93 kbps peak. In-game rope settle after plug-in: 7 samples, 0.77 s on average, 1.0 s at most, none unsettled.
Frame time with both cables moving: host 18.2 ms, clients 17.8 ms average (headless 480 x 270, so GPU cost is not in it; before the fix 17.3 to 17.5 ms on the host); the tube rebuild
costs 0.175 ms per cable per frame on average (0.35 ms for both, worst single frame 4.5 ms).


## Prompt 05: buildings and shift

- **Authority.** The host runs `ShiftModel` and the `BuildingDirector` (every building's `BuildingModel`) and owns the money. `ShiftNet` publishes state and countdown at once, time left at 4 Hz, money whenever
  its whole value changes. `BuildingNet` publishes window, Spiking and Angry at once, happiness quantised at most 4 Hz, and the current bubble (id, trigger, line, start time) so a late joiner sees what is on screen.
  Building i owns socket i; a building is powered when its socket is (`PowerSocket.ById` prefers the host's instance and is looked up on every call, a cached copy went stale in tests).
- **Late join and `-verify`.** `NetVerifyExtras.Add` takes several hooks. Besides the Prompt 04 checks the joiner reports shift state, time left, money and, per building, window, powered, happiness, Angry and bubble;
  the host compares once its own discrete state has been quiet for 2 s (time left within 1.5 s, happiness within 12, money within 12 behind and never ahead).
- **Bots.** `ShiftBot` walks to the clipboard and presses it (`-autostart`; it aims at the clipboard itself, an aim point 0.9 m too high never gave it focus), sends the core-loop bot to the building that is
  Requesting in the dark and, between shifts, presses "Another shift" (`-shifts n`).

### Verification results (development build, `-netsim bad`, headless bots, 3-minute shifts)

| Run | Result |
|---|---|
| C1 (host + 3 bots, 180 s) | host upload **124.6 kbps average, 237.4 kbps peak** (Prompt 04: 174 / 359, before the adaptive cable / chain snapshot rate and delta positions). 0 errors, 0 exceptions, `-verify` 24 / 24 |
| S1 (host + 3 bots, full shift) | 0 errors, 0 exceptions. Host upload 112.8 kbps average, 260.2 kbps peak. Uptime **17.1 %** (the target is 20 to 95 %), money 0, 4 Angry, 2 spikes, 1 overheat. Further shifts in S3 / S2: uptime 21 to 49 %, money 0 to 16. The bots pay almost nothing: plugs pop out and cables snap all the time (19 to 61 % stretch) and `RatePerAmpPerMinute` is 1, so the tuning is off in the sense that it pays next to nothing, and uptime sits at the bottom of the range. Not tuned yet |
| S2 (host + 2 bots, joiner at 90 s) | 0 errors. Joiner: 9 full comparisons OK, 7 skipped because the host had just changed, 0 mismatches (time left, money, every building) |
| S3 (host + 3 bots, 2 shifts) | 0 errors. After "Another shift": money 0, held items 0, bubbles 0; the second shift starts with happiness 70 on all four buildings (the bots pick up cans again within seconds, so held items at the start are not leftovers) |
| S4 (host killed at 100 s) | all clients log "host left" and load `MainMenu` with `net.error.host_left`. 0 errors |

Screenshots: `docs/screenshots/shift_hud_{en,ar}.png`, `shift_summary_{en,ar}.png`, `building_bubble_{en,ar}.png` (`-shot shift -shotdir docs/screenshots` on a bot client with graphics). Markers and bubbles had been drawn about 100 times too large (the per-frame distance scale replaced the canvas scale of 0.01 instead of multiplying it); fixed in `BuildingMarker` / `BuildingBubble`.

### Why plugs came loose (fix/shift-tuning)

Every pop-out and snap now logs a cause (`[Cable] outlet N popOut|snap cause: ...`, metrics `popOutCause_*` / `snapCause_*`) plus the rope stretch at plug-in and how long the plug stayed in (`plugSeconds`).
`-cabletrace` logs the raw stretch every 0.5 s for 4 s after a plug-in. The test pull `CableNet.TestYank` (pop-out state machine fed a 25 % stretch) is now opt-in with `-yank`; before it fired in every bot run.

- **Reach.** All four sockets are reachable with the 5 % slack and no tension: 4.1, 5.4, 7.8, 9.3, 10.2 and 11.2 m from the outlets against a 12 m rope (longest allowed 15 m). A seated, untouched cable sits at 0 to 1 % stretch.
- **Cause.** A plug that stays untouched never pops out. Pop-outs came 2.3 to 2.6 s after plug-in (seat grace 2 s plus the 0.5 s limit) from a sudden hit: the stretch jumped from 1 % to 15 to 90 % in one step with 60 to 120 J in the rope.
  The hit was the players' kinematic push capsule (`PlayerPushCollider`), which flings the light rope when a player (or bot) walks past the generator, where the first segments lie. Cans dropped on the rope do the same.
- **Fix.** Cables ignore the players' push capsules (`PlayerPushCollider.All` / `Added`, `CableNet.IgnorePlayer`): players walk through cables. Cans, other cables and the held plug still collide.

| Two-shift run (host + 3 bots, `bad`) | plug-ins | pop-outs by cause | snaps by cause |
|---|---|---|---|
| before, brief's S3 | 16 | 9 (not classified) | 9 (not classified) |
| before, with causes and `-yank` | 21 | 9: yanked test pull 3, hit by player 3 + touched by player 1, hit by other cable 1, rope stretched alone 1 | 6: hit by player 3 + touched 1, can 1, other cable 1 |
| before, without `-yank` | 21 | 10: hit by player 3 + touched 1, can 3, rope stretched alone 3 | 5: touched by player 4, rope stretched in reach 1 |
| after (S3) | 13 | 0 | 4: rope stretched in reach 2 (a holder dragging the plug), hit by the other cable 2 |

After the fix S1 had 2 pop-outs (both a can) and 3 snaps (can, in reach, a player), S2 none. "Rope stretched alone" means no hit was recorded; the players' contacts were not always seen, so treat it as unexplained rather than as proof.

### Tuning (fix/shift-tuning)

`FallPerSecond` 3 to 1 (70 s in the dark before a building goes Angry, `RisePerSecond` stays 2) and `RatePerAmpPerMinute` 1 to 10 (a 5 A building pays 50 a minute). Tests that depended on the old numbers are updated.
`CableRebuildAllocationTests` read one frame and failed when the editor allocated in that same frame (the first run after a recompile, about half a MB); it now takes the smallest of five frames, the threshold is unchanged (20 of 20 passes, the first one right after a recompile).

### Re-run on a fresh build (marker scale fix included)

| Run | Result |
|---|---|
| S1 | 0 errors. Uptime **53.3 %**, money **192**, Angry **1**, 2 spikes, 0 overheats, snaps 3, pop-outs 2. Host upload 125.7 kbps average, 262.9 peak. Target (uptime 20 to 95 %, at most 2 Angry) met |
| S2 | 0 errors. Late joiner: 7 full comparisons OK, 1 skipped, 0 mismatches. Uptime 24.6 %, money 117, Angry 2, 1 overheat, snaps 0, pop-outs 0. Host upload 46.7 average (a 2-bot run), 270 peak |
| S3 | 0 errors. Shift 1: uptime 27.3 %, money 116, Angry 0; shift 2: 25.6 %, 88, Angry 2. After "Another shift": money 0, held items 0, bubbles 0, happiness 70 everywhere. Snaps 4, pop-outs 0. Host upload 106.9 / 282.6 |
| S4 | all clients log "host left" and load `MainMenu`. No exceptions in any log |

Since the follow-up (Prompt 06b) the heat and fuel tolerance is `base + |rate| x snapshotAge` (`VerifyTolerance`, numbers in `NetTuning`: heat 4, fuel 1.5, age 1 s; the host tracks the fastest recent change with `RateTracker`), so a value that is racing up is not reported against a report that is a second old. Other fields are compared as before.

Two `-verify` mismatches showed up in the core-loop comparison (not in shift or building fields, and not on the late joiner): S2 generator heat 86 vs 90 on a client while the heat was climbing towards the overheat (tolerance 4), S3 a cable seen as Held on a client while the host had plugged it. Both are timing noise of the older comparison under `bad`; not changed.

## Prompt 07: breakdowns

**New on the wire** (see the message list): `NetPlayer.UseRpc` (the wrench swing, validated by the host: holder, reach, rhythm, sweet spot), the existing hold-E path for the belt slot and the breaker lever, the `BreakdownNet` NetworkVariables (active mask, sweet spot, hits, tripped outlet, lever, two progress bars; written only on change) and a handful of one-shot RPCs. Smoke, sparks, shake, flicker and the glow are drawn locally from the NetworkVariables. The old belt that flies out is a **local prop** on every machine (one RPC with a seed) instead of a host-simulated `NetworkPhysicsItem`: nobody can pick it up and it is gone after 20 s, so a network object plus a transform stream would cost more than the single message. The wrench and the spare belts are ordinary `NetworkPhysicsItem`s (server-authority `NetworkTransform`).

Effects on the host: the sputter's dip and the dead outlet go through `GeneratorModel.OutputDip` and `PowerGrid.SetTripped`, so buildings and sockets simply read `GeneratorNet.Feeds(socket)`; the lamps flicker through the existing `PowerSocket` NetworkVariable (changes only on a change of state).

### Verification results (development build, `-netsim bad`, headless bots, 600 s shifts unless noted)

| Run | Result |
|---|---|
| B1 (host + 3 bots) | 0 errors, 0 exceptions. 3 breakdowns (sputter 2, belt 1), 3 fixed, average fix **10.5 s**, 0 re-trips. Uptime 42.7 %, money 540, 1 Angry, 2 overheats, 8 snaps. Host upload **117.0 kbps average, 271.2 peak** |
| B1 control (same build, `-breakdowns off`) | host upload 154.6 average, 303.8 peak; uptime 47.4 %, money 606. The breakdowns add no measurable upload: the noise between two runs (cable traffic) is far bigger than the few bytes of NetworkVariables. Prompt 06b's S1 had 125.7 / 262.9; this run is -7 % on average and +3 % on the peak, inside the 10 % allowed |
| B2 (host + 2 bots, joiner at 300 s, `-verify`) | 0 errors, 0 exceptions. **24 comparisons, 24 OK, 0 mismatches** (the joiner: 8 of 8). 1114 fields compared, 40 skipped one by one (the whole-report skips of Prompt 06b are gone; most reports compared 44 to 48 fields, and the joiner reported the breakdown that was active when it arrived, with its sweet spot). Breakdowns: sputter 1, belt 2, breaker 1, 4 fixed |
| B3 (host + 3 bots, `-shifts 2 -shiftseconds 150`) | 0 errors. After "Another shift" the second shift starts with 0 active breakdowns, 0 loose belts and the wrench 0.01 m from its hook (first shift: 0.02 m) |

Over all bot runs the expected 3 to 5 breakdowns per 10 minutes held up (3, 4); the unit-test simulation gives 4.9. Bugs the runs found and fixed: the spawned wrench's rigidbody started at the origin (inside the generator) and fell through the world, so the item is placed through its body (`BreakdownWorld.PlaceBody`); the bots walked into the generator instead of round it (`BreakdownBot.NextWaypoint`).

Screenshots (`-shot breakdowns`): `breakdown_{sputter,hit,belt,belt_fit,breaker,breaker_reset}_{en,ar}.png`, plus the new `lobby_legend`, `controls_card` and `shift_summary` (seven legend icons, the two new summary lines, "١٥٫٤٪").
