using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Features.Join;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

// The game scripts the shared world replaces or skips (the patcher makes them hookable).
[assembly: HookScript(nameof(Scripts.scr_globaltile_seed_validate))]
[assembly: HookScript(nameof(Scripts.scr_globaltile_dungeon_set))]
[assembly: HookScript(nameof(Scripts.scr_globaltile_dungeon_set_map))]
[assembly: HookScript(nameof(Scripts.scr_globaltile_dungeon_set_list))]
[assembly: HookScript(nameof(Scripts.scr_dungeonFloorSeedGenerate))]
[assembly: HookScript(nameof(Scripts.scr_dungeonSpecialRoomInit))]
[assembly: HookScript(nameof(Scripts.scr_weatherEveryHourUpdate))]
[assembly: HookScript(nameof(Scripts.scr_weatherEveryMinuteUpdate))]
[assembly: HookScript(nameof(Scripts.scr_smokeEveryHourUpdate))]
[assembly: HookScript(nameof(Scripts.scr_smokeEveryMinuteUpdate))]

namespace StoneshardMP.Features.World;

// One world for everyone in it (legacy StoneshardMP's world sync):
// - Built alike: an area's layout seeds (first visit, respawn) and a dungeon's floors come from the world seed, not
//   randomize()/irandom, so every game in the same world builds the same area or dungeon (SharedWorld.TileSeedValidate,
//   SharedWorld.DungeonSeed). Always, solo too - an area a host builds before anyone joins is the one they'll find.
// - Kept alike: when a game saves a location it was running (leaving it: o_roomEntitySaver), what's in it - what's
//   dead, taken, opened - goes to the others, who keep it as their own save of that location (SharedWorld.LocationStore). A
//   client that followed the host there doesn't: the host's copy is the real one. A world-map tile goes out too
//   (SharedWorld.TileApply) when its seeds are set, when one of its dungeon's values is (its floor seeds, saved floor graphs,
//   boss, open, cage, mob levels, contract...), and when a location on it is
//   saved - leaving a dungeon floor, its graph is saved into the dungeon's maps in place.
// - Caught up: a client coming into the host's world asks for a copy of everything the host has - every location's
//   state and every tile - which goes out a few a frame.
// - One sky: the host's weather and fog; a client in its world rolls none of its own.
// Only between games in the same world (the seed) - and a client only once it plays the host's save.
public sealed class WorldSync
{
    // (How many locations and tiles of a copy go out a frame.)
    private const int CopyLocationsPerFrame = 4;
    private const int CopyTilesPerFrame = 20;

    // (A dungeon's reset timer counts down every hour on every dungeon, in every game alike - the clock is shared - so
    // it isn't news to send; it goes along with the rest when anything else is.)
    private const string DungeonResetKey = "dungeon_reset";

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly JoinManager _join;
    // Tiles whose seeds or dungeon were set here, to send.
    private readonly HashSet<(int X, int Y)> _changedTiles = new();
    // Host: players waiting for a copy, and the one being copied (where it's got to).
    private readonly Queue<int> _copyQueue = new();
    private int _copyTo = -1;
    private List<(string Location, GmValue Room, GmValue Preset)> _copyLocations = new();
    private List<(int X, int Y)> _copyTiles = new();
    private int _copyLocation, _copyTile;
    // (Taking another game's tile: its dungeon keys aren't news to send back.)
    private bool _applying;
    private bool _wasInWorld;
    // Client: we've been following the host in this place - leaving it, our copy isn't sent.
    private string? _place;
    private bool _followedHere;
    private int _frame;
    private string _weatherSent = "";

