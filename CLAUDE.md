# Power Cut Crew (شباب الموتور)

1–4 player online co-op physics game about running a neighborhood electricity generator during blackouts. Players
fuel the generator, run physics-rope cables to buildings, keep it from overheating and fix funny breakdowns while
neighbors complain. Free tools only (Unity Personal, OSS packages, OFL fonts); see `docs/THIRD_PARTY.md`.
Design knowledge base: `docs/DESIGN.md`. Networking details and the two-PC Steam test: `docs/NETWORKING.md`.

## Unity

- Version: **6000.3.26f1** (Unity 6.3 LTS), URP 3D, Windows x64, Mono, .NET Standard 2.1.
- Editor: `C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe`
- A Unity process locks the project. Batchmode needs the Editor closed; with an Editor open, drive it via
  `unity command ...` (com.unity.pipeline) instead.
- Run Unity from PowerShell with `Start-Process -Wait -PassThru` (a bare `&` returns before Unity finishes).
- NGO's own tests are enabled (`testables`) for its integration helpers, so always pass `-testFilter PowerCutCrew` (without it PlayMode runs thousands of NGO tests and looks hung).
- Net integration tests: `NetIntegrationTests` (host + 2 clients; the shift tests loosen `CableTuning` pop-out / snap stretch, because client body copies stretch the host rope in one process) and `CableSettleTests` (host only). In one process all network managers share one physics world, so client copies of bodies
  disturb host physics: tests of physics settling must be host-only. Test players only supply poses (disable `PlayerController` and push colliders).

```powershell
$u = 'C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe'; $p = 'C:\Dev\power-cut-crew'
# compile check (grep log for "error CS" and "warning CS" from PowerCutCrew.*)
Start-Process $u -Wait -ArgumentList '-batchmode','-nographics','-quit','-projectPath',$p,'-logFile','Logs\compile.log'
# EditMode tests / PlayMode tests (PlayMode runs fine with -nographics too)
Start-Process $u -Wait -ArgumentList '-batchmode','-nographics','-projectPath',$p,'-runTests','-testPlatform','EditMode','-testFilter','PowerCutCrew','-testResults','Logs\editmode.xml','-logFile','Logs\editmode.log'
Start-Process $u -Wait -ArgumentList '-batchmode','-nographics','-projectPath',$p,'-runTests','-testPlatform','PlayMode','-testFilter','PowerCutCrew','-testResults','Logs\playmode.xml','-logFile','Logs\playmode.log'
# Windows development build -> Builds/Windows/PowerCutCrew.exe (builds Addressables content, copies steam_appid.txt)
Start-Process $u -Wait -ArgumentList '-batchmode','-nographics','-quit','-projectPath',$p,'-executeMethod','PowerCutCrew.Editor.BuildTools.WindowsDevelopment','-logFile','Logs\build.log'
# local multiplayer: host + 3 bot clients on one PC, bad network, 120 s (metrics in Builds\Windows\Logs\net-metrics-*.json)
$e = "$p\Builds\Windows\PowerCutCrew.exe"; $c = '-batchmode','-nographics','-bot','-netsim','bad','-duration','120'
Start-Process $e -ArgumentList ($c + '-host','-role','host','-seed','1')
1..3 | % { Start-Sleep 2; Start-Process $e -ArgumentList ($c + '-client','127.0.0.1','-role',"client$_",'-seed',"$_") }
# humans: run the exe without args, Play > Host (Local) on one PC, Play > Join (IP) on the other
```

## Structure

All our content is in `Assets/_Project/`; third-party assets live outside it. One asmdef per folder, root namespace =
assembly name. Every system references `PowerCutCrew.Core`; nothing references `UI` or `Net` unless it must; no cycles.

