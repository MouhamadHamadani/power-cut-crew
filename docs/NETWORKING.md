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
   host spawns: players (slot 0..3 at spawn points), 10 cans, the chain                            │
Clients: approval (ConnectionRules: full / version) → scene sync → host spawns their player ◄──────┘

HOST (authority)                                   CLIENT
 physics for cans, chain                            owner-authoritative movement (own player)
 grab arbitration (InteractionArbiter)              raycast → localized prompt → request RPC
 HoldPoint follow of held items                     held item: local copy in the hand
 SnapshotStream sender (20 Hz, only when moving)    SnapshotStream receiver: buffer + interpolate
```

## Message list

| Message | Direction | Delivery | Purpose |
|---|---|---|---|
| connection approval payload | client → host | reliable | build version (UTF-8); host answers approve or a reason key |
| `NetPlayer.InteractRpc(NetworkObjectReference)` | owner → host | reliable | interact / pick up request |
| `NetPlayer.DropRpc()` | owner → host | reliable | drop the held item |
| `NetPlayer.ThrowRpc(Vector3)` | owner → host | reliable | throw the held item |
| `NetPlayer.VerifyRpc(...)` | owner → host | reliable | `-verify` bot runs only: late-join assertion |
| NetworkVariables: player slot, name (owner-write), item holder / holder version / index | host → all | reliable | state |
| NetworkTransform: players (owner authority), items (host authority) | see left | NGO | transforms, unreliable deltas |
| `pcc.snap` named message | host → clients | unreliable-sequenced | `SnapshotStream` snapshot (positions 3 x uint16, rotations 4 bytes) |
| `pcc.snap` (full) | host → one client | reliable-sequenced | full snapshot for a late joiner (requested by `RequestFullSnapshotRpc`) |

Rules of thumb: requests go client → host, state goes host → clients; never trust a client position for anything but its own movement.

## Tuning assets (`Assets/_Project/Config/Resources/`)

- `NetTuning`: max players (4), port, disconnect and connect timeouts, tick rate (30), game and menu scene names,
  interpolation buffer, snapshot rate / extent / heartbeat / motion threshold, held-item blend time, netsim profiles and the calibration factor (0.55).
- `PlayerTuning`: walk / sprint / crouch speeds, jump, gravity, heights, look sensitivity, interaction range, hold point, throw and drop speeds.
- `NetPrefabCatalog` (`Config/`): the network prefabs the session registers and spawns.

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

NGO's integration helpers work on 2.13.3 (they are exposed with `testables`), see `Tests/PlayMode/NetGrabIntegrationTests.cs`.
