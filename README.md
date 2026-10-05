# BlockTeamChange

CounterStrikeSharp plugin for CS2. The assembly and plugin name remain
`BlockTeamChange`.

## Behaviour

- Team selection is locked during the pre-round countdown and freeze time.
- Spectator mode remains available while team selection is locked.
- A player who connects or reconnects to a playable team waits dead until the
  next round and can spectate living teammates from that team.
- A spectator who joins T/CT during an active round also waits until the next
  round.
- The next round uses the engine's normal spawn path.
- No per-tick scan or timer is used; work is done only on relevant game and
  connection events.

## Build

Requirements: .NET 10 and CounterStrikeSharp.API 1.0.376.

```powershell
dotnet build BlockTeamChange.csproj -c Release
```

Copy `bin/Release/net10.0/BlockTeamChange.dll` to the CounterStrikeSharp
plugins directory.
