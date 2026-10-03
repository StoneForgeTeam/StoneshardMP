using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Join;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

// The game scripts the shared world replaces or skips (the patcher makes them hookable).
[assembly: HookScript(nameof(Scripts.scr_globaltile_seed_validate))]
[assembly: HookScript(nameof(Scripts.scr_globaltile_dungeon_set))]
[assembly: HookScript(nameof(Scripts.scr_dungeonFloorSeedGenerate))]
[assembly: HookScript(nameof(Scripts.scr_dungeonSpecialRoomInit))]
[assembly: HookScript(nameof(Scripts.scr_weatherEveryHourUpdate))]
[assembly: HookScript(nameof(Scripts.scr_weatherEveryMinuteUpdate))]
[assembly: HookScript(nameof(Scripts.scr_smokeEveryHourUpdate))]
[assembly: HookScript(nameof(Scripts.scr_smokeEveryMinuteUpdate))]

namespace StoneshardMP.Features.World;

// One world for everyone in it (legacy StoneshardMP's world sync):
// - Built alike: an area's layout seeds (first visit, respawn) and a dungeon's floors come from the world seed, not
//   randomize()/irandom, so every game in the same world builds the same area or dungeon (MpTileSeedValidate,
//   MpDungeonSeed). Always, solo too - an area a host builds before anyone joins is the one they'll find.
// - Kept alike: when a game saves a location it was running (leaving it: o_roomEntitySaver), what's in it - what's
//   dead, taken, opened - goes to the others, who keep it as their own save of that location (MpLocationStore). A
//   client that followed the host there doesn't: the host's copy is the real one. Tiles whose seeds or dungeon
//   layout are set go out too (MpTileApply).
// - Caught up: a client coming into the host's world asks for a copy of everything the host has - every location's
//   state and every tile - which goes out a few a frame.
// - One sky: the host's weather and fog; a client in its world rolls none of its own.
// Only between games in the same world (the seed) - and a client only once it plays the host's save.
public sealed class WorldSync
{
    // (How many locations and tiles of a copy go out a frame.)
    private const int CopyLocationsPerFrame = 4;
    private const int CopyTilesPerFrame = 20;

