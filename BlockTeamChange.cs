using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace BlockTeamChange;

public sealed class BlockTeamChangeConfig : IBasePluginConfig
{
    public int Version { get; set; } = 1;
    public string Language { get; set; } = "ru";
    public bool LockDuringFreezeTime { get; set; } = true;
    public bool LockAfterRoundEnd { get; set; } = true;
    public bool PreventLateJoinSpawn { get; set; } = true;
    public bool SpectateUnpickedPlayers { get; set; } = true;
}

public sealed class BlockTeamChangePlugin : BasePlugin, IPluginConfig<BlockTeamChangeConfig>
{
    public override string ModuleName => "BlockTeamChange";
    public override string ModuleVersion => "2.2.0";
    public override string ModuleAuthor => "Assistant";
    public override string ModuleDescription => "Controls team selection and late-join spawns";

    public BlockTeamChangeConfig Config { get; set; } = new();

    private Dictionary<string, string> _translations = new();
    private ConVar? _joinGraceTime;
    private float? _originalJoinGraceTime;
    private ConVar? _forcePickTime;
    private float? _originalForcePickTime;
    private bool _freezeTime;
    private bool _roundEnded;
    private bool _liveRoundStarted;

    public void OnConfigParsed(BlockTeamChangeConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        LoadTranslations();
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundFreezeEnd>(OnRoundFreezeEnd);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        AddCommandListener("jointeam", OnJoinTeam, HookMode.Pre);
        AddCommandListener("spectate", OnSpectate, HookMode.Pre);

        if (hotReload)
            Server.NextFrame(RestoreRoundState);

        if (Config.PreventLateJoinSpawn)
        {
            // The game enforces this before it creates a late joiner's pawn.
            _joinGraceTime = ConVar.Find("mp_join_grace_time");
            if (_joinGraceTime != null)
            {
                _originalJoinGraceTime = _joinGraceTime.GetPrimitiveValue<float>();
                EnsureJoinGraceDisabled();
            }
            else
            {
                Logger.LogWarning("mp_join_grace_time was unavailable; late-join spawning cannot be controlled.");
            }
        }

        if (Config.SpectateUnpickedPlayers)
        {
            _forcePickTime = ConVar.Find("mp_force_pick_time");
            if (_forcePickTime != null)
            {
                _originalForcePickTime = _forcePickTime.GetPrimitiveValue<float>();
                EnsureAutoPickDisabled();
            }
            else
            {
                Logger.LogWarning("mp_force_pick_time was unavailable; automatic team assignment cannot be controlled.");
            }
        }
    }

    public override void Unload(bool hotReload)
    {
        if (_joinGraceTime != null && _originalJoinGraceTime.HasValue &&
            _joinGraceTime.GetPrimitiveValue<float>() == 0.0f)
            _joinGraceTime.SetValue(_originalJoinGraceTime.Value);

        if (_forcePickTime != null && _originalForcePickTime.HasValue &&
            _forcePickTime.GetPrimitiveValue<float>() == 86400.0f)
            _forcePickTime.SetValue(_originalForcePickTime.Value);
    }

    private void OnMapStart(string _)
    {
        ResetRoundState();
        EnsureAutoPickDisabled();
    }

    private void OnClientPutInServer(int slot)
    {
        if (!Config.SpectateUnpickedPlayers || !_originalForcePickTime.HasValue)
            return;

        var player = Utilities.GetPlayers().FirstOrDefault(p => p.Slot == slot);
        if (!IsHuman(player))
            return;

        var selectionTime = _originalForcePickTime.Value > 0 ? _originalForcePickTime.Value : 15.0f;
        AddTimer(selectionTime, () =>
        {
            if (IsHuman(player) && player!.TeamNum == 0)
                player.ChangeTeam(CsTeam.Spectator);
        });
    }

    private HookResult OnRoundStart(EventRoundStart _, GameEventInfo __)
    {
        EnsureJoinGraceDisabled();
        EnsureAutoPickDisabled();
        _roundEnded = false;
        _liveRoundStarted = !IsWarmup();
        _freezeTime = _liveRoundStarted;
        return HookResult.Continue;
    }

    private HookResult OnRoundFreezeEnd(EventRoundFreezeEnd _, GameEventInfo __)
    {
        _freezeTime = false;
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd _, GameEventInfo __)
    {
        if (_liveRoundStarted && !IsWarmup())
            _roundEnded = true;
        _freezeTime = false;
        return HookResult.Continue;
    }

    private HookResult OnJoinTeam(CCSPlayerController? player, CommandInfo _)
    {
        if (!IsHuman(player) || IsWarmup())
            return HookResult.Continue;

        if (Config.LockAfterRoundEnd && _roundEnded)
        {
            player!.PrintToChat(Translate("round_ended"));
            return HookResult.Handled;
        }

        if (Config.LockDuringFreezeTime && _freezeTime)
        {
            player!.PrintToChat(Translate("freeze_time"));
            return HookResult.Handled;
        }

        return HookResult.Continue;
    }

    private HookResult OnSpectate(CCSPlayerController? player, CommandInfo _)
    {
        if (!IsHuman(player) || IsWarmup() || !Config.LockAfterRoundEnd || !_roundEnded)
            return HookResult.Continue;

        player!.PrintToChat(Translate("round_ended"));
        return HookResult.Handled;
    }

    private void ResetRoundState()
    {
        _roundEnded = false;
        _freezeTime = false;
        _liveRoundStarted = false;
    }

    private void EnsureJoinGraceDisabled()
    {
        if (Config.PreventLateJoinSpawn && _joinGraceTime != null &&
            _joinGraceTime.GetPrimitiveValue<float>() != 0.0f)
            _joinGraceTime.SetValue(0.0f);
    }

    private void EnsureAutoPickDisabled()
    {
        if (Config.SpectateUnpickedPlayers && _forcePickTime != null &&
            _forcePickTime.GetPrimitiveValue<float>() != 86400.0f)
            _forcePickTime.SetValue(86400.0f);
    }

    private void RestoreRoundState()
    {
        var rules = GetGameRules();
        if (rules is null || rules.WarmupPeriod) return;
        _liveRoundStarted = rules.RoundStartCount > 0;
        _roundEnded = _liveRoundStarted && rules.RoundWinStatus != 0;
    }

    private static bool IsWarmup()
    {
        return GetGameRules()?.WarmupPeriod ?? false;
    }

    private static CCSGameRules? GetGameRules() =>
        Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .FirstOrDefault()?.GameRules;

    private static bool IsHuman(CCSPlayerController? player) =>
        player is { IsValid: true, IsBot: false, IsHLTV: false };

    private string Translate(string key) => _translations.GetValueOrDefault(key, key);

    private void LoadTranslations()
    {
        var language = Config.Language.Equals("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
        var path = Path.Combine(ModuleDirectory, "lang", $"{language}.json");
        if (!File.Exists(path))
            path = Path.Combine(ModuleDirectory, "lang", "en.json");
        if (File.Exists(path))
            _translations = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
    }
}
