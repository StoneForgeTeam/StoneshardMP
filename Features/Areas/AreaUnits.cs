using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using LiteNetLib;
using StoneshardMP.Net.Packets;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Areas;

// An area's units - NPCs, animals, enemies - are the host's where it shares a place with clients: the host sends its
// roster every few frames (Snapshot), and a client there makes its own match it (Apply): each of the host's units bound
// to its twin here or made, kept as the host has it (cell, health, state, animation), its own AI off, and anything the
// host didn't send removed. (Legacy: scr_mp_area_unit_snapshot, scr_mp_area_unit_apply.)
public sealed class AreaUnits
{
    // The variables the game picks a unit's animation from: copied, not only the sprite it's showing.
    private static readonly string[] LookKeys =
    {
        "idle_spr", "default_sprite", "fight_sprite", "corpse_sprite", "head_sprite", "ko_sprite", "jailed_spr",
        "spr_sleep", "spr_work", "avatar", "fixed_sprite", "flying_sprite", "npc_sprite",
    };

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Func<int> _playerObject;
    private int _frame;
    // Whether a sprite is one of the game's own (the same id in every game) - a sprite made at run time isn't.
    private readonly Dictionary<int, bool> _assetSprites = new();
    // Host: our units' sync ids (what they go by on the wire).
    private readonly Dictionary<Instance, long> _syncIds = new();
    private long _nextSyncId;
    // Client: the host's units bound to ours (by the host's sync id), every unit of ours ever bound (an unbound twin is
    // looked for among the rest), and which we were sent in the latest roster.
    private readonly Dictionary<long, Instance> _bound = new();
    private readonly HashSet<Instance> _everBound = new();
    private readonly HashSet<Instance> _current = new();
    // The place those are for: a new place starts them over (the units of the last one are gone).
    private string? _place;
    // Client: what was last applied to each of our units (the host's entry for it) - only what's changed since is
    // written, each write being a call into the game; each unit's object, read once; the size of the player's list of
    // units to run each turn, after ours were last taken out of it.
    private readonly Dictionary<Instance, JsonObject> _applied = new();
    private readonly Dictionary<Instance, int> _objectOf = new();
    private int _turnsSize = -1;
    // Client: the roster being applied, a few units a frame (UnitsPerFrame) rather than all at once - the next one
    // replaces it, and starts over; where it's got to, the units it's bound so far, and whether any is new.
    private const int UnitsPerFrame = 12;
    private JsonObject[]? _roster;
    private int _next;
    private readonly HashSet<Instance> _pass = new();
    private bool _passBound;
    // Client: which of our units are big (more than one cell - read once), and the units not yet bound with their object
    // and cell, worked out once a pass when a unit needs its twin.
    private readonly Dictionary<Instance, bool> _poly = new();
    private Dictionary<(int Obj, int X, int Y), List<Instance>>? _unbound;

    public AreaUnits(ModContext context, Session session, Func<int> playerObject)
    {
        _context = context;
        _session = session;
        _playerObject = playerObject;
        session.On<AreaUnitsPacket>(Receive);
    }

    public void Clear()
    {
        _frame = 0;
        _place = null;
        _syncIds.Clear();
        _bound.Clear();
        _everBound.Clear();
        _current.Clear();
        _applied.Clear();
        _objectOf.Clear();
        _turnsSize = -1;
        _roster = null;
        _pass.Clear();
        _poly.Clear();
        _unbound = null;
    }

    /// <summary>Whether this unit is one of the host's, from the latest roster (for the debug dump).</summary>
    public bool IsHosts(Instance unit) => _current.Contains(unit.Persist());

    public void Tick()
    {
        // (A client: the roster it's applying, a few more units of it.)
        if (_roster != null && _session.Mode == Session.SessionMode.Client)
            Profiler.Measure(_context, "area units applied", ApplySome);
        if (!_session.Connected || !Gm.InGame || _session.Mode != Session.SessionMode.Host || ++_frame % 6 != 0)
            return;
        PlayerState? mine = OurPlayer.State();
        if (mine == null || !_session.Players.Any(p => p.State?.Place == mine.Place))
            return;
        NewPlace(mine.Place);
        string snapshot = Snapshot();
        foreach (RemotePlayer player in _session.Players)
            if (player.State?.Place == mine.Place)
                // A complete room roster is often larger than LiteNetLib's 1,020-byte sequenced-packet cap.
                // ReliableOrdered fragments it safely and also ensures a follower never applies a newer roster
                // before an older snapshot that created one of its bindings.
                _session.Send(new AreaUnitsPacket(mine.Place, snapshot), player.Slot, DeliveryMethod.ReliableOrdered);
    }

    private void Receive(RemotePlayer sender, AreaUnitsPacket packet)
    {
        if (_session.Mode != Session.SessionMode.Client || sender.Slot != 0)
            return;
        if (OurPlayer.State()?.Place != packet.Place)
            return;
        NewPlace(packet.Place);
        // (Taken in the next frames - the latest roster, from its start.)
        if (packet.Snapshot.Length > 0 && JsonNode.Parse(packet.Snapshot) is JsonArray roster)
        {
            _roster = roster.OfType<JsonObject>().ToArray();
            _next = 0;
            _pass.Clear();
            _passBound = false;
            _unbound = null;
        }
    }