    private static readonly HashSet<string> DungeonLayoutKeys = new()
    {
        "DungeonSeed", "dungeon_questFloor", "dungeon_modificationFloor", "dungeon_secretFloor",
    };

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly JoinManager _join;
    // Tiles ("x_y") whose seeds or dungeon were set here, to send.
    private readonly HashSet<string> _changedTiles = new();
    // Host: players waiting for a copy, and the one being copied (where it's got to).
    private readonly Queue<int> _copyQueue = new();
    private int _copyTo = -1;
    private int _copyLocations, _copyTiles, _copyLocation, _copyTile;
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
            string tile = Gml.MpTileSeedValidate(Arg(call, 0), Arg(call, 1), Arg(call, 2));
            if (tile.Length > 0)
                _changedTiles.Add(tile);
            return true;
        });
        Scripts.scr_globaltile_dungeon_set.Before(context, call =>
        {
            if (!_applying && Arg(call, 0).AsString is { } key && DungeonLayoutKeys.Contains(key))
            {
                GmValue x = Arg(call, 2), y = Arg(call, 3);
                _changedTiles.Add($"{(x.IsUndefined ? Game.Global["playerGridX"] : x).AsReal}_{(y.IsUndefined ? Game.Global["playerGridY"] : y).AsReal}");
            }
            return false;
        });
        // A floor's layout seed: the vanilla irandom draws from the world seed's.
        Scripts.scr_dungeonFloorSeedGenerate.Before(context, call =>
        {
            double seed = Gml.MpDungeonSeed(1, Game.CallScript("scr_dungeonGetCurrentFloorNumber", default).AsInt);
            if (seed >= 0)
                Game.CallBuiltin("random_set_seed", seed);
            return false;
        });
        // Which floors are special: rolled from the world seed, then the generator back as it was (the floor's own).
        Scripts.scr_dungeonSpecialRoomInit.Before(context, call =>
        {
            double seed = Gml.MpDungeonSeed(2, 0);
            if (seed < 0)
                return false;
            GmValue previous = Game.CallBuiltin("random_get_seed");
            Game.CallBuiltin("random_set_seed", seed);
            try { call.Result = Scripts.scr_dungeonSpecialRoomInit.CallOriginal(call); }
            finally { Game.CallBuiltin("random_set_seed", previous); }
            return true;
        });
        // A floor rejected: counted, for its next seed.
        context.OnCode("gml_Object_o_dungeon_controller_Other_15", before: (_, _) =>
        {
            Gml.MpDungeonRetryNote();
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
        Session.SessionMode.Host => Gml.MpHostInWorld() && _session.Players.Any(),
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
        foreach (string tile in _changedTiles)
        {
            int sep = tile.IndexOf('_');
            string state = Gml.MpTileExport(double.Parse(tile[..sep]), double.Parse(tile[(sep + 1)..]));
            if (state.Length > 0)
                Send(WorldData.Tile, state);
        }
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
            _session.Send(new WorldDataPacket(WorldData.CopyRequest, Gml.MpWorldSeed(), Array.Empty<byte>()), to: 0);
            _context.Log("In the host's world: asking for its copy of the world");
        }
        _wasInWorld = inWorld;
        // Following the host where we are (it's here too).
        if (!inWorld || ++_frame % 6 != 0)
            return;
        string? mine = PlayerState.Parse(Gml.MpPlayerState())?.Place;
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
        string state = Gml.MpLocationExportSaved(saver);
        if (state.Length > 0)
            Send(WorldData.Location, state);
    }

    // ---- the host's copy for a newcomer ----

    private void CopyTick()
    {
        if (_copyTo < 0)
        {
            if (!_copyQueue.TryDequeue(out int next))
                return;
            string[] counts = Gml.MpWorldCopyBegin().Split('|');
            _copyTo = next;
            _copyLocations = int.Parse(counts[0]);
            _copyTiles = int.Parse(counts[1]);
            _copyLocation = _copyTile = 0;
            string name = _session.Players.FirstOrDefault(p => p.Slot == next)?.Name ?? "a player";
            _context.Log($"Copying our world to {name}: {_copyLocations} locations, {_copyTiles} tiles");
            // (The weather too.)
            _weatherSent = "";
        }
        for (int i = 0; i < CopyTilesPerFrame && _copyTile < _copyTiles; i++)
            Send(WorldData.Tile, Gml.MpWorldCopyItem(true, _copyTile++), _copyTo);
        for (int i = 0; i < CopyLocationsPerFrame && _copyLocation < _copyLocations; i++)
            Send(WorldData.Location, Gml.MpWorldCopyItem(false, _copyLocation++), _copyTo);
        if (_copyTile >= _copyTiles && _copyLocation >= _copyLocations)
            _copyTo = -1;
    }

    // ---- weather ----

    // Host: our weather and fog to everyone, checked twice a second, sent when it changes.
    private void WeatherTick()
    {
        if (++_frame % 30 != 0)
            return;
        string state = Gml.MpWeatherState();
        if (state.Length == 0 || state == _weatherSent)
            return;
        _weatherSent = state;
        Send(WorldData.Weather, state);
    }

    // ---- network ----

    private void Send(WorldData kind, string state, int to = Session.Everyone)
    {
        if (state.Length > 0)
            _session.Send(new WorldDataPacket(kind, Gml.MpWorldSeed(), JoinCompression.Compress(state)), to);
    }

    private void Receive(RemotePlayer sender, WorldDataPacket packet)
    {
        // Only from and for a game in our world.
        if (!Sharing || packet.Seed != Gml.MpWorldSeed())
            return;
        switch (packet.Kind)
        {
            case WorldData.CopyRequest when _session.Mode == Session.SessionMode.Host:
                if (_copyTo != sender.Slot && !_copyQueue.Contains(sender.Slot))
                    _copyQueue.Enqueue(sender.Slot);
                break;
            case WorldData.Location:
                string result = Gml.MpLocationStore(JoinCompression.Decompress(packet.Data));
                if (result.Length > 0)
                    _context.Log($"From {sender.Name}: {result}");
                break;
            case WorldData.Tile:
                _applying = true;
                try { Gml.MpTileApply(JoinCompression.Decompress(packet.Data)); }
                finally { _applying = false; }
                break;
            case WorldData.Weather when _session.Mode == Session.SessionMode.Client && sender.Slot == 0:
                Gml.MpWeatherApply(JoinCompression.Decompress(packet.Data));
                break;
        }
    }

    private static GmValue Arg(ScriptCall call, int index) => index < call.Args.Length ? call.Args[index] : GmValue.Undefined;
}