    public WorldSync(ModContext context, Session session, JoinManager join)
    {
        _context = context;
        _session = session;
        _join = join;
        session.On<WorldDataPacket>(Receive);
        session.PlayerLeft += player =>
        {
            if (_copyTo == player.Slot)
                _copyTo = -1;
        };

        Scripts.scr_globaltile_seed_validate.Before(context, call =>
        {
            if (SharedWorld.TileSeedValidate(Arg(call, 0), Arg(call, 1), Arg(call, 2)) is { } tile)
                _changedTiles.Add(tile);
            return true;
        });
        // A dungeon's value set - a number or text, a map, a list: its tile to send.
        Func<ScriptCall, bool> dungeonSet = call =>
        {
            if (!_applying && Arg(call, 0).AsString is { } key && key != DungeonResetKey)
                QueueTile(Arg(call, 2), Arg(call, 3));
            return false;
        };
        Scripts.scr_globaltile_dungeon_set.Before(context, dungeonSet);
        Scripts.scr_globaltile_dungeon_set_map.Before(context, dungeonSet);
        Scripts.scr_globaltile_dungeon_set_list.Before(context, dungeonSet);
        // A floor's layout seed, and which floors are special: the vanilla rolls, drawn from the world seed's (then the
        // game's random carries on, as it would have).
        Scripts.scr_dungeonFloorSeedGenerate.Before(context, call =>
            Seeded(call, Scripts.scr_dungeonFloorSeedGenerate, SharedWorld.DungeonSeed(1, WorldMap.DungeonFloor)));
        Scripts.scr_dungeonSpecialRoomInit.Before(context, call =>
            Seeded(call, Scripts.scr_dungeonSpecialRoomInit, SharedWorld.DungeonSeed(2, 0)));
        // A floor rejected: counted, for its next seed.
        context.OnCode("gml_Object_o_dungeon_controller_Other_15", before: (_, _) =>
        {
            SharedWorld.DungeonRetry();
            return false;
        });
        // A location saved as we leave it: to the others, if we were running it.
        context.OnCode("gml_Object_o_roomEntitySaver_Other_12", after: (self, _) => LocationSaved(self));
        // A client in the host's world takes its weather instead of rolling its own.
        Scripts.scr_weatherEveryHourUpdate.Before(context, call => _join.ClientInWorld);
        Scripts.scr_weatherEveryMinuteUpdate.Before(context, call => _join.ClientInWorld);
        Scripts.scr_smokeEveryHourUpdate.Before(context, call => _join.ClientInWorld);
        Scripts.scr_smokeEveryMinuteUpdate.Before(context, call => _join.ClientInWorld);
    }

    public void Clear()
    {
        _changedTiles.Clear();
        _copyQueue.Clear();
        _copyTo = -1;
        _wasInWorld = false;
        _place = null;
        _followedHere = false;
        _weatherSent = "";
    }

    // Whether we share our world: the host in a world with players, a client in the host's.
    private bool Sharing => _session.Mode switch
    {
        Session.SessionMode.Host => JoinSave.HostInWorld() && _session.Players.Any(),
        Session.SessionMode.Client => _join.ClientInWorld,
        _ => false,
    };

    // Each frame.
    public void Tick()
    {
        bool sharing = Sharing;
        if (_session.Mode == Session.SessionMode.Client)
            ClientTick();
        if (!sharing)
        {
            _changedTiles.Clear();
            return;
        }
        foreach (var (x, y) in _changedTiles)
            Send(WorldData.Tile, SharedWorld.TileExport(x, y));
        _changedTiles.Clear();
        if (_session.Mode == Session.SessionMode.Host)
        {
            CopyTick();
            WeatherTick();
        }
    }

    private void ClientTick()
    {
        // In the host's world now: ask for everything it has.
        bool inWorld = _join.ClientInWorld && Gm.InGame;
        if (inWorld && !_wasInWorld)
        {
            _session.Send(new WorldDataPacket(WorldData.CopyRequest, SharedWorld.WorldSeed(), Array.Empty<byte>()), to: 0);
            _context.Log("In the host's world: asking for its copy of the world");
        }
        _wasInWorld = inWorld;
        // Following the host where we are (it's here too).
        if (!inWorld || ++_frame % 6 != 0)
            return;
        string? mine = OurPlayer.State()?.Place;
        if (mine == null)
            return;
        if (mine != _place)
        {
            _place = mine;
            _followedHere = false;
        }
        if (_session.Players.FirstOrDefault(p => p.Slot == 0)?.State?.Place == mine)
            _followedHere = true;
    }

    // ---- locations ----