    private void NewPlace(string place)
    {
        if (place == _place)
            return;
        _place = place;
        _syncIds.Clear();
        _bound.Clear();
        _everBound.Clear();
        _current.Clear();
        _applied.Clear();
        _objectOf.Clear();
        _turnsSize = -1;
        _roster = null;
        _pass.Clear();
        _poly.Clear();
        _unbound = null;
    }

    // Host: every real unit in the room - NPCs, animals, enemies; not other players' - as JSON.
    private string Snapshot()
    {
        int playerObject = _playerObject();
        var units = new JsonArray();
        foreach (Instance unit in Instances.All(GameObjectId.o_enemy))
        {
            int obj = unit.Get("object_index").AsInt;
            if (obj == playerObject)
                continue;
            var look = new JsonObject();
            foreach (string key in LookKeys)
                // (A sprite made at run time has an id of this game's alone: not copied.)
                if (unit.Get(key) is { Kind: GmKind.Real } value && (value.AsReal < 0 || IsAssetSprite(value.AsInt)))
                    look[key] = value.AsReal;
            var (x, y) = UnitGrid.CellOf(unit);
            if (!_syncIds.TryGetValue(unit, out long syncId))
                _syncIds[unit] = syncId = ++_nextSyncId;
            units.Add(new JsonObject
            {
                ["id"] = syncId, ["obj"] = obj, ["x"] = x, ["y"] = y, ["hp"] = unit.Get("HP").ToJsonNode(),
                ["state"] = unit.Get("state").AsString, ["sleeping"] = unit.Get("is_sleeping").ToJsonNode(),
                ["neutral"] = unit.Get("is_neutral").ToJsonNode(), ["flip"] = unit.Get("image_xscale").AsReal < 0,
                ["sprite"] = unit.Get("sprite_index").ToJsonNode(), ["frame"] = unit.Get("image_index").ToJsonNode(),
                ["speed"] = unit.Get("image_speed").ToJsonNode(), ["angle"] = unit.Get("image_angle").ToJsonNode(),
                ["alpha"] = unit.Get("image_alpha").ToJsonNode(), ["look"] = look,
                ["npcName"] = unit.Get("name").ToJsonNode(), ["lifeAnimation"] = unit.Get("is_life").ToJsonNode(),
            });
        }
        return units.ToJsonString();
    }

    // Client: the host's roster made ours, UnitsPerFrame units a frame - each of its units bound to our twin (same
    // object, on the same cell, never bound before) or made, set as the host has it, and moved through the game's grids.
    // Once the whole roster's done: ours taken out of our turns (the host runs them), and anything the host didn't send
    // removed, without its Destroy event (no loot, corpse or kill credit here).
    private void ApplySome()
    {
        if (_roster == null)
            return;
        if (!Gm.InstanceExists(GameObjectId.o_player) || UnitGrid.Current() is not { } grids)
        {
            _roster = null;
            return;
        }
        for (int done = 0; done < UnitsPerFrame && _next < _roster.Length; done++)
            Profiler.Measure(_context, "area units: unit", () => ApplyUnit(_roster[_next++], grids));
        if (_next < _roster.Length)
            return;
        _roster = null;
        Profiler.Measure(_context, "area units: tidy", Tidy);
    }

    private void ApplyUnit(JsonObject u, UnitGrid.Grids grids)
    {
        long hostId = (long)Number(u["id"]);
        int obj = Int(u["obj"]), x = Int(u["x"]), y = Int(u["y"]);
        Instance unit = _bound.GetValueOrDefault(hostId);
        if (unit.IsNone || !unit.Exists)
        {
            unit = Profiler.Measure(_context, "area units: bind", () => Bind(obj, x, y));
            if (unit.IsNone || !unit.Exists)
                return;
            _bound[hostId] = unit;
            _everBound.Add(unit);
            _applied.Remove(unit);
            _passBound = true;
        }
        _pass.Add(unit);
        JsonObject? last = _applied.GetValueOrDefault(unit);
        Set(unit, u, last);
        if (last == null || Int(last["x"]) != x || Int(last["y"]) != y)
        {
            if (!_poly.TryGetValue(unit, out bool poly))
                _poly[unit] = poly = unit.Get("is_poly_cell").AsBool;
            UnitGrid.Move(unit, x, y, grids, poly);
        }
        _applied[unit] = u;
    }

