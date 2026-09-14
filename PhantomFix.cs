using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Generic;

namespace PhantomFix;

public class PhantomFixPlugin : BasePlugin
{
    public override string ModuleName => "Phantom Fix";
    public override string ModuleVersion => "1.3.0";
    public override string ModuleAuthor => "Assistant";
    public override string ModuleDescription => "Cascade-kill player on team change to prevent phantom state";

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

            if (!_lastTeam.TryGetValue(slot, out var lastTeam) || lastTeam == currentTeam)
                continue;

            _lastTeam[slot] = currentTeam;

            // Смена команды = смерть. Наблюдателей и "None" не трогаем.
            if (currentTeam == CsTeam.Spectator || currentTeam == CsTeam.None)
                continue;

            // Запускаем каскад убийства с небольшой задержкой
            AddTimer(0.2f, () => StartKillCascade(player, 0));
        }
    }

    /// <summary>
    /// Каскадное убийство. Шаг 0: CommitSuicide, Шаг 1: AddEntityIOEvent("Kill"), Шаг 2: ExecuteClientCommand("kill").
    /// </summary>
    private void StartKillCascade(CCSPlayerController player, int step)
    {
        if (!player.IsValid) return;

        // Если игрок уже мертв, прекращаем
        if (!player.PawnIsAlive)
        {
            Server.PrintToConsole($"[PhantomFix] '{player.PlayerName}' is already dead after step {step}.");
            return;
        }

        // Если достигли последнего шага и все еще живы - выходим (значит что-то совсем пошло не так)
        if (step > 2)
        {
            Server.PrintToConsole($"[PhantomFix] CRITICAL: '{player.PlayerName}' is STILL alive after all kill attempts!");
            return;
        }

        Server.PrintToConsole($"[PhantomFix] Kill cascade step {step} for '{player.PlayerName}'.");

        switch (step)
        {
            case 0:
                player.CommitSuicide(true, false);
                break;
            case 1:
                // Прямая команда сущности на убийство
                var pawn = player.PlayerPawn.Value;
                if (pawn != null && pawn.IsValid)
                {
                    pawn.AddEntityIOEvent("Kill", pawn, delay: 0.1f);
                }
                break;
            case 2:
                // Консольная команда от имени игрока (самый жесткий вариант)
                player.ExecuteClientCommand("kill");
                break;
        }

        // Проверяем результат через 0.5 секунды и, если нужно, запускаем следующий шаг
        AddTimer(0.5f, () => StartKillCascade(player, step + 1));
    }
}