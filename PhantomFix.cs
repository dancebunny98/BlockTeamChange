using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils; // <-- ДОБАВЛЕНО: здесь находится CsTeam
using System.Collections.Generic;

namespace PhantomFix;

public class PhantomFixPlugin : BasePlugin
{
    public override string ModuleName => "Phantom Fix";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Assistant";
    public override string ModuleDescription => "Fix phantom player bug after team change";

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

            if (_lastTeam.TryGetValue(slot, out var lastTeam) && lastTeam != currentTeam)
            {
                _lastTeam[slot] = currentTeam;

                if (currentTeam == CsTeam.Spectator || currentTeam == CsTeam.None) continue;

                AddTimer(0.1f, () =>
                {
                    if (!player.IsValid) return;
                    player.CommitSuicide(true, false);

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