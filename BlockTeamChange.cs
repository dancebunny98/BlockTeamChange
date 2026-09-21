using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace BlockTeamChange;

/// <summary>
/// 1) Блокирует смену команды/переход в наблюдатели во время freeze time.
/// 2) Фиксит "фантомную модель": пока клиент не подключился полностью
///    (Connected != PlayerConnectedState.PlayerConnected), его pawn уже
///    заспавнен на карте и его можно убить, хотя игрок ещё не загрузился.
///    На это время pawn становится неуязвимым и не блокирующим.
/// </summary>
public class BlockTeamChangePlugin : BasePlugin
{
    public override string ModuleName => "Block Team Change + Phantom Fix";
    public override string ModuleVersion => "1.3.0";
    public override string ModuleAuthor => "Assistant";
    public override string ModuleDescription => "Blocks team changes during freeze time and fixes the phantom killable player model on connect";

    // Флаг, указывающий, идёт ли сейчас период заморозки
    private bool _isFreezePeriod = false;

    public override void Load(bool hotReload)
    {
        // --- Блокировка смены команды во время заморозки ---
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundFreezeEnd>(OnRoundFreezeEnd);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);

        AddCommandListener("jointeam", OnTeamChange, HookMode.Pre);
        AddCommandListener("spectate", OnTeamChange, HookMode.Pre);

        // --- Фикс фантомной модели ---
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterListener<Listeners.OnTick>(OnTick);
    }

    // ===================== Блокировка смены команды =====================

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _isFreezePeriod = true;
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
        return HookResult.Continue;
    }

    private HookResult OnTeamChange(CCSPlayerController? player, CommandInfo commandInfo)
    {
        // Игнорируем ботов, HLTV и невалидных игроков
        if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
            return HookResult.Continue;

        // Блокируем смену команды только во время периода заморозки
        if (_isFreezePeriod)
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

        if (player.Connected != PlayerConnectedState.PlayerConnected)
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

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid)
                continue;

            bool fullyConnected = player.Connected == PlayerConnectedState.PlayerConnected;

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
