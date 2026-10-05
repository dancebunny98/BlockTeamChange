using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;

namespace BlockTeamChange;

/// <summary>
/// 1) Блокирует смену команды/переход в наблюдатели во время freeze time.
/// 2) Фиксит "фантомную модель": пока клиент не подключился полностью
///    (Connected != PlayerConnectedState.Connected), его pawn уже
///    заспавнен на карте и его можно убить, хотя игрок ещё не загрузился.
///    На это время pawn становится неуязвимым и не блокирующим.
/// </summary>
public class BlockTeamChangePlugin : BasePlugin
{
    public override string ModuleName => "Block Team Change + Phantom Fix";
    public override string ModuleVersion => "1.7.0";
    public override string ModuleAuthor => "Assistant";
    public override string ModuleDescription => "Blocks team changes during freeze time and fixes the phantom killable player model on connect";

    // Флаг, указывающий, идёт ли сейчас период заморозки
    private bool _isFreezePeriod = false;
    private bool _hasRoundStarted = false;
    private readonly HashSet<int> _waitForNextRound = new();

    public override void Load(bool hotReload)
    {
        // --- Блокировка смены команды во время заморозки ---
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundFreezeEnd>(OnRoundFreezeEnd);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam, HookMode.Post);
        RegisterListener<Listeners.OnClientConnected>(OnClientConnected);
        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);

        AddCommandListener("jointeam", OnTeamChange, HookMode.Pre);
        AddCommandListener("spectate", OnTeamChange, HookMode.Pre);

        // --- Фикс фантомной модели ---
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterListener<Listeners.OnTick>(OnTick);
    }

    // ===================== Блокировка смены команды =====================

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _hasRoundStarted = true;
        _isFreezePeriod = true;
        _waitForNextRound.Clear();
        return HookResult.Continue;
    }

    private HookResult OnRoundFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        _isFreezePeriod = false;
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        // На случай, если раунд закончился до окончания заморозки
        _isFreezePeriod = false;
        _hasRoundStarted = false;
        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
            return HookResult.Continue;

        // A player who joins T/CT from spectator must wait for the next round.
        // CS2 may create a pawn immediately after player_team, so remember the
        // slot and enforce the dead state from the next frame and on every tick.
        if (player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
        {
            _waitForNextRound.Add(player.Slot);
            Server.NextFrame(() => KeepDeadUntilNextRound(player));
        }

        return HookResult.Continue;
    }

    private void OnClientConnected(int slot)
    {
        MarkWaitingForNextRound(slot);
    }

    private void OnClientPutInServer(int slot)
    {
        MarkWaitingForNextRound(slot);

        Server.NextFrame(() =>
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player != null)
                KeepDeadUntilNextRound(player);
        });
    }

    private void MarkWaitingForNextRound(int slot)
    {
        // A reconnect can restore the player's team without emitting a new
        // player_team event. Mark the slot before the first spawn event.
        _waitForNextRound.Add(slot);
    }

    private void OnClientDisconnect(int slot)
    {
        _waitForNextRound.Remove(slot);
    }

    private void KeepDeadUntilNextRound(CCSPlayerController player)
    {
        if (!player.IsValid || !_waitForNextRound.Contains(player.Slot))
            return;

        KillIfAlive(player);
    }

    private HookResult OnTeamChange(CCSPlayerController? player, CommandInfo commandInfo)
    {
        // Игнорируем ботов, HLTV и невалидных игроков
        if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
            return HookResult.Continue;

        // Блокируем смену команды только во время периода заморозки
        var isSpectator = player.Team is CsTeam.Spectator or CsTeam.None;
        if (_hasRoundStarted && _isFreezePeriod && !isSpectator)
        {
            player.PrintToChat(" \x04[Сервер] \x01Смена команды доступна только во время раунда.");
            return HookResult.Handled;
        }

        return HookResult.Continue;
    }

    // ===================== Фикс фантомной модели =====================

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot)
            return HookResult.Continue;

        if (_waitForNextRound.Contains(player.Slot))
        {
            // Let CS2 finish creating the pawn, then immediately use the
            // normal death path so controller and scoreboard stay consistent.
            KillIfAlive(player);
            return HookResult.Continue;
        }

        if (player.Connected != PlayerConnectedState.Connected)
            ApplyProtection(player);

        return HookResult.Continue;
    }

    // Каждый тик проверяем всех игроков: как только состояние подключения
    // меняется, включаем или снимаем защиту.
    private void OnTick()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.IsBot)
                continue;

            if (_waitForNextRound.Contains(player.Slot))
            {
                KillIfAlive(player);
                continue;
            }

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid)
                continue;

            bool fullyConnected = player.Connected == PlayerConnectedState.Connected;

            if (!fullyConnected && pawn.TakesDamage)
            {
                ApplyProtection(player);
            }
            else if (fullyConnected && !pawn.TakesDamage)
            {
                RemoveProtection(player);
            }
        }
    }

    private static void KillIfAlive(CCSPlayerController player)
    {
        if (player.PawnIsAlive)
            player.PlayerPawn.Value?.CommitSuicide(false, false);
    }

    private void ApplyProtection(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;

        pawn.TakesDamage = false;
        pawn.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_CollisionGroup");
    }

    private void RemoveProtection(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;

        pawn.TakesDamage = true;
        pawn.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_CollisionGroup");
    }
}
