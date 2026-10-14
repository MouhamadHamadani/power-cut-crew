# Networking

Netcode for GameObjects 2.13.3. Host-authoritative peer-to-peer: one player hosts, up to 3 friends join. Local play uses
Unity Transport (UDP); online play uses Steam (SteamNetworkingSockets relay, no port forwarding). Reference material from the
transport spike: branches `spike/ngo` and `spike/fishnet`, `docs/spikes/networking-spike.md` on them.

## Architecture

```
Bootstrap scene
  └─ creates NetworkSession prefab (DontDestroyOnLoad)
        NetworkManager + MeteredUnityTransport (Local) + SteamNetworkingSocketsTransport (Steam) + NetSession
MainMenu ── Play ── Host (Local) / Join (IP) / Host (Steam)
                          │
NetSession.StartHost(mode)│            NetSession.StartClient(mode, address|lobbyId)
   Steam: SteamLobbyService.Init → CreateLobby (friends-only, 4, game+version data) ──────────────┐
   NetworkManager.StartHost → host loads Street_Greybox (NGO scene sync, clients follow)          │
   host spawns: players (slot 0..3 at spawn points), 10 cans, the chain, generator, fuel drum,      │
                4 sockets, one cable per generator outlet (core loop: CoreLoopWorld)               │
Clients: approval (ConnectionRules: full / version) → scene sync → host spawns their player ◄──────┘

HOST (authority)                                   CLIENT
 physics for cans, chain                            owner-authoritative movement (own player)
 grab arbitration (InteractionArbiter)              raycast → localized prompt → request RPC
 HoldPoint follow of held items                     held item: local copy in the hand
 SnapshotStream sender (20 Hz, only when moving)    SnapshotStream receiver: buffer + interpolate
 GeneratorModel / PowerGrid / CableModel / FuelContainer   generator panel, lamps, hand rope: read NetworkVariables
 (plain C# rules, run in FixedUpdate)               release: item leaves the hand at once (ghost) and keeps simulating
```

## Message list

| Message | Direction | Delivery | Purpose |
|---|---|---|---|
| connection approval payload | client → host | reliable | build version (UTF-8); host answers approve or a reason key |
| `NetPlayer.InteractRpc(ref, index, handPos, handRot)` | owner → host | reliable | interact request: which `Interactable` of the object, plus the hand pose (used for plug-in) |
| `NetPlayer.HoldRpc(ref, index, phase, handPos, handRot)` | owner → host | reliable | hold-E interactions (pour, fill): begin, keep-alive every 0.2 s, end. The host ends the hold 0.5 s after the last keep-alive |
| `NetPlayer.ReleaseRpc(handPos, handRot, velocity, isThrow)` | owner → host | reliable | drop / throw carrying the holder's hand pose and the velocity; the host trusts the pose within `NetTuning.ReleasePoseTolerance` (2.5 m) and teleports the item to it before releasing. Plug-in uses the same pose rule |
| `NetPlayer.VerifyExtrasRpc(...)` | owner → host | reliable | `-verify` bot runs only: per-stream snapshot check (2 cm) and the core-loop report (generator, cans, cables) |
| `NetPlayer.VerifyRpc(...)` | owner → host | reliable | `-verify` bot runs only: late-join assertion |
| `GeneratorNet.SputterRpc(result)` | host → all | reliable | cosmetic: smoke and shake on a failed pull |
| NetworkVariables `GeneratorNet`: state (byte, at once), fuel (0.5 steps), heat (1 steps), load (0.1 A steps) | host → all | reliable | fuel / heat / load only when the quantised value changed and at most 4 Hz |
| NetworkVariables `FuelCan`: fuel | host → all | reliable | can fill level (visual fill) |
| NetworkVariables `CableNet`: state, socket, outlet, snap index | host → all | reliable | cable state machine; the rope shape itself travels as a snapshot stream |
| NetworkVariables `PowerSocket`: id, powered, occupied | host → all | reliable | lamp state; changes only when the powered state changes (no flicker) |
| `pcc.snap` (per cable) | host → clients | unreliable-sequenced | rope: 31 bodies, stride 2 (16 sent, the rest interpolated), positions only, 20 Hz, only while a body moves; heartbeat when still |
| NetworkVariables: player slot, name (owner-write), item holder / holder version / index | host → all | reliable | state |
| NetworkTransform: players (owner authority), items (host authority) | see left | NGO | transforms, unreliable deltas |
| `pcc.snap` named message | host → clients | unreliable-sequenced | `SnapshotStream` snapshot (positions 3 x uint16, rotations 4 bytes) |
| `pcc.snap` (full) | host → one client | reliable-sequenced | full snapshot for a late joiner (requested by `RequestFullSnapshotRpc`) |

Rules of thumb: requests go client → host, state goes host → clients; never trust a client position for anything but its own movement.

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
Keep one `NetcodeIntegrationTest` fixture per assembly (all net tests live in `NetIntegrationTests`), give spawned objects an explicit `NetworkManager`
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

