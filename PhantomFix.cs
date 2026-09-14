using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using System.Collections.Generic;

namespace PhantomFix;

public class PhantomFixPlugin : BasePlugin
{
    public override string ModuleName => "Phantom Fix";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Assistant";
    public override string ModuleDescription => "Fix phantom player bug after team change";

    // Храним последнюю известную команду игрока
    private readonly Dictionary<int, CsTeam> _lastTeam = new();

    public override void Load(bool hotReload)
    {
        // Слушатель входа игрока на сервер
        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
        
        // Слушатель каждого тика для отслеживания смены команды
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
        // Проверяем всех игроков на сервере
        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid) continue;

            var slot = player.Slot;
            var currentTeam = player.Team;

            // Если команда изменилась
            if (_lastTeam.TryGetValue(slot, out var lastTeam) && lastTeam != currentTeam)
            {
                _lastTeam[slot] = currentTeam;

                // Пропускаем наблюдателей и невалидные состояния
                if (currentTeam == CsTeam.Spectator || currentTeam == CsTeam.None) continue;

                // Принудительно "пересоздаём" игрока: убиваем и респавним
                // Задержка в 0.1 сек (6 тиков) нужна, чтобы движок успел применить смену команды
                AddTimer(0.1f, () =>
                {
                    if (!player.IsValid) return;

                    // Убиваем игрока, чтобы сбросить "призрачное" состояние
                    player.CommitSuicide(true, false);

                    // Небольшая дополнительная задержка перед респавном
                    AddTimer(0.1f, () =>
                    {
                        if (player.IsValid && player.Team != CsTeam.Spectator && player.Team != CsTeam.None)
                        {
                            player.Respawn();
                        }
                    });
                });
            }
        }
    }
}