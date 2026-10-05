using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace BlockTeamChange;

/// <summary>
/// Keeps team selection closed during the pre-round countdown and prevents a
/// player who joins a team after the round has started from getting an extra
/// life. Waiting players are released by the next round's normal spawn.
/// </summary>
public sealed class BlockTeamChangePlugin : BasePlugin
{
    public override string ModuleName => "BlockTeamChange";
    public override string ModuleVersion => "2.0.0";
    public override string ModuleAuthor => "Assistant";
    public override string ModuleDescription => "Controls team selection and delayed round spawns";

    private readonly HashSet<int> _waitingForRound = new();
    private bool _teamSelectionLocked = true;
    private bool _roundPlayable;

    public override void Load(bool hotReload)
    {
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundFreezeEnd>(OnRoundFreezeEnd);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam, HookMode.Post);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn, HookMode.Post);
        RegisterListener<Listeners.OnClientConnected>(OnClientConnected);
        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        AddCommandListener("jointeam", OnJoinTeam, HookMode.Pre);
        AddCommandListener("spectate", OnSpectate, HookMode.Pre);

        if (hotReload)
            Server.NextFrame(MarkConnectedPlayersWaiting);
    }

    private HookResult OnRoundStart(EventRoundStart _, GameEventInfo __)
    {
        _waitingForRound.Clear();
        _teamSelectionLocked = true;
        _roundPlayable = false;
        return HookResult.Continue;
    }

    private HookResult OnRoundFreezeEnd(EventRoundFreezeEnd _, GameEventInfo __)
    {
        _teamSelectionLocked = false;
        _roundPlayable = true;
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd _, GameEventInfo __)
    {
        _teamSelectionLocked = true;
        _roundPlayable = false;
        return HookResult.Continue;
    }

    private HookResult OnJoinTeam(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsHuman(player))
            return HookResult.Continue;

        var requested = command.ArgCount > 1 ? command.GetArg(1) : string.Empty;
        var spectatorRequest = requested is "1" or "spec" or "spectator";

        if (_teamSelectionLocked && !spectatorRequest)
        {
            player!.PrintToChat(" \x04[Сервер] \x01Выбор команды доступен после начала раунда.");
            return HookResult.Handled;
        }

        if (_roundPlayable && !spectatorRequest)
            WaitForNextRound(player!);

        return HookResult.Continue;
    }

    private HookResult OnSpectate(CCSPlayerController? player, CommandInfo _)
    {
        if (!IsHuman(player))
            return HookResult.Continue;

        if (_roundPlayable)
            _waitingForRound.Remove(player!.Slot);

        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (!IsHuman(player))
            return HookResult.Continue;

        if (_roundPlayable && player!.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
            WaitForNextRound(player);

        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (!IsHuman(player) || !_waitingForRound.Contains(player!.Slot))
            return HookResult.Continue;

        Server.NextFrame(() => KillWaitingPlayer(player.Slot));
        return HookResult.Continue;
    }

    private void OnClientConnected(int slot) => _waitingForRound.Add(slot);

    private void OnClientPutInServer(int slot)
    {
        _waitingForRound.Add(slot);
        Server.NextFrame(() => KillWaitingPlayer(slot));
    }

    private void OnClientDisconnect(int slot) => _waitingForRound.Remove(slot);

    private void MarkConnectedPlayersWaiting()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (IsHuman(player))
                _waitingForRound.Add(player.Slot);
        }
    }

    private void WaitForNextRound(CCSPlayerController player)
    {
        _waitingForRound.Add(player.Slot);
        Server.NextFrame(() => KillWaitingPlayer(player.Slot));
    }

    private void KillWaitingPlayer(int slot)
    {
        if (!_waitingForRound.Contains(slot))
            return;

        var player = Utilities.GetPlayerFromSlot(slot);
        if (!IsHuman(player) || !player!.PawnIsAlive)
            return;

        player.PlayerPawn.Value?.CommitSuicide(false, false);
    }

    private static bool IsHuman(CCSPlayerController? player) =>
        player is { IsValid: true, IsBot: false, IsHLTV: false };
}
