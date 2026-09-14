using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Generic;

namespace PhantomFix;

public class PhantomFixPlugin : BasePlugin
{
    public override string ModuleName => "Phantom Fix";
    public override string ModuleVersion => "1.2.0";
    public override string ModuleAuthor => "Assistant";
    public override string ModuleDescription => "Kill player on team change and verify they are really dead";

    // Храним последнюю известную команду каждого игрока (по слоту)
    private readonly Dictionary<int, CsTeam> _lastTeam = new();

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
        RegisterListener<Listeners.OnTick>(OnTick);
    }

    private void OnClientPutInServer(int playerSlot)
    {
        var player = Utilities.GetPlayerFromSlot(playerSlot);
        if (player == null || !player.IsValid) return;

        _lastTeam[playerSlot] = player.Team;
    }

    private void OnTick()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid) continue;

            var slot = player.Slot;
            var currentTeam = player.Team;

            // Если команда не изменилась — пропускаем
            if (!_lastTeam.TryGetValue(slot, out var lastTeam) || lastTeam == currentTeam)
                continue;

            _lastTeam[slot] = currentTeam;

            // Наблюдателей и "None" не трогаем
            if (currentTeam == CsTeam.Spectator || currentTeam == CsTeam.None)
                continue;

            // Небольшая задержка, чтобы движок успел применить смену команды
            AddTimer(0.1f, () => KillAndVerify(player));
        }
    }

    /// <summary>
    /// Убивает игрока и проверяет, что он действительно мёртв (а не "призрак").
    /// Никакого респавна — игрок остаётся мёртвым.
    /// </summary>
    private void KillAndVerify(CCSPlayerController player)
    {
        if (!player.IsValid) return;

        // Первое убийство
        player.CommitSuicide(true, false);

        // Через мгновение проверяем состояние
        AddTimer(0.2f, () =>
        {
            if (!player.IsValid) return;

            if (IsPhantom(player))
            {
                Server.PrintToConsole($"[PhantomFix] Phantom detected on '{player.PlayerName}', forcing kill");

                // Повторное принудительное убийство
                player.CommitSuicide(true, false);

                // Если и это не помогло — шлём консольную команду kill от имени клиента
                AddTimer(0.2f, () =>
                {
                    if (!player.IsValid) return;

                    if (IsPhantom(player))
                    {
                        Server.PrintToConsole($"[PhantomFix] Phantom persists on '{player.PlayerName}', sending 'kill' command");
                        player.ExecuteClientCommand("kill");
                    }
                });
            }
        });
    }

    /// <summary>
    /// Проверка на "призрачное" состояние.
    /// Возвращает true, если игрок "жив" по контроллеру, но его пешка невалидна
    /// или не соответствует мёртвому состоянию после убийства.
    /// </summary>
    private bool IsPhantom(CCSPlayerController player)
    {
        // Если игрок уже мёртв — всё в порядке
        if (!player.PawnIsAlive)
            return false;

        var pawn = player.PlayerPawn.Value;

        // Пешка битая/невалидная, а контроллер считает игрока живым — это призрак
        if (pawn == null || !pawn.IsValid)
            return true;

        // Health > 0 — смерть не применилась
        return pawn.Health > 0;
    }
}