    private void LocationSaved(Instance saver)
    {
        if (!Sharing)
            return;
        // A client that followed the host here leaves the host's copy as the real one.
        if (_session.Mode == Session.SessionMode.Client && _followedHere)
            return;
        string state = SharedWorld.LocationExportSaved(saver);
        if (state.Length > 0)
            Send(WorldData.Location, state);
        // Its tile too: leaving a dungeon floor, the floor's graph is saved in the dungeon's maps, filled in place
        // (no setter to hook).
        QueueTile(GmValue.Undefined, GmValue.Undefined);
    }

    // ---- the host's copy for a newcomer ----

    private void CopyTick()
    {
        if (_copyTo < 0)
        {
            if (!_copyQueue.TryDequeue(out int next))
                return;
            _copyTo = next;
            (_copyLocations, _copyTiles) = SharedWorld.CopyList();
            _copyLocation = _copyTile = 0;
            string name = _session.Players.FirstOrDefault(p => p.Slot == next)?.Name ?? "a player";
            _context.Log($"Copying our world to {name}: {_copyLocations.Count} locations, {_copyTiles.Count} tiles");
            // (The weather too.)
            _weatherSent = "";
        }
        // (Each as it is now: the list is only which ones.)
        for (int i = 0; i < CopyTilesPerFrame && _copyTile < _copyTiles.Count; i++)
        {
            var (x, y) = _copyTiles[_copyTile++];
            Send(WorldData.Tile, SharedWorld.TileExport(x, y), _copyTo);
        }
        for (int i = 0; i < CopyLocationsPerFrame && _copyLocation < _copyLocations.Count; i++)
        {
            var (location, room, preset) = _copyLocations[_copyLocation++];
            Send(WorldData.Location, SharedWorld.LocationExport(location, room, preset), _copyTo);
        }
        if (_copyTile >= _copyTiles.Count && _copyLocation >= _copyLocations.Count)
            _copyTo = -1;
    }

    // ---- weather ----

    // Host: our weather and fog to everyone, checked twice a second, sent when it changes.
    private void WeatherTick()
    {
        if (++_frame % 30 != 0)
            return;
        string state = SharedWorld.WeatherState();
        if (state.Length == 0 || state == _weatherSent)
            return;
        _weatherSent = state;
        Send(WorldData.Weather, state);
    }

    // ---- network ----

    private void Send(WorldData kind, string state, int to = Session.Everyone)
    {
        if (state.Length > 0)
            _session.Send(new WorldDataPacket(kind, SharedWorld.WorldSeed(), JoinCompression.Compress(state)), to);
    }

    private void Receive(RemotePlayer sender, WorldDataPacket packet)
    {
        // Only from and for a game in our world.
        if (!Sharing || packet.Seed != SharedWorld.WorldSeed())
            return;
        switch (packet.Kind)
        {
            case WorldData.CopyRequest when _session.Mode == Session.SessionMode.Host:
                if (_copyTo != sender.Slot && !_copyQueue.Contains(sender.Slot))
                    _copyQueue.Enqueue(sender.Slot);
                break;
            case WorldData.Location:
                string result = SharedWorld.LocationStore(JoinCompression.Decompress(packet.Data));
                if (result.Length > 0)
                    _context.Log($"From {sender.Name}: {result}");
                break;
            case WorldData.Tile:
                _applying = true;
                try { SharedWorld.TileApply(JoinCompression.Decompress(packet.Data)); }
                finally { _applying = false; }
                break;
            case WorldData.Weather when _session.Mode == Session.SessionMode.Client && sender.Slot == 0:
                SharedWorld.WeatherApply(JoinCompression.Decompress(packet.Data));
                break;
        }
    }

    // A world-map tile to send (undefined: the one we're on).
    private void QueueTile(GmValue x, GmValue y)
    {
        if (WorldMap.PlayerCell is var (gridX, gridY))
            _changedTiles.Add((x.IsUndefined ? gridX : x.AsInt, y.IsUndefined ? gridY : y.AsInt));
    }

    // A script run with the game's random seeded (a dungeon seed: -1, none - as vanilla).
    private static bool Seeded(ScriptCall call, Script script, long seed)
    {
        if (seed < 0)
            return false;
        call.Result = Game.WithSeed(seed, () => script.CallOriginal(call));
        return true;
    }

    private static GmValue Arg(ScriptCall call, int index) => index < call.Args.Length ? call.Args[index] : GmValue.Undefined;
}
