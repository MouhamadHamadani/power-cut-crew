# Power Cut Crew (شباب الموتور)

1–4 player online co-op physics game about running a neighborhood electricity generator during blackouts. Players
fuel the generator, run physics-rope cables to buildings, keep it from overheating and fix funny breakdowns while
neighbors complain. Free tools only (Unity Personal, OSS packages, OFL fonts); see `docs/THIRD_PARTY.md`.
Design knowledge base: `docs/DESIGN.md`.

## Unity

- Version: **6000.3.26f1** (Unity 6.3 LTS), URP 3D, Windows x64, Mono, .NET Standard 2.1.
- Editor: `C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe`
- A Unity process locks the project. Batchmode needs the Editor closed; with an Editor open, drive it via
  `unity command ...` (com.unity.pipeline) instead.
- Run Unity from PowerShell with `Start-Process -Wait -PassThru` (a bare `&` returns before Unity finishes).

```powershell
$u = 'C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe'; $p = 'C:\Dev\power-cut-crew'
# compile check (grep log for "error CS" and "warning CS" from PowerCutCrew.*)
Start-Process $u -Wait -ArgumentList '-batchmode','-nographics','-quit','-projectPath',$p,'-logFile','Logs\compile.log'
# EditMode tests / PlayMode tests (PlayMode needs a graphics device: omit -nographics)
Start-Process $u -Wait -ArgumentList '-batchmode','-nographics','-projectPath',$p,'-runTests','-testPlatform','EditMode','-testResults','Logs\editmode.xml','-logFile','Logs\editmode.log'
Start-Process $u -Wait -ArgumentList '-batchmode','-projectPath',$p,'-runTests','-testPlatform','PlayMode','-testResults','Logs\playmode.xml','-logFile','Logs\playmode.log'
# Windows development build -> Builds/Windows/PowerCutCrew.exe (builds Addressables content first)
Start-Process $u -Wait -ArgumentList '-batchmode','-nographics','-quit','-projectPath',$p,'-executeMethod','PowerCutCrew.Editor.BuildTools.WindowsDevelopment','-logFile','Logs\build.log'
```

## Structure

All our content is in `Assets/_Project/`; third-party assets live outside it. One asmdef per folder, root namespace =
assembly name. Every system references `PowerCutCrew.Core`; nothing references `UI` or `Net` unless it must; no cycles.

| Folder (`Scripts/`) | Assembly | Responsibility |
|---|---|---|
| Core | PowerCutCrew.Core | shared types, bootstrap, utilities, `TuningConfig`, generated `PlayerControls` |
| Generator | PowerCutCrew.Generator | fuel, load (amps), heat, overload, breakdown states |
| Cables | PowerCutCrew.Cables | physics rope generator→building, attach/detach/snap |
| Buildings | PowerCutCrew.Buildings | demand, subscription tier, happiness, payment |
| Fuel | PowerCutCrew.Fuel | jerrycans, refill point, spills, fire |
| Breakdowns | PowerCutCrew.Breakdowns | typed failures with physical fixes |
| Shift | PowerCutCrew.Shift | timer and score |
| Economy | PowerCutCrew.Economy | upgrades between shifts |
| Net | PowerCutCrew.Net | lobbies, sessions, sync |
| UI | PowerCutCrew.UI | menus, HUD, Bootstrap |
| Localization | PowerCutCrew.Localization | language switching, RTL helpers (`LocalizedRtlText`) |
| Editor | PowerCutCrew.Editor | editor-only: `ProjectSetup`, `ContentSetup`, `BuildTools` |

Other folders: `Config/` (ScriptableObject tuning), `Scenes/` (Bootstrap → MainMenu → Street_Greybox), `Prefabs/`,
`Art/`, `Audio/`, `Settings/{Rendering,Input,Localization}`, `Tests/{EditMode,PlayMode}`.

**Rules:** game rules live in plain C# classes; MonoBehaviours and network behaviours stay thin; every tunable number
lives in a `TuningConfig` ScriptableObject, never hard-coded.

## Networking (later prompts; decide up front, never retrofit)

Host-authoritative P2P over Steam lobbies. The host owns generator, cables, fuel, fires, buildings and money. Clients
send input and interaction requests only. Own movement is client-predicted. Everything physical is simulated on the
host and synced. Networking is built in from the first prototype.

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
2. EditMode and PlayMode tests green.
3. No new warnings from our assemblies (`PowerCutCrew.*`).
4. UI changes: check English and Arabic.
5. Zero-cost rule: no paid packages, no Unity Gaming Services/Cloud/Plastic; record new deps in `docs/THIRD_PARTY.md`.
