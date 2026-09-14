using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace BlockTeamChange;

public class BlockTeamChangePlugin : BasePlugin
{
    public override string ModuleName => "Block Team Change During Freeze Time";
    public override string ModuleVersion => "1.2.0";
    public override string ModuleAuthor => "Assistant";
    public override string ModuleDescription => "Blocks all team changes during freeze period via game events";

    // Флаг, указывающий, идёт ли сейчас период заморозки
    private bool _isFreezePeriod = false;

    public override void Load(bool hotReload)
    {
        // Отслеживаем начало раунда и конец заморозки
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundFreezeEnd>(OnRoundFreezeEnd);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);

        // Перехватываем команды смены команды и перехода в наблюдатели
        AddCommandListener("jointeam", OnTeamChange, HookMode.Pre);
        AddCommandListener("spectate", OnTeamChange, HookMode.Pre);
    }

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
}