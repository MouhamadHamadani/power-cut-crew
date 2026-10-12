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
- NGO's own tests are enabled (`testables`) for its integration helpers, so always pass `-testFilter PowerCutCrew`.

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
| Generator | PowerCutCrew.Generator | fuel, load (amps), heat, overload, breakdown states |
| Cables | PowerCutCrew.Cables | physics rope generator→building, attach/detach/snap |
| Buildings | PowerCutCrew.Buildings | demand, subscription tier, happiness, payment |
| Fuel | PowerCutCrew.Fuel | jerrycans, refill point, spills, fire |
| Breakdowns | PowerCutCrew.Breakdowns | typed failures with physical fixes |
| Shift | PowerCutCrew.Shift | timer and score |
| Economy | PowerCutCrew.Economy | upgrades between shifts |
| Net | PowerCutCrew.Net | sessions, Steam lobbies, players, grabbable items, `SnapshotStream`, bots, metrics |
| UI | PowerCutCrew.UI | menus, HUD, Bootstrap |
| Localization | PowerCutCrew.Localization | language switching, RTL helpers (`LocalizedRtlText`) |
| Editor | PowerCutCrew.Editor | editor-only: `ProjectSetup`, `ContentSetup`, `NetFoundationSetup`, `BuildTools` |

Other folders: `Config/` (tuning assets; `Config/Resources/` holds `NetTuning`, `PlayerTuning`), `Scenes/` (Bootstrap →
MainMenu → Street_Greybox), `Prefabs/Net/`, `Art/`, `Audio/`, `Settings/{Rendering,Input,Localization}`, `Tests/{EditMode,PlayMode}`.

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
- **Many-body physics** (cable, chains) syncs through `SnapshotStream`, **not** per-object NetworkTransforms.
- Connection approval is `ConnectionRules` (full, version mismatch). Steam is only initialised when Steam mode is used.
- Launch args: `-host`, `-client <ip>`, `-steamhost`, `-steamclient <lobbyId>`, `-bot`, `-duration <s>`, `-netsim none|bad|awful`,
  `-seed <n>`, `-role <name>`, `-verify` (late-join assertion), `-holdforever`, `-botlog`. F1 = debug overlay (dev builds).
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