    // One of the host's units we have none bound to: our twin (same object, same cell, never bound) - from a table of
    // our unbound units made once a pass - or a new one.
    private Instance Bind(int obj, int x, int y)
    {
        if (_unbound == null)
        {
            int playerObject = _playerObject();
            _unbound = new();
            foreach (Instance local in Instances.All(GameObjectId.o_enemy))
            {
                if (_everBound.Contains(local) || ObjectOf(local) == playerObject)
                    continue;
                var (cx, cy) = UnitGrid.CellOf(local);
                var key = (ObjectOf(local), cx, cy);
                if (!_unbound.TryGetValue(key, out var list))
                    _unbound[key] = list = new();
                list.Add(local);
            }
        }
        if (_unbound.TryGetValue((obj, x, y), out var twins))
            while (twins.Count > 0)
            {
                Instance twin = twins[^1];
                twins.RemoveAt(twins.Count - 1);
                if (!_everBound.Contains(twin) && twin.Exists)
                    return twin;
            }
        return UnitGrid.InstanceOf(Game.CallScript("scr_enemy_create", default, x * UnitGrid.Cell + 13, y * UnitGrid.Cell + 13, obj, false, false));
    }

    // A whole roster done: ours out of our turns - between turns only, the turn may be walking the list; only when a
    // unit's new to us or the list has changed since - and the units the host didn't send gone.
    private void Tidy()
    {
        _current.Clear();
        _current.UnionWith(_pass);
        int turns = UnitGrid.TurnsCount();
        if (_passBound || turns != _turnsSize)
        {
            UnitGrid.RemoveFromTurns(_current.Contains);
            _turnsSize = UnitGrid.TurnsCount();
        }
        int playerObject = _playerObject();
        foreach (Instance gone in Instances.All(GameObjectId.o_enemy).Where(unit => !_current.Contains(unit) && ObjectOf(unit) != playerObject))
        {
            UnitGrid.Remove(gone);
            _applied.Remove(gone);
            _objectOf.Remove(gone);
            _poly.Remove(gone);
        }
    }

    // A unit's object (it never changes: read once).
    private int ObjectOf(Instance unit)
    {
        if (!_objectOf.TryGetValue(unit, out int obj))
            _objectOf[unit] = obj = unit.Get("object_index").AsInt;
        return obj;
    }

    // A unit set as the host has it: its AI off, its health, state and animation - what's changed since last (all of it
    // the first time: last null).
    private void Set(Instance unit, JsonObject u, JsonObject? last)
    {
        bool Changed(string key) => last == null || !JsonNode.DeepEquals(last[key], u[key]);
        // (Every time: the game may switch it back on.)
        unit["ai_is_on"] = false;
        if (Changed("hp"))
            unit["HP"] = Math.Max(1, Number(u["hp"]));
        if (Changed("state"))
            unit["state"] = u["state"]?.GetValue<string>() ?? "";
        if (Changed("sleeping"))
            unit["is_sleeping"] = GmValue.FromJsonNode(u["sleeping"]);
        if (Changed("neutral"))
            unit["is_neutral"] = GmValue.FromJsonNode(u["neutral"]);
        if (Changed("look") && u["look"] is JsonObject look)
            foreach (var (key, value) in look)
                if (!unit.Get(key).IsUndefined && Number(value) is var sprite && (sprite < 0 || IsAssetSprite((int)sprite)))
                    unit[key] = sprite;
        if (Changed("npcName"))
            unit["name"] = GmValue.FromJsonNode(u["npcName"]);
        if (Changed("flip"))
        {
            double scaleX = Math.Abs(unit.Get("image_xscale").AsReal);
            unit["image_xscale"] = u["flip"]?.GetValue<bool>() == true ? -scaleX : scaleX;
        }
        // The unit's current animation is part of its state: idle / fight / work / sleep sprites may differ between two
        // games' spawns even when the object and AI state match.
        if ((Changed("sprite") | Changed("lifeAnimation")) && Number(u["sprite"]) is var shown && IsAssetSprite((int)shown))
        {
            unit["sprite_index"] = shown;
            // (As scr_npc_change_animation: the sprite it renders, and whether it's in its normal unit animation - a
            // work or sleep pose isn't.)
            unit["spr"] = shown;
            unit["is_life"] = GmValue.FromJsonNode(u["lifeAnimation"]);
            // (Its highlight sprite, which goes by the sprite it shows.)
            Game.CallScript("scr_set_hl", unit);
        }
        if (Changed("frame"))
            unit["image_index"] = Number(u["frame"]);
        if (Changed("speed"))
            unit["image_speed"] = Number(u["speed"]);
        if (Changed("angle"))
            unit["image_angle"] = Number(u["angle"]);
        if (Changed("alpha"))
            unit["image_alpha"] = Number(u["alpha"]);
    }

    private bool IsAssetSprite(int sprite)
    {
        if (sprite < 0)
            return false;
        if (!_assetSprites.TryGetValue(sprite, out bool asset))
            _assetSprites[sprite] = asset = Game.CallBuiltin("sprite_exists", sprite).AsBool
                && Gm.AssetGetIndex(Game.CallBuiltin("sprite_get_name", sprite).AsString) == sprite;
        return asset;
    }

    private static double Number(JsonNode? node) => GmValue.FromJsonNode(node).AsReal;
    private static int Int(JsonNode? node) => (int)Number(node);
}