| Folder (`Scripts/`) | Assembly | Responsibility |
|---|---|---|
| Core | PowerCutCrew.Core | shared types, `TuningConfig`, `Interactable` + arbiter, generated `PlayerControls` |
| Generator | PowerCutCrew.Generator | `GeneratorModel` (pull cord, fuel burn, heat, overheat; plain C#), `PowerGrid`, `GeneratorNet`, panel, tank cap, pull cord |
| Cables | PowerCutCrew.Cables | host-only physics rope generator→socket, `CableModel` (grab / plug / snap / pop-out / respawn timers), `PlugRules`, sockets, reels, core-loop world + bots + verify hooks |
| Buildings | PowerCutCrew.Buildings | `BuildingModel` / `BuildingDirector` (plain C#: demand windows, subscription amps, spikes, happiness, Angry, payment), `BuildingNet` (NetworkVariables, marker, bubble; building i owns socket i) |
| Fuel | PowerCutCrew.Fuel | `FuelContainer` (plain C#), jerrycans, fuel drum; spills and fire later |
| Breakdowns | PowerCutCrew.Breakdowns | `BreakdownModel` / `BreakdownDirector` (plain C#): sputtering engine (wrench on the sweet spot), broken fan belt (spare belt from the crate), tripped breaker (hold the lever); `BreakdownNet`, wrench, crate, slot, lever, bot, verify. Only Shift, Editor and tests reference it |
| Shift | PowerCutCrew.Shift | `ShiftModel` (Lobby, Countdown, Running, Summary; plain C#), `ShiftNet` (host-owned money, HUD, summary, "Another shift" resets the street), clipboard, bots, `ShiftVerify`, `ShiftShots` |
| Economy | PowerCutCrew.Economy | upgrades between shifts |
| Net | PowerCutCrew.Net | sessions, Steam lobbies, players, grabbable items, `SnapshotStream`, bots, metrics |
| UI | PowerCutCrew.UI | menus, HUD, Bootstrap |
| Localization | PowerCutCrew.Localization | language switching, RTL helpers (`LocalizedRtlText`) |
| Editor | PowerCutCrew.Editor | editor-only: `ProjectSetup`, `ContentSetup`, `NetFoundationSetup`, `CoreLoopSetup`, `BuildTools` |

Other folders: `Config/` (tuning assets; `Config/Resources/` holds `NetTuning`, `PlayerTuning`, `GeneratorTuning`, `FuelTuning`, `CableTuning`, `BreakdownTuning`, `CoreLoopCatalog`, `BreakdownCatalog`), `Scenes/` (Bootstrap →
MainMenu → Street_Greybox), `Prefabs/Net/`, `Prefabs/CoreLoop/`, `Art/`, `Audio/`, `Settings/{Rendering,Input,Localization}`, `Tests/{EditMode,PlayMode}`.

**Rules:** game rules live in plain C# classes; MonoBehaviours and network behaviours stay thin; every tunable number
lives in a `TuningConfig` ScriptableObject, never hard-coded.

## Networking

Netcode for GameObjects **2.13.3** + Unity Transport 2.7.4 (`Local`, UDP) and the community SteamNetworkingSockets
transport (`Steam`), Steamworks.NET. Host-authoritative P2P over Steam lobbies. Build networking in from the start.
- **Authority:** the host owns the world: items, generator, cables, fuel, fires, buildings, money. Clients send input
  and interaction *requests* (RPCs); the host validates (range, availability, arbitration) and executes.
- **Players:** own movement is owner-authoritative (owner `NetworkTransform`, interpolated for others). No client prediction.
- **Interaction:** anything interactive is an `Interactable` (Core). The owner raycasts, the HUD shows the localized
  prompt, the request RPC carries a `NetworkObjectReference`, the host calls `HostTryInteract`. Exclusive things use `InteractionArbiter`.
- **Held items:** the host owns the real item (kinematic follow of the holder's hold point). The **holder sees a local
  copy** attached to the hand (never trails the camera) and the networked item is hidden for them; on release the copy
  blends back (`NetTuning.HeldBlendSeconds`). Everyone else sees the networked item.
- **Release:** drop / throw / plug-in requests carry the holder's hand pose (+ velocity); the host trusts the pose within `NetTuning.ReleasePoseTolerance`
  (2.5 m, `ReleasePoseRule`) and teleports the item to it. The holder's copy keeps simulating as a ghost until the networked item catches up.
- **Hold interactions** (pour, fill the can): `Interactable.IsHold`; the owner sends begin / keep-alive (0.2 s) / end, the host's `HoldRegistry` ticks them.
- **Core loop:** generator (`GeneratorModel`, rated 10 A), fuel (`FuelContainer`), cables (`CableModel`, custom host-only rope), sockets (`PowerGrid`). Plain classes
  hold the rules and are unit-tested; `GeneratorNet`, `CableNet`, `PowerSocket`, `FuelCan` are thin NetworkBehaviours. Details and bandwidth: `docs/NETWORKING.md`.
- **Cable drawing:** one tube mesh per cable (`CableSpline` + `CableTubeMesh`, Catmull-Rom through the rope points, rebuilt every frame on every machine); the box segments are only colliders on the host.
  The Prompt 05 spline uses the same renderer. Rope numbers (damping, solver, friction, slack, settle limits) are in `CableTuning`; `docs/NETWORKING.md` explains why.
- **Many-body physics** (cable, chains) syncs through `SnapshotStream`, **not** per-object NetworkTransforms.
- Connection approval is `ConnectionRules` (full, version mismatch). Steam is only initialised when Steam mode is used.
- Launch args: `-host`, `-client <ip>`, `-steamhost`, `-steamclient <lobbyId>`, `-bot`, `-duration <s>`, `-netsim none|bad|awful`,
  `-seed <n>`, `-role <name>`, `-verify` (late-join assertion: core loop, shift state / time / money, every building's window, powered, happiness, Angry, bubble), `-autostart` (a bot starts the shift at the clipboard), `-yank` (the host bot fakes a pull on a plugged cable), `-cabletrace` (stretch after each plug-in), `-shifts <n>`, `-shiftseconds <s>`, `-breakdowns off` (no breakdowns), `-breakdown sputter|belt|breaker -breakdownat <s>` (force one, again `s` s after each fix; the dice are off), `-holdforever`, `-holdplug`, `-snapbias`, `-botlog`,
  `-shotdir <dir> -shot playpanel|prompt|coreloop|shift|breakdowns` (doc screenshots). F1 = debug overlay (dev builds).
- `steam_appid.txt` (480) is copied for development builds only; release builds must not ship it.

## Arabic / localization

- All player-facing text goes through Localization (`UI` string table collection, locales `en` default, `ar`).
- Arabic text uses RTLTMPro (`RTLTextMeshPro` + `LocalizedRtlText`). Test RTL on every UI screen.
- MSA for UI; Lebanese dialect is allowed in neighbor lines.
- Fonts: Bowlby One (EN display), Lalezar (AR display), Noto Sans (EN body), Noto Sans Arabic (AR body). Dynamic TMP atlases.

## Design rules

Failure is funny, not frustrating. Every mechanic should be clip-worthy. Readable in 3 seconds.
Brand colors: background `#0F1630`, title `#FFC93C`, button text `#FFF4DC`.

## Git

`main`, Git LFS for binaries (see `.gitattributes`). Never commit Library, Builds, recordings, raw footage or asset-pack
archives. Keep the repo lean to stay inside the free GitHub LFS quota (10 GiB storage + 10 GiB/month bandwidth).

## Verification checklist (every change)

1. Compile with zero errors.
2. EditMode and PlayMode tests green (`-testFilter PowerCutCrew`).
3. No new warnings from our assemblies (`PowerCutCrew.*`).
4. UI changes: check English and Arabic. Network changes: a bot run (host + 3 clients, `bad`) with zero errors.
5. Zero-cost rule: no paid packages, no Unity Gaming Services/Cloud/Plastic; record new deps in `docs/THIRD_PARTY.md`.
