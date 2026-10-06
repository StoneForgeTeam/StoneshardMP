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

// An area's units - NPCs, animals, enemies - are its owner's where players share a place (AreaOwnership: whoever got
// there first, not always the host): the owner sends its roster every few frames (Snapshot), and a follower there makes
// its own match it (Apply): each of the owner's units bound to its twin here or made, kept as the owner has it (cell,
// health, state, animation, the effects on it), its own AI off, and anything the owner didn't send removed. What a
// follower's own actions do to its copies - a knockback, a stun - goes to the owner (CombatSync), which does it to the
// real ones: until the roster has caught up with it (Moved, EffectsChanged - a moment), the copy isn't put back as the
// roster has it.
// When we stop following - the owner's gone, and we run the place now - our copies are handed back: their AI on and
// their turns ours again (legacy: scr_mp_units_release). Left as they were, they'd stand frozen: no AI, out of our
// turns. Following a new owner, ours are bound to its units afresh (the same units, matched again by object and cell).
// (Legacy: scr_mp_area_unit_snapshot, scr_mp_area_unit_apply.)
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
    private readonly AreaOwnership _ownership;
    private readonly Func<int> _playerObject;
    private int _frame;
    // Whether a sprite is one of the game's own (the same id in every game) - a sprite made at run time isn't.
    private readonly Dictionary<int, bool> _assetSprites = new();
    // Owner: our units' sync ids (what they go by on the wire).
    private readonly Dictionary<Instance, long> _syncIds = new();
    private long _nextSyncId;
    // Follower: the owner's units bound to ours (by the owner's sync id), every unit of ours ever bound (an unbound twin is
    // looked for among the rest), and which we were sent in the latest roster.
    private readonly Dictionary<long, Instance> _bound = new();
    private readonly HashSet<Instance> _everBound = new();
    private readonly HashSet<Instance> _current = new();
    // The place those are for: a new place starts them over (the units of the last one are gone).
    private string? _place;
    // Follower: what was last applied to each of our units (the owner's entry for it) - only what's changed since is
    // written, each write being a call into the game; each unit's object, read once; the size of the player's list of
    // units to run each turn, after ours were last taken out of it.
    private readonly Dictionary<Instance, JsonObject> _applied = new();
    private readonly Dictionary<Instance, int> _objectOf = new();
    private int _turnsSize = -1;
    // Follower: the roster being applied, a few units a frame (UnitsPerFrame) rather than all at once - one that comes
    // meanwhile waits for it to finish (the latest one), so a pass always gets to its end, where what the owner didn't
    // send is removed (a big place's roster takes longer than the owner takes to send the next: starting over each time,
    // that never happened, and a twin made beside one of ours left both); where it's got to, the units it's bound so far,
    // and whether any is new.
    private const int UnitsPerFrame = 12;
    private JsonObject[]? _roster, _waiting;
    private int _next;
    private readonly HashSet<Instance> _pass = new();
    private bool _passBound;
    // Follower: which of our units are big (more than one cell - read once), and the units not yet bound with their object
    // and cell, worked out once a pass when a unit needs its twin.
    private readonly Dictionary<Instance, bool> _poly = new();
    private Dictionary<(int Obj, Cell Cell), List<Instance>>? _unbound;
    // Follower: copies our own actions moved (the cell, and until when the roster may still have them where they were),
    // and put effects on (until when the roster may not have them yet).
    private const long CatchUpMs = 1500;
    private readonly Dictionary<Instance, (Cell Cell, long Until)> _movedByUs = new();
    private readonly Dictionary<Instance, long> _effectsByUs = new();

    public AreaUnits(ModContext context, Session session, AreaOwnership ownership, Func<int> playerObject)
    {
        _context = context;
        _session = session;
        _ownership = ownership;
        _playerObject = playerObject;
        session.On<AreaUnitsPacket>(Receive);
        ownership.Changed += RoleChanged;
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
        _waiting = null;
        _pass.Clear();
        _poly.Clear();
        _unbound = null;
        _movedByUs.Clear();
        _effectsByUs.Clear();
    }

    /// <summary>Follower: whether the owner's roster is being applied to our copies (moved, set, their effects matched) -
    /// what that does to them isn't ours to send the owner.</summary>
    public bool Applying { get; private set; }

    /// <summary>Follower: our own action moved one of our copies of the owner's units to this cell (the owner's been told):
    /// it isn't put back where the roster has it until the roster's caught up, or a moment's passed.</summary>
    public void Moved(Instance unit, Cell cell) => _movedByUs[unit.Persist()] = (cell, Environment.TickCount64 + CatchUpMs);

    /// <summary>Follower: our own action put an effect on one of our copies (the owner's been told): its effects aren't
    /// matched to the roster's for a moment, while the owner's catch up.</summary>
    public void EffectsChanged(Instance unit) => _effectsByUs[unit.Persist()] = Environment.TickCount64 + CatchUpMs;

    /// <summary>Whether this unit is one of the owner's, from the latest roster (for the debug dump).</summary>
    public bool IsOwners(Instance unit) => _current.Contains(unit.Persist());

    /// <summary>Follower: the owner's sync id for one of our units bound to one of its; null if it isn't one.</summary>
    public long? OwnerIdOf(Instance unit)
    {
        unit = unit.Persist();
        foreach (var (hostId, bound) in _bound)
            if (bound.Persist().Equals(unit))
                return hostId;
        return null;
    }

    /// <summary>Follower: our unit bound to the owner's with this sync id; none if there's none.</summary>
    public Instance LocalOf(long hostId)
        => _bound.TryGetValue(hostId, out Instance unit) && unit.Exists ? unit : default;

    /// <summary>Owner: the sync id one of our units goes by (what a follower calls it); null if it hasn't one yet.</summary>
    public long? SyncIdOf(Instance unit)
    {
        unit = unit.Persist();
        foreach (var (synced, id) in _syncIds)
            if (synced.Persist().Equals(unit))
                return id;
        return null;
    }

    /// <summary>Owner: our unit going by this sync id (what a follower calls it); none if there's none here now.</summary>
    public Instance UnitOf(long syncId)
    {
        foreach (var (unit, id) in _syncIds)
            if (id == syncId)
                return unit.Exists ? unit : default;
        return default;
    }

    public void Tick()
    {
        // (Following: the roster we're applying, a few more units of it - or the one that came meanwhile, from its start.)
        if (_roster == null && _waiting != null)
            StartPass(_waiting);
        if (_roster != null && _ownership.Role == AreaRole.Follower)
            Profiler.Measure(_context, "area units applied", ApplySome);
        if (!_session.Connected || !Gm.InGame || _ownership.Role != AreaRole.Owner || _ownership.Place is not { } place
            || ++_frame % 6 != 0)
            return;
        NewPlace(place);
        string snapshot = Snapshot();
        foreach (int follower in _ownership.Others)
            // A complete room roster is often larger than LiteNetLib's 1,020-byte sequenced-packet cap.
            // ReliableOrdered fragments it safely and also ensures a follower never applies a newer roster
            // before an older snapshot that created one of its bindings.
            _session.Send(new AreaUnitsPacket(place, snapshot), follower, DeliveryMethod.ReliableOrdered);
    }

    private void Receive(RemotePlayer sender, AreaUnitsPacket packet)
    {
        if (_ownership.Role != AreaRole.Follower || sender.Slot != _ownership.Owner || packet.Place != _ownership.Place)
            return;
        if (OurPlayer.State()?.Place != packet.Place)
            return;
        NewPlace(packet.Place);
        // (Taken in the next frames, from its start - once the one being applied is done: the latest one waits.)
        if (packet.Snapshot.Length > 0 && JsonNode.Parse(packet.Snapshot) is JsonArray roster)
        {
            _waiting = roster.OfType<JsonObject>().ToArray();
            if (_roster == null)
                StartPass(_waiting);
        }
    }

    private void StartPass(JsonObject[] roster)
    {
        _roster = roster;
        _waiting = null;
        _next = 0;
        _pass.Clear();
        _passBound = false;
        _unbound = null;
    }

    // Our role in the place changed. No longer following (alone, or the owner now): our copies are ours to run again.
    // Following someone else now: ours are bound to their units afresh. Owning now: our units get ids of their own.
    private void RoleChanged(AreaRole was, int wasOwner)
    {
        AreaRole now = _ownership.Role;
        if (was == AreaRole.Follower && now != AreaRole.Follower)
            Release();
        else if (now == AreaRole.Follower && _ownership.Owner != wasOwner)
            ForgetBindings();
        if (now == AreaRole.Owner && was != AreaRole.Owner)
            _syncIds.Clear();
    }

    // Our copies of the owner's units handed back: their AI on, their turns ours again - the units are the same, only
    // who runs them has changed - and what we kept of the owner's roster forgotten.
    private void Release()
    {
        var copies = _bound.Values.Concat(_everBound).Where(unit => unit.Exists).Distinct().ToList();
        if (Gm.InGame && copies.Count > 0 && Units.ReturnToTurns(copies))
            _context.Log($"No longer following here: {copies.Count} unit(s) ours to run again");
        ForgetBindings();
    }

    // The owner's units we'd bound and applied forgotten (our units stay: matched again by object and cell).
    private void ForgetBindings()
    {
        _bound.Clear();
        _everBound.Clear();
        _current.Clear();
        _applied.Clear();
        _turnsSize = -1;
        _roster = null;
        _waiting = null;
        _pass.Clear();
        _unbound = null;
        _movedByUs.Clear();
        _effectsByUs.Clear();
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
        _waiting = null;
        _pass.Clear();
        _poly.Clear();
        _unbound = null;
        _movedByUs.Clear();
        _effectsByUs.Clear();
    }

    // Owner: every real unit in the room - NPCs, animals, enemies; not other players' - as JSON.
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
            var (x, y) = Units.CellOf(unit);
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
                ["fx"] = EffectsOf(unit),
            });
        }
        return units.ToJsonString();
    }

    // Follower: the owner's roster made ours, UnitsPerFrame units a frame - each of its units bound to our twin (same
    // object, on the same cell, never bound before) or made, set as the owner has it, and moved through the game's grids.
    // Once the whole roster's done: ours taken out of our turns (the owner runs them), and anything the owner didn't send
    // removed, without its Destroy event (no loot, corpse or kill credit here).
    private void ApplySome()
    {
        if (_roster == null)
            return;
        if (!Gm.InstanceExists(GameObjectId.o_player) || Units.Current() is not { } grids)
        {
            _roster = null;
            return;
        }
        Applying = true;
        try
        {
            for (int done = 0; done < UnitsPerFrame && _next < _roster.Length; done++)
                Profiler.Measure(_context, "area units: unit", () => ApplyUnit(_roster[_next++], grids));
            if (_next < _roster.Length)
                return;
            _roster = null;
            Profiler.Measure(_context, "area units: tidy", Tidy);
        }
        finally { Applying = false; }
    }

    private void ApplyUnit(JsonObject u, Units.Grids grids)
    {
        long hostId = (long)Number(u["id"]);
        int obj = Int(u["obj"]);
        var cell = new Cell(Int(u["x"]), Int(u["y"]));
        Instance unit = _bound.GetValueOrDefault(hostId);
        if (unit.IsNone || !unit.Exists)
        {
            unit = Profiler.Measure(_context, "area units: bind", () => Bind(obj, cell));
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
        long now = Environment.TickCount64;
        // Where the owner has it - wherever ours is now, not only when the owner's moved it: ours may have been moved here
        // (a knockback that didn't happen there). Unless we moved it, and the roster's not caught up yet.
        bool hold = false;
        if (_movedByUs.TryGetValue(unit, out var moved))
        {
            if (moved.Cell == cell || now >= moved.Until)
                _movedByUs.Remove(unit);
            else
                hold = true;
        }
        if (!hold && Units.CellOf(unit) != cell)
        {
            if (!_poly.TryGetValue(unit, out bool poly))
                _poly[unit] = poly = unit.Get("is_poly_cell").AsBool;
            Units.Move(unit, cell, grids, poly);
        }
        // Its effects as the owner has them - when they've changed there, or once ours have been left alone long enough
        // for the owner to have what we did.
        bool effectsDue = last == null || !JsonNode.DeepEquals(last["fx"], u["fx"]);
        if (_effectsByUs.TryGetValue(unit, out long until))
        {
            effectsDue = now >= until;
            if (effectsDue)
                _effectsByUs.Remove(unit);
        }
        if (effectsDue && u["fx"] is JsonArray effects)
            MatchEffects(unit, effects);
        _applied[unit] = u;
    }

    // Owner: the effects on a unit that show (EffectKind), as [name, duration] pairs.
    private JsonArray EffectsOf(Instance unit)
    {
        var effects = new JsonArray();
        foreach (var effect in ShownEffects(unit))
            effects.Add(new JsonArray(effect.Name, effect.Duration));
        return effects;
    }

    // The effects on a unit that show - not the game's invisible workings, which each game makes for its own units alike.
    private static List<GameEffect> ShownEffects(Instance unit) => UnitEffects.On(unit).Where(effect => effect.Shown).ToList();

    // Follower: a copy's effects made the owner's - each of the owner's there with its duration (made as the game makes one,
    // from the copy itself, if it isn't), and any the owner hasn't taken off.
    private void MatchEffects(Instance unit, JsonArray hosts)
    {
        var ours = ShownEffects(unit);
        foreach (JsonNode? entry in hosts)
        {
            if (entry is not JsonArray { Count: 2 } pair || pair[0]?.GetValue<string>() is not { } name)
                continue;
            double duration = Number(pair[1]);
            int at = ours.FindIndex(e => e.Name == name);
            if (at >= 0)
            {
                if (ours[at].Duration != duration)
                {
                    Instance effect = ours[at].Instance;
                    effect["duration"] = duration;
                }
                ours.RemoveAt(at);
                continue;
            }
            Instance made = UnitEffects.Create(name, unit, duration);
            if (!made.IsNone && made.Exists)
                made["duration"] = duration;
        }
        foreach (var effect in ours)
            if (effect.Instance.Exists)
                effect.Instance.Destroy();
    }

    // One of the owner's units we have none bound to: our twin (same object, never bound) on the same cell - or, if
    // there's none, the nearest within TwinReach cells (one that's wandered a step or two here meanwhile: made anew beside
    // it, the two stood side by side) - from a table of our unbound units made once a pass; or a new one.
    private const int TwinReach = 4;
    private Instance Bind(int obj, Cell cell)
    {
        if (_unbound == null)
        {
            int playerObject = _playerObject();
            _unbound = new();
            foreach (Instance local in Instances.All(GameObjectId.o_enemy))
            {
                if (_everBound.Contains(local) || ObjectOf(local) == playerObject)
                    continue;
                var key = (ObjectOf(local), Units.CellOf(local));
                if (!_unbound.TryGetValue(key, out var list))
                    _unbound[key] = list = new();
                list.Add(local);
            }
        }
        if (_unbound.TryGetValue((obj, cell), out var twins))
            while (twins.Count > 0)
            {
                Instance twin = twins[^1];
                twins.RemoveAt(twins.Count - 1);
                if (!_everBound.Contains(twin) && twin.Exists)
                    return twin;
            }
        // (Nearest first, by cells: the greater of the two distances.)
        (int Distance, Cell Cell, Instance Unit)? near = null;
        foreach (var ((o, at), list) in _unbound)
        {
            if (o != obj || list.Count == 0)
                continue;
            int distance = Math.Max(Math.Abs(at.X - cell.X), Math.Abs(at.Y - cell.Y));
            if (distance <= TwinReach && (near == null || distance < near.Value.Distance))
                near = (distance, at, list[^1]);
        }
        if (near is var (_, nearCell, nearest) && nearest.Exists && !_everBound.Contains(nearest))
        {
            _unbound[(obj, nearCell)].Remove(nearest);
            return nearest;
        }
        return Units.Create(obj, cell);
    }

    // A whole roster done: ours out of our turns - between turns only, the turn may be walking the list; only when a
    // unit's new to us or the list has changed since - and the units the owner didn't send gone.
    private void Tidy()
    {
        _current.Clear();
        _current.UnionWith(_pass);
        int turns = Units.TurnsCount();
        if (_passBound || turns != _turnsSize)
        {
            Units.RemoveFromTurns(_current.Contains);
            _turnsSize = Units.TurnsCount();
        }
        int playerObject = _playerObject();
        foreach (Instance gone in Instances.All(GameObjectId.o_enemy).Where(unit => !_current.Contains(unit) && ObjectOf(unit) != playerObject))
        {
            Units.Remove(gone);
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

    // A unit set as the owner has it: its AI off, its health, state and animation - what's changed since last (all of it
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
            _assetSprites[sprite] = asset = Draw.SpriteExists(sprite) && Gm.AssetGetIndex(Draw.SpriteName(sprite)) == sprite;
        return asset;
    }

    private static double Number(JsonNode? node) => GmValue.FromJsonNode(node).AsReal;
    private static int Int(JsonNode? node) => (int)Number(node);
}
