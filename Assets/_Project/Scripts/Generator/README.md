# PowerCutCrew.Generator

Fuel, load (amps), heat, overload and the breakdown hooks of the generator.

- `GeneratorModel` (plain C#): pull-cord odds, fuel burn, load, heat, overheat. Breakdown hooks (Prompt 07): `OutputDip` (the engine coughs: nothing delivered or drawn, `OutputLive` is false), `CoolingBroken` + `BrokenCoolingExtraHeat` (no cooling, extra heat while running). Both are off by default, so nothing changes without the breakdowns.
- `PowerGrid` (plain C#): which outlet feeds which socket and the draw of each. A tripped outlet (`SetTripped`) delivers no power: its socket is unfed and its draw is not part of `Load`; `LoadIfNothingTripped` is what the breaker reset compares with the rating. `IsConnected` says a cable runs to a socket whether or not the outlet is live.
- `GeneratorNet` (thin): runs the model, publishes state / fuel / heat / load. `Feeds(socket)` is the one question the buildings and the sockets ask ("does this socket have power right now": a cable from a live outlet and the output alive); `HeatAlarm` makes the panel's heat bar pulse.
