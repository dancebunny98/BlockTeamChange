using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.Linq;

namespace BlockTeamChange;

public class BlockTeamChangePlugin : BasePlugin
{
    public override string ModuleName => "Block Team Change During Freeze Time";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "Assistant";
    public override string ModuleDescription => "Blocks all team changes during freeze period, allows them during active round";

    public override void Load(bool hotReload)
    {
        // Перехватываем все способы смены команды
        AddCommandListener("jointeam", OnTeamChange, HookMode.Pre);
        AddCommandListener("spectate", OnTeamChange, HookMode.Pre);
    }

    private HookResult OnTeamChange(CCSPlayerController? player, CommandInfo commandInfo)
    {
        // Реагируем только на реальных игроков
        if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
            return HookResult.Continue;

        var gameRules = GetGameRules();
        if (gameRules == null)
            return HookResult.Continue;

        // Если идёт заморозка (отсчёт до начала раунда) — блокируем ЛЮБУЮ смену команды
        if (gameRules.m_bFreezePeriod)
        {
            player.PrintToChat(" \x04[Сервер] \x01Смена команды доступна только во время раунда.");
            return HookResult.Handled; // отменяем команду
        }

        // Идёт активный раунд — разрешаем
        return HookResult.Continue;
    }

    private CCSGameRules? GetGameRules()
    {
        var proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        return proxy?.GameRules;
    }
}