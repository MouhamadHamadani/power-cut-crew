# PowerCutCrew.Net

Sessions, Steam lobbies, networked players, grabbable physics items, the snapshot stream for many-body physics, bots and run metrics.
Lobbies, sessions and state sync. See `docs/NETWORKING.md`.

- `NetSession`: start host / client over Local (UDP) or Steam, approval, scene flow, spawning, disconnects.
- `Players/`: `NetPlayer` (slot, name, requests to the host), `PlayerController` (owner movement and interaction raycast), input sources.
- `Items/`: `NetworkPhysicsItem` (host simulated, holder sees a local copy in the hand).
- `Snapshots/`: `SnapshotCodec`, `SnapshotInterpolator`, `SnapshotStream` (use this for cables and chains).
- `Bots/`, `Verification/`, `NetMetrics`: bot runs and the late-join assertion.
