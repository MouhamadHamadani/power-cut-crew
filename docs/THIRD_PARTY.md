# Third-party dependencies

Everything here is free. Unity registry packages are used under Unity Personal and the Unity Companion License (UCL);
all of them ship with a LICENSE file inside the package. No paid assets, no trials, no Unity Gaming Services.

## Editor

| Name | Version | License | Source |
|---|---|---|---|
| Unity Editor | 6000.3.26f1 (Unity 6.3 LTS) | Unity Personal | https://unity.com/products/unity-personal |

## Packages (Packages/manifest.json)

| Name | Version | License | Source |
|---|---|---|---|
| com.unity.render-pipelines.universal (URP) | 17.3.0 | Unity Companion License | Unity registry |
| com.unity.inputsystem | 1.14.2 | Unity Companion License | Unity registry |
| com.unity.ugui (uGUI + TextMeshPro) | 2.0.0 | Unity Companion License | Unity registry |
| com.unity.localization | 1.5.7 | Unity Companion License | Unity registry |
| com.unity.cinemachine | 3.1.5 | Unity Companion License | Unity registry |
| com.unity.test-framework | 1.6.0 | Unity Companion License | Unity registry |
| com.unity.pipeline (Editor bridge) | 0.7.0-exp.1 | Unity Companion License | Unity registry |
| com.unity.ide.visualstudio | 2.0.23 | Unity Companion License | Unity registry |
| com.nosuchstudio.rtltmpro (RTLTMPro) | 4.0.0 | MIT | https://github.com/hk1ll3r/RTLTMPro (UPM fork of https://github.com/pnarimani/RTLTMPro) |

Transitive (pulled in automatically, all Unity registry, UCL): addressables 2.10.3 (required by Localization), burst 1.8.30,
collections 2.6.8, mathematics 1.3.3, splines 2.9.1 (Cinemachine), shadergraph 17.3.0, scriptablebuildpipeline 2.6.2,
test-framework.performance 3.5.0, newtonsoft-json 3.2.2, and a few more listed in `Packages/packages-lock.json`.

## Fonts (Assets/_Project/Art/Fonts)

All SIL Open Font License 1.1. Source: https://github.com/google/fonts (license texts stored next to the fonts as `OFL-*.txt`).

| Font | Use | Source path |
|---|---|---|
| Bowlby One | EN display | `ofl/bowlbyone` |
| Lalezar | AR display | `ofl/lalezar` |
| Noto Sans (variable) | EN body | `ofl/notosans` |
| Noto Sans Arabic (variable) | AR body | `ofl/notosansarabic` |

## Networking (Prompt 03)

| Name | Version | License | Source |
|---|---|---|---|
| com.unity.netcode.gameobjects (NGO) | 2.13.3 | Unity Companion License | Unity registry |
| com.unity.transport (Unity Transport) | 2.7.4 | Unity Companion License | Unity registry |
| com.unity.multiplayer.tools (Network Simulator, profiler) | 2.2.12 | Unity Companion License | Unity registry |
| com.unity.multiplayer.playmode (Multiplayer Play Mode) | 2.0.2 | Unity Companion License | Unity registry (installs and compiles on 6.3; editor-only, not exercised in batchmode) |
| Steamworks.NET (`com.rlabrecque.steamworks.net`) | 2025.165.0, commit `ba71581f1ed7349e8d0f17ddc6f135dd3bc8a6a3` | MIT | https://github.com/rlabrecque/Steamworks.NET |
| SteamNetworkingSockets transport for NGO (`com.community.netcode.transport.steamnetworkingsockets`) | 1.0.1, commit `0fab638470379ace12b0149dfb41c043d23dbce5` | MIT | https://github.com/Unity-Technologies/multiplayer-community-contributions (Transports/com.community.netcode.transport.steamnetworkingsockets) |

`Packages/manifest.json` also lists `testables: com.unity.netcode.gameobjects` so NGO's integration test helpers can be used by our PlayMode test.
The Steamworks.NET package bundles Valve's `steam_api64.dll`; Steam test App ID 480 (Spacewar) is used for development only (`steam_appid.txt`, development builds only).
Shipping on Steam needs a real App ID (one-time Steam Direct fee per game), which is outside the zero-cost rule and is not part of this prompt.

Prompt 04 (core loop: generator, fuel, cables) added **no packages**. The cable is our own ConfigurableJoint chain (no rope asset), all meshes are grey-box primitives, no new fonts.
