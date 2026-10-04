using System;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Combat;
using StoneshardMP.Features.Rounds;
using StoneshardMP.Features.Clock;
using StoneshardMP.Features.Contracts;
using StoneshardMP.Features.Debug;
using StoneshardMP.Features.Effects;
using StoneshardMP.Features.Join;
using StoneshardMP.Features.Loot;
using StoneshardMP.Features.Menu;
using StoneshardMP.Features.Party;
using StoneshardMP.Features.Players;
using StoneshardMP.Features.Quests;
using StoneshardMP.Features.Saves;
using StoneshardMP.Features.World;
using StoneshardMP.Net;

namespace StoneshardMP;

// StoneshardMP on StoneForge: co-op over LiteNetLib. A trusted mod (mod.json "trusted": true) - it needs the
// network, and brings LiteNetLib (lib\). Being ported feature by feature from the GML version (MSL), milestone by
// milestone; so far: the session - hosting, joining, players coming and going (Net\Session) - and the other
// players on our screen (Features\Players\PlayerManager) - each feature in its own folder under Features.
public sealed class MultiplayerMod : IStoneMod, ITickable
{
    private ModContext _context = null!;
    private Session _session = null!;
    private PlayerManager _players = null!;
    private PartyFrames _party = null!;
    private EffectManager _effects = null!;
    private AreaUnits _areaUnits = null!;
    private CombatSync _combat = null!;
    private TurnRounds _rounds = null!;
    private Follow _follow = null!;
    private MultiplayerMenu _menu = null!;
    private WorldClock _clock = null!;
    private DebugDump _dump = null!;
    private JoinManager _join = null!;
    private WorldSync _world = null!;
    private LootSync _loot = null!;
    private QuestSync _quests = null!;
    private ContractSync _contracts = null!;

    public void Load(ModContext context)
    {
        _context = context;
        var settings = new MpSettings(context.Settings);
        _session = new Session(context.Manifest.Version, context.Log);
        _players = new PlayerManager(context, _session, () => settings.ShowNames.Value, () => _join.InSharedWorld);
        // A frame for each of the others: health, energy, level, effects, which way they are.
        _party = new PartyFrames(context, _session, settings, () => _join.InSharedWorld);
        // "Follow" on another player's right-click menu: walk after them.
        _follow = new Follow(context, _session, _players);
        _effects = new EffectManager(context, _session);
        _areaUnits = new AreaUnits(context, _session, () => _players.ObjectIndex);
        // Combat where players are together: each game resolves its own character's fights, the host's units the real ones.
        _combat = new CombatSync(context, _session, _areaUnits, _players, () => _join.InSharedWorld);
        // One completed action is one world turn for everyone: a client's moves turn the host's world (its units,
        // streamed back by AreaUnits), the host's own turn the clients' clocks.
        // Shared turn-based rounds where players are together and anyone needs turns (in combat, bleeding out, on fire),
        // with their turn order shown on the HUD.
        _rounds = new TurnRounds(context, _session, () => _join.InSharedWorld);
        context.UI.Hud.Add(new TurnOrder(_rounds, _session, () => _players.ObjectIndex));
        _clock = new WorldClock(context, _session, () => _join.InSharedWorld, _ => _rounds.Active);
        _dump = new DebugDump(context, _session, _areaUnits);
        // The main menu's Multiplayer screens (host, join - its dialog -, the game's Play buttons meanwhile) and the
        // Players & Settings window.
        var window = context.UI.MainMenu.Add(new MultiplayerWindow(_session, settings, SteamName));
        var join = context.UI.MainMenu.Add(new JoinDialog(_session, settings, () => window.PlayerName));
        // The host keeps everyone's save: a client joins the host's world, or makes a character for it.
        _join = new JoinManager(context, _session, () => window.PlayerName);
        // One world for everyone in it: areas built alike, what's in them shared, the host's weather.
        _world = new WorldSync(context, _session, _join);
        // A multiplayer world's saves are named for who plays in it.
        new SaveNames(context, _session, () => window.PlayerName);
        // Live ground loot where players are together: the host's is the real one.
        _loot = new LootSync(context, _session, () => _join.InSharedWorld);
        // One story: quest steps, reputation, dialogue and location flags, crime records; quest items held by anyone.
        _quests = new QuestSync(context, _session, _join);
        // One set of contracts: the host's, kept alike, with deadlines on the host's clock.
        _contracts = new ContractSync(context, _session, _join, _quests);
        _menu = new MultiplayerMenu(context, _session, settings, () => window.PlayerName, join, window, () => _join.Status);
        context.Log($"StoneshardMP {context.Manifest.Version} (protocol {Session.Protocol}), LiteNetLib networking");
    }

    // Switched off (or reloaded): out of any session, the port freed.
    public void Unload()
    {
        _session.Stop("");
        _players.Clear();
        _party.Clear();
        _effects.Clear();
        _areaUnits.Clear();
        _combat.Clear();
        _rounds.Clear();
        _follow.Clear();
        _clock.Clear();
        _join.Clear();
        _world.Clear();
        _loot.Clear();
        _quests.Clear();
        _contracts.Clear();
    }

    public void Tick(double deltaTime)
    {
        // (Where our player is, worked out again this frame when asked.)
        OurPlayer.NewFrame();
        // (Each part timed for StoneForge's profiler, Ctrl+Shift+P. The network's time includes what the packets it hands
        // out do - an area's units applied, loot made...)
        Profiler.Measure(_context, "network", _session.Poll);
        if (Game.Running)
        {
            Profiler.Measure(_context, "menu", _menu.Tick);
            Profiler.Measure(_context, "join", _join.Tick);
            Profiler.Measure(_context, "world", _world.Tick);
            Profiler.Measure(_context, "loot", _loot.Tick);
            Profiler.Measure(_context, "quests", _quests.Tick);
            Profiler.Measure(_context, "contracts", _contracts.Tick);
            Profiler.Measure(_context, "players", _players.Tick);
            Profiler.Measure(_context, "party", _party.Tick);
            Profiler.Measure(_context, "effects", _effects.Tick);
            Profiler.Measure(_context, "area units", _areaUnits.Tick);
            Profiler.Measure(_context, "rounds", _rounds.Tick);
            Profiler.Measure(_context, "clock", _clock.Tick);
            Profiler.Measure(_context, "dump", _dump.Tick);
        }
    }

    // The player's Steam name ("Player" without Steam).
    private static string SteamName()
    {
        try
        {
            string name = Game.CallBuiltinUnrestricted("steam_get_persona_name", default, default).AsString;
            return name.Length > 0 ? name : "Player";
        }
        catch (Exception)
        {
            return "Player";
        }
    }
}
