using System;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Combat;
using StoneshardMP.Features.Rounds;
using StoneshardMP.Features.Clock;
using StoneshardMP.Features.Contracts;
using StoneshardMP.Features.Debug;
using StoneshardMP.Features.Breakables;
using StoneshardMP.Features.Chests;
using StoneshardMP.Features.Doors;
using StoneshardMP.Features.Traps;
using StoneshardMP.Features.Corpses;
using StoneshardMP.Features.Placeables;
using StoneshardMP.Features.GroundEffects;
using StoneshardMP.Features.Bombs;
using StoneshardMP.Features.Crimes;
using StoneshardMP.Features.Talk;
using StoneshardMP.Features.Dev;
using StoneshardMP.Features.Death;
using StoneshardMP.Features.Recipes;
using StoneshardMP.Features.Summons;
using StoneshardMP.Features.Caravan;
using StoneshardMP.Features.Trade;
using StoneshardMP.Features.Gathering;
using StoneshardMP.Features.Effects;
using StoneshardMP.Features.Join;
using StoneshardMP.Features.Loot;
using StoneshardMP.Features.Map;
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
    private AreaOwnership _ownership = null!;
    private AreaUnits _areaUnits = null!;
    private CombatSync _combat = null!;
    private CrimeSync _crimes = null!;
    private TalkSync _talk = null!;
    private DevTools _dev = null!;
    private DeathSync _death = null!;
    private SleepSync _sleep = null!;
    private RecipeSync _recipes = null!;
    private SummonSync _summons = null!;
    private CaravanSync _caravan = null!;
    private MerchantSync _merchants = null!;
    private NoiseSync _noise = null!;
    private TurnRounds _rounds = null!;
    private TurnTime _turnTime = null!;
    private DoorSync _doors = null!;
    private TrapSync _traps = null!;
    private PlacedTrapSync _placedTraps = null!;
    private CorpseSync _corpses = null!;
    private PlaceableSync _placeables = null!;
    private GroundEffectSync _groundEffects = null!;
    private BombSync _bombs = null!;
    private ChestSync _chests = null!;
    private BreakableSync _breakables = null!;
    private PersonalStash _stash = null!;
    private WorldSlots _slots = null!;
    private SlotRoster _roster = null!;
    private MapMarkerSync _markers = null!;
    private Follow _follow = null!;
    private MultiplayerMenu _menu = null!;
    private WorldClock _clock = null!;
    private DebugDump _dump = null!;
    private JoinManager _join = null!;
    private WorldSync _world = null!;
    private LootSync _loot = null!;
    private GatherSync _gather = null!;
    private QuestSync _quests = null!;
    private ContractSync _contracts = null!;
    private SituationSync _situations = null!;

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
        // Who runs each place players share: whoever got there first; the others follow (units, loot, fights, rounds).
        _ownership = new AreaOwnership(context, _session, () => _join.InSharedWorld);
        _areaUnits = new AreaUnits(context, _session, _ownership, () => _players.ObjectIndex);
        // (Running a place, our units step off a cell another player's on.)
        _players.OwnsArea = () => _ownership.Role == AreaRole.Owner;
        // Combat where players are together: each game resolves its own character's fights, the owner's units the real ones.
        _combat = new CombatSync(context, _session, _areaUnits, _ownership, _players, () => _join.InSharedWorld);
        // A follower's noise - steps, fights, spells - heard by the owner's units, from its stand-in.
        _noise = new NoiseSync(context, _session, _ownership, _players, () => _join.InSharedWorld);
        _crimes = new CrimeSync(context, _session, _areaUnits, _ownership, () => _join.InSharedWorld);
        _talk = new TalkSync(context, _session, _areaUnits, _ownership, _players, () => _join.InSharedWorld);
        // One completed action is one world turn for everyone: a client's moves turn the host's world, the host's own
        // turn the clients' clocks (a place's owner moving its units, streamed to its followers by AreaUnits).
        // Shared turn-based rounds where players are together and anyone needs turns (in combat, bleeding out, on fire),
        // with their turn order shown on the HUD.
        _rounds = new TurnRounds(context, _session, _ownership, () => _join.InSharedWorld);
        context.UI.Hud.Add(new TurnOrder(_rounds, _session, () => _players.ObjectIndex));
        _clock = new WorldClock(context, _session, _ownership, () => _join.InSharedWorld, _ => _rounds.Active);
        // A move's time shared out: 30 seconds over the number of players in the world (a round's turn keeps its 30).
        // Only the host sleeps; everyone fades out with it, and wakes at its clock.
        _sleep = new SleepSync(context, _session, () => _join.InSharedWorld);
        // Recipes and schematics are the party's: one player learning one teaches everyone.
        _recipes = new RecipeSync(_session, () => _join.InSharedWorld);
        // Summons are their caster's: run in the caster's game, a stand-in for each in the others'.
        _summons = new SummonSync(_session, _players, () => _join.InSharedWorld);
        // One caravan, the host's: its state and storage kept alike, its storage one player at a time, moved by the host.
        _caravan = new CaravanSync(context, _session, () => _join.InSharedWorld);
        // One stock per trader: a trade's end sends the trader's goods, gold and sold-out uniques to everyone.
        _merchants = new MerchantSync(context, _session, () => _join.InSharedWorld);
        _turnTime = new TurnTime(context, _session, () => _join.InSharedWorld, () => _rounds.Active);
        // (A move in another place: only its time passes here, not a turn for our units.)
        _clock.PassTime = _turnTime.PassTurnTime;
        _dump = new DebugDump(context, _session, _areaUnits);
        // Our name to the others: the setting, or the Steam name.
        string PlayerName() => settings.Name.Value.Trim() is { Length: > 0 } name ? name : SteamName();
        // The main menu's Multiplayer screens (host, join - its dialog -, the game's Play buttons meanwhile, who's in).
        var join = context.UI.MainMenu.Add(new JoinDialog(_session, settings, PlayerName));
        // The host keeps everyone's save: a client joins the host's world, or makes a character for it.
        // Which of the world's player slots each player plays: the order they joined in, unless the host swaps them.
        _slots = new WorldSlots(_session);
        // Who plays which slot, and who that is - the host's, sent to everyone (the main menu's player list).
        // Host: a save picked on the main menu, gone into with Play - the slots chosen in between.
        var lobby = new HostLobby(context, _session);
        _roster = new SlotRoster(_session, _slots, lobby);
        _join = new JoinManager(context, _session, PlayerName, _slots);
        // One world for everyone in it: areas built alike, what's in them shared, the host's weather.
        _world = new WorldSync(context, _session, _join, _ownership);
        // A multiplayer world's saves are named for who plays in it.
        new SaveNames(context, _session, PlayerName);
        // Live ground loot where players are together: the place's owner's is the real one.
        _loot = new LootSync(context, _session, _ownership, () => _join.InSharedWorld);
        // What a place is built with to be picked - herbs, mushrooms, sticks, berry bushes: picked once for everyone.
        _gather = new GatherSync(context, _session, _ownership, () => _join.InSharedWorld);
        // A client dying: back to its checkpoint (the host's last save), what it picked up since dropped where it fell.
        _death = new DeathSync(context, _session, _join, _ownership, _loot);
        // Doors opened or shut where players are together: the same in every game there.
        _doors = new DoorSync(context, _session, () => _join.InSharedWorld);
        _traps = new TrapSync(_session, () => _join.InSharedWorld);
        // Traps players set - claw traps, caltrops: the place owner's, in everyone's game; they catch enemies only.
        _placedTraps = new PlacedTrapSync(context, _session, _ownership, _players, () => _join.InSharedWorld);
        _corpses = new CorpseSync(_session, _ownership, () => _join.InSharedWorld);
        _placeables = new PlaceableSync(context, _session, _ownership, () => _join.InSharedWorld);
        _groundEffects = new GroundEffectSync(context, _session, _ownership, () => _join.InSharedWorld);
        // Thrown bombs where players are together: their burst seen and heard by all, a jar's swarm the owner's.
        _bombs = new BombSync(context, _session, _ownership, () => _join.InSharedWorld);
        // Chests, barrels and tombs where players are together: one set of contents, one player in each at a time.
        _chests = new ChestSync(context, _session, _ownership, () => _join.InSharedWorld);
        // The chest by the bed: each player's own stash in it, kept in the host's world.
        // Crates, barrels and furniture where players are together: one health pool each, broken for everyone.
        _breakables = new BreakableSync(context, _session, _ownership, () => _join.InSharedWorld);
        _stash = new PersonalStash(context, _session, () => _join.WorldSlot, _slots, () => _join.InSharedWorld);
        // One set of markers on the world map for everyone in the world.
        _markers = new MapMarkerSync(context, _session, () => _join.InSharedWorld);
        // One story: quest steps, reputation, dialogue and location flags, crime records; quest items held by anyone.
        _quests = new QuestSync(context, _session, _join);
        // One set of contracts: the host's, kept alike, with deadlines on the host's clock.
        _contracts = new ContractSync(context, _session, _join, _quests);
        // One set of settlement situations - a fair, a pilgrimage, the economy: the host's.
        _situations = new SituationSync(context, _session, _join);
        _menu = new MultiplayerMenu(context, _session, settings, PlayerName, join, () => _join.Status, _slots, _roster, lobby);
        // Dev tools for the mod's contributors (mod.json): Ctrl+Shift+M.
        _dev = new DevTools(context, new DevHooks
        {
            Session = _session,
            Ownership = _ownership,
            AreaUnits = _areaUnits,
            Players = _players,
            InSharedWorld = () => _join.InSharedWorld,
            Dump = _dump.Write,
            Features = new (string, Func<string>)[]
            {
                ("units", () => _areaUnits.DevSummary),
                ("placeables", () => _placeables.DevSummary),
                ("ground effects", () => _groundEffects.DevSummary),
                ("bombs", () => _bombs.DevSummary),
                ("situations", () => _situations.DevSummary),
                ("steam", () => _session.SteamSummary),
                ("talk", () => _talk.DevSummary),
                ("rounds", () => _rounds.Active ? "a round is on" : "no round"),
            },
            // (What each keeps for the place we're in: dropped, so each starts over and asks the owner again.)
            Resync = () =>
            {
                _areaUnits.Clear();
                _loot.Clear();
                _gather.Clear();
                _corpses.Clear();
                _placeables.Clear();
                _groundEffects.Clear();
                _chests.Clear();
                _breakables.Clear();
                _traps.Clear();
                _placedTraps.Clear();
                _doors.Clear();
            },
        });
        context.Log($"StoneshardMP {context.Manifest.Version} (protocol {Session.Protocol}), LiteNetLib networking");
    }

    // Switched off (or reloaded): out of any session, the port freed.
    public void Unload()
    {
        _session.Stop("");
        _players.Clear();
        _party.Clear();
        _effects.Clear();
        _ownership.Clear();
        _areaUnits.Clear();
        _combat.Clear();
        _rounds.Clear();
        _turnTime.Clear();
        _doors.Clear();
        _traps.Clear();
        _placedTraps.Clear();
        _corpses.Clear();
        _placeables.Clear();
        _groundEffects.Clear();
        _bombs.Clear();
        _talk.Clear();
        _death.Clear();
        _sleep.Clear();
        _recipes.Clear();
        _summons.Clear();
        _caravan.Clear();
        _merchants.Clear();
        _chests.Clear();
        _breakables.Clear();
        _stash.Clear();
        _slots.Clear();
        _roster.Clear();
        _markers.Clear();
        _follow.Clear();
        _clock.Clear();
        _join.Clear();
        _world.Clear();
        _loot.Clear();
        _gather.Clear();
        _quests.Clear();
        _contracts.Clear();
        _situations.Clear();
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
            Profiler.Measure(_context, "slots", _roster.Tick);
            Profiler.Measure(_context, "join", _join.Tick);
            Profiler.Measure(_context, "world", _world.Tick);
            // (Who runs our place, before anything that goes by it.)
            Profiler.Measure(_context, "ownership", _ownership.Tick);
            Profiler.Measure(_context, "loot", _loot.Tick);
            Profiler.Measure(_context, "gather", _gather.Tick);
            Profiler.Measure(_context, "doors", _doors.Tick);
            Profiler.Measure(_context, "traps", _traps.Tick);
            Profiler.Measure(_context, "placed traps", _placedTraps.Tick);
            Profiler.Measure(_context, "corpses", _corpses.Tick);
            Profiler.Measure(_context, "placeables", _placeables.Tick);
            Profiler.Measure(_context, "ground effects", _groundEffects.Tick);
            Profiler.Measure(_context, "talk", _talk.Tick);
            Profiler.Measure(_context, "death", _death.Tick);
            Profiler.Measure(_context, "sleep", _sleep.Tick);
            Profiler.Measure(_context, "recipes", _recipes.Tick);
            Profiler.Measure(_context, "summons", _summons.Tick);
            Profiler.Measure(_context, "caravan", _caravan.Tick);
            Profiler.Measure(_context, "chests", _chests.Tick);
            Profiler.Measure(_context, "breakables", _breakables.Tick);
            Profiler.Measure(_context, "stash", _stash.Tick);
            Profiler.Measure(_context, "map markers", _markers.Tick);
            Profiler.Measure(_context, "quests", _quests.Tick);
            Profiler.Measure(_context, "contracts", _contracts.Tick);
            Profiler.Measure(_context, "situations", _situations.Tick);
            Profiler.Measure(_context, "players", _players.Tick);
            Profiler.Measure(_context, "party", _party.Tick);
            Profiler.Measure(_context, "effects", _effects.Tick);
            Profiler.Measure(_context, "area units", _areaUnits.Tick);
            Profiler.Measure(_context, "rounds", _rounds.Tick);
            Profiler.Measure(_context, "clock", _clock.Tick);
            Profiler.Measure(_context, "dump", _dump.Tick);
            Profiler.Measure(_context, "dev tools", _dev.Tick);
        }
    }

    // The player's Steam name ("Player" without Steam).
    private static string SteamName()
    {
        string name = Steam.PersonaName;
        return name.Length > 0 ? name : "Player";
    }
}
