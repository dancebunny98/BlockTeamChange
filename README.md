# BlockTeamChange

CounterStrikeSharp plugin for CS2. Requires .NET 10 and CounterStrikeSharp.API 1.0.376.

## Behavior

`mp_join_grace_time` is set to zero while the plugin runs. This asks the game to
keep players who connect during a live round on their chosen team as observers
until the next normal round spawn. The plugin never kills a player to enforce
the wait. The previous convar value is restored on unload if it is still zero.

Team selection is locked during freeze time and between `round_end` and the next
`round_start`. The latter lock also covers `spectate` and spectator selection.
Warmup is exempt from both locks. Map changes reset the lock state.

## Configuration

CounterStrikeSharp creates `configs/plugins/BlockTeamChange/BlockTeamChange.json`.

| Setting | Default | Effect |
| --- | --- | --- |
| `Language` | `ru` | `en` or `ru`; messages are in `lang/`. |
| `LockDuringFreezeTime` | `true` | Blocks team selection during freeze time. |
| `LockAfterRoundEnd` | `true` | Blocks all team changes after `round_end`. |
| `PreventLateJoinSpawn` | `true` | Sets `mp_join_grace_time` to zero. |

The convar is shared with the server and other plugins. A server config that
changes it after this plugin loads can override the late-join behavior.

## Build and release

```powershell
dotnet build BlockTeamChange.csproj -c Release
```

Install `BlockTeamChange.dll` with the `lang` directory from
`bin/Release/net10.0/` in the same plugin folder. Do not copy the API DLL.
