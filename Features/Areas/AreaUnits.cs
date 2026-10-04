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

    public AreaUnits(Session session, Func<int> playerObject)
    {
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
    }

    /// <summary>Whether this unit is one of the host's, from the latest roster (for the debug dump).</summary>
    public bool IsHosts(Instance unit) => _current.Contains(unit.Persist());

    public void Tick()
    {
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
        Apply(packet.Snapshot);
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

    // Client: the host's roster made ours - each of its units bound to our twin (same object, on the same cell, never
    // bound before) or made, set as the host has it, and moved through the game's grids; ours taken out of our turns
    // (the host runs them); anything the host didn't send removed, without its Destroy event (no loot, corpse or kill
    // credit here).
    private void Apply(string snapshot)
    {
        if (!Gm.InstanceExists(GameObjectId.o_player) || !Gm.InstanceExists(GameObjectId.o_controller) || snapshot.Length == 0)
            return;
        if (JsonNode.Parse(snapshot) is not JsonArray roster)
            return;
        int playerObject = _playerObject();
        var here = Instances.All(GameObjectId.o_enemy).Where(unit => unit.Get("object_index").AsInt != playerObject).ToList();
        _current.Clear();
        foreach (JsonObject u in roster.OfType<JsonObject>())
        {
            long hostId = (long)Number(u["id"]);
            int obj = Int(u["obj"]), x = Int(u["x"]), y = Int(u["y"]);
            Instance unit = _bound.GetValueOrDefault(hostId);
            if (unit.IsNone || !unit.Exists)
            {
                unit = here.FirstOrDefault(local => !_everBound.Contains(local) && !_current.Contains(local)
                    && local.Get("object_index").AsInt == obj && UnitGrid.CellOf(local) == (x, y));
                if (unit.IsNone)
                    unit = UnitGrid.InstanceOf(Game.CallScript("scr_enemy_create", default, x * UnitGrid.Cell + 13, y * UnitGrid.Cell + 13, obj, false, false));
                if (unit.IsNone || !unit.Exists)
                    continue;
                _bound[hostId] = unit;
                _everBound.Add(unit);
            }
            _current.Add(unit);
            Set(unit, u);
            UnitGrid.Move(unit, x, y);
        }
        // (Out of our turns - between turns only: the turn may be walking the list.)
        UnitGrid.RemoveFromTurns(_current.Contains);
        foreach (Instance gone in here.Where(unit => !_current.Contains(unit)))
            UnitGrid.Remove(gone);
    }

    // A unit set as the host has it: its AI off, its health, state and animation.
    private void Set(Instance unit, JsonObject u)
    {
        unit["ai_is_on"] = false;
        unit["HP"] = Math.Max(1, Number(u["hp"]));
        unit["state"] = u["state"]?.GetValue<string>() ?? "";
        unit["is_sleeping"] = GmValue.FromJsonNode(u["sleeping"]);
        unit["is_neutral"] = GmValue.FromJsonNode(u["neutral"]);
        if (u["look"] is JsonObject look)
            foreach (var (key, value) in look)
                if (!unit.Get(key).IsUndefined && Number(value) is var sprite && (sprite < 0 || IsAssetSprite((int)sprite)))
                    unit[key] = sprite;
        unit["name"] = GmValue.FromJsonNode(u["npcName"]);
        double scaleX = Math.Abs(unit.Get("image_xscale").AsReal);
        unit["image_xscale"] = u["flip"]?.GetValue<bool>() == true ? -scaleX : scaleX;
        // The unit's current animation is part of its state: idle / fight / work / sleep sprites may differ between two
        // games' spawns even when the object and AI state match.
        if (Number(u["sprite"]) is var shown && IsAssetSprite((int)shown))
        {
            unit["sprite_index"] = shown;
            // (As scr_npc_change_animation: the sprite it renders, and whether it's in its normal unit animation - a
            // work or sleep pose isn't.)
            unit["spr"] = shown;
            unit["is_life"] = GmValue.FromJsonNode(u["lifeAnimation"]);
        }
        unit["image_index"] = Number(u["frame"]);
        unit["image_speed"] = Number(u["speed"]);
        unit["image_angle"] = Number(u["angle"]);
        unit["image_alpha"] = Number(u["alpha"]);
        Game.CallScript("scr_set_hl", unit);
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
