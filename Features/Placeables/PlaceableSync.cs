using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Placeables;

// The area owner's roster is authoritative, but followers may place and pack up
// objects. A spell stays real on its caster's game: replicas have collision and
// appearance, but never run caster effects or their own lifetime countdown.
public sealed class PlaceableSync
{
    private static readonly GameObjectId[] Kinds =
    {
        GameObjectId.o_campbed_crafted, GameObjectId.o_campfire_crafted,
        GameObjectId.o_runic_boulder, GameObjectId.o_stone_spikes_instance,
    };
    private readonly Session _session;
    private readonly AreaOwnership _ownership;
    private readonly Func<bool> _inSharedWorld;
    private readonly HashSet<Instance> _copies = new();
    private readonly Dictionary<string, int> _casters = new();
    private Dictionary<string, (Instance Instance, PlacedObjectState State)> _seen = new();
    private readonly Dictionary<string, long> _pending = new();
    private readonly Dictionary<string, long> _taken = new();
    // Owner: copies we took away ourselves (the rest that go were broken in our game). Follower: our spells the owner
    // said were broken, and when - not sent back to it as lost.
    private readonly HashSet<Instance> _removedByUs = new();
    private readonly Dictionary<string, long> _broken = new();
    private Dictionary<string, PlacedObjectState>? _snapshot;
    private string? _place;
    private int _owner = -1;
    private string _others = "", _last = "";
    private long _sent, _asked;
    private int _frame;
    private bool _owed;

    public PlaceableSync(ModContext context, Session session, AreaOwnership ownership, Func<bool> inSharedWorld)
    {
        _session = session;
        _ownership = ownership;
        _inSharedWorld = inSharedWorld;
        session.On<PlaceablesPacket>(ReceiveSnapshot);
        session.On<PlaceableChangePacket>(ReceiveChange);
        // User event 2 checks the caster / expires the construct. Only its real
        // caster-side instance runs it; the authoritative roster removes copies.
        foreach (string obj in new[] { "o_runic_boulder", "o_stone_spikes_instance" })
            context.OnCode($"gml_Object_{obj}_Other_12", before: (self, _) => _copies.Contains(self.Persist()));
        context.OnCode("gml_Object_o_runic_boulder_Alarm_1", before: (self, _) => _copies.Contains(self.Persist()));
    }

    /// <summary>Dev tools: a line on where it's got to.</summary>
    public string DevSummary => $"seen {_seen.Count}, copies {_copies.Count}, pending {_pending.Count}, taken {_taken.Count}, owner list "
        + (_snapshot == null ? "none yet" : _snapshot.Count.ToString());

    public void Clear()
    {
        _place = null;
        _owner = -1;
        _seen.Clear();
        _copies.Clear();
        _casters.Clear();
        _pending.Clear();
        _taken.Clear();
        _removedByUs.Clear();
        _broken.Clear();
        _snapshot = null;
        _last = _others = "";
        _sent = _asked = 0;
        _owed = true;
    }

    private bool Ready => _session.Connected && _inSharedWorld() && Gm.InGame && !Rooms.IsChanging
        && _ownership.Place == OurPlayer.State()?.Place && _ownership.Role != AreaRole.Alone;

    private void Prepare()
    {
        if (_place == _ownership.Place && _owner == _ownership.Owner)
            return;
        // On an ownership handoff in this room, retain which constructs are
        // replicas and who their real caster is. Clearing that would expire them.
        if (_place != _ownership.Place)
            Clear();
        _place = _ownership.Place;
        _owner = _ownership.Owner;
        _snapshot = null;
        _pending.Clear();
        _taken.Clear();
        _last = _others = "";
        _owed = true;
        _seen = Read();
    }

    public void Tick()
    {
        if (!Ready)
        {
            // Room transitions are temporary. Keep replicas marked until the
            // new room is ready; nobody should run their missing-caster effects.
            if (!_session.Connected || !_inSharedWorld() || !Gm.InGame)
                Clear();
            else if (!Rooms.IsChanging && _ownership.Role == AreaRole.Alone && _place == OurPlayer.State()?.Place)
            {
                // The source left and there is no longer an area owner. Camping
                // gear stays, but caster-less spell replicas must not be immortal.
                foreach (var entry in Read().Values.Where(v => v.State.Spell && _copies.Contains(v.Instance)).ToList())
                    Remove(entry.Instance);
            }
            return;
        }
        Prepare();
        if (++_frame % 6 != 0)
            return;
        var now = Read();
        if (_ownership.Role == AreaRole.Owner)
        {
            // A follower's spell broken here - an NPC smashed our copy of their boulder - is broken in their game too: as
            // a lost copy, they'd only send it back.
            foreach (var (key, entry) in _seen)
            {
                if (now.ContainsKey(key) || !entry.Instance.IsGone || _removedByUs.Contains(entry.Instance) || !_copies.Contains(entry.Instance)
                    || !entry.State.Spell || entry.State.Caster == _session.Slot || !_ownership.Others.Contains(entry.State.Caster))
                    continue;
                _session.Send(new PlaceableChangePacket(_place!, PlaceableChangeKind.Broken, entry.State), entry.State.Caster);
            }
            _removedByUs.RemoveWhere(i => i.IsGone);
            // A follower's spell source left the area: its replicas cannot live
            // forever waiting for a removal from a caster who has moved on.
            foreach (var entry in now.Values.ToList())
            {
                if (entry.State.Spell && _copies.Contains(entry.Instance)
                    && entry.State.Caster != _session.Slot && !_ownership.Others.Contains(entry.State.Caster))
                {
                    Remove(entry.Instance);
                    now.Remove(entry.State.Key);
                }
            }
            string others = string.Join(",", _ownership.Others.OrderBy(s => s));
            string json = JsonSerializer.Serialize(now.Values.Select(v => v.State).OrderBy(v => v.Key).ToArray());
            if (_owed || others != _others || json != _last || Environment.TickCount64 - _sent >= 3000)
            {
                byte[] data = JoinCompression.Compress(json);
                foreach (int slot in _ownership.Others)
                    _session.Send(new PlaceablesPacket(_place!, data), slot);
                _last = json;
                _others = others;
                _sent = Environment.TickCount64;
                _owed = false;
            }
        }
        else
        {
            long time = Environment.TickCount64;
            foreach (var (key, entry) in now)
            {
                if (_seen.ContainsKey(key) || _pending.ContainsKey(key) || _snapshot?.ContainsKey(key) == true)
                    continue;
                _pending[key] = time;
                // Prepare captured the arrival baseline: anything new since
                // then is ours, even on the first polling tick.
                _session.Send(new PlaceableChangePacket(_place!, PlaceableChangeKind.Added, entry.State), _owner);
            }
            foreach (var (key, entry) in _seen)
            {
                if (now.ContainsKey(key) || _taken.ContainsKey(key))
                    continue;
                _session.Send(new PlaceableChangePacket(_place!, PlaceableChangeKind.Removed, entry.State), _owner);
                _taken[key] = time;
                _copies.Remove(entry.Instance);
            }
            if (_snapshot != null)
                Reconcile(now);
            else if (time - _asked >= 1000)
            {
                _asked = time;
                _session.Send(new PlaceableChangePacket(_place!, PlaceableChangeKind.Request, default), _owner);
            }
        }
        _seen = Read();
        _copies.RemoveWhere(i => i.IsGone);
    }

    private Dictionary<string, (Instance Instance, PlacedObjectState State)> Read()
    {
        var result = new Dictionary<string, (Instance, PlacedObjectState)>();
        foreach (GameObjectId kind in Kinds)
        {
            foreach (Instance item in Instances.All(kind, includeCulled: true))
            {
                Awake(item, () =>
                {
                    var state = new PlacedObjectState(Gm.ObjectGetName(item.Get("object_index").AsInt),
                        item.Get("x").AsReal, item.Get("y").AsReal, Number(item, "timestamp"),
                        Number(item, "HP"), Number(item, "duration"), _session.Slot);
                    state = state with { Caster = _casters.GetValueOrDefault(state.Key, _session.Slot) };
                    // A genuine locally cast construct is the source, even if
                    // an older roster assigned this tile to a different caster.
                    if (state.Spell && !_copies.Contains(item)
                        && Instance.Of(item.Get("owner")).Persist().Equals(OurPlayer.Instance.Persist()))
                    {
                        state = state with { Caster = _session.Slot };
                        _casters[state.Key] = _session.Slot;
                    }
                    result[state.Key] = (item, state);
                    return true;
                });
            }
        }
        return result;
    }

    private void ReceiveSnapshot(RemotePlayer from, PlaceablesPacket packet)
    {
        if (!Ready || _ownership.Role != AreaRole.Follower || from.Slot != _ownership.Owner || packet.Place != _ownership.Place)
            return;
        Prepare();
        var states = JsonSerializer.Deserialize<PlacedObjectState[]>(JoinCompression.Decompress(packet.Data));
        if (states == null || states.Any(s => !s.Valid) || states.Select(s => s.Key).Distinct().Count() != states.Length)
            return;
        // Tick detects our own placements/removals before applying the roster,
        // so a snapshot cannot silently erase a placement made this frame.
        _snapshot = states.ToDictionary(s => s.Key);
    }

    private void Reconcile(Dictionary<string, (Instance Instance, PlacedObjectState State)> now)
    {
        long time = Environment.TickCount64;
        foreach (var (key, entry) in now)
        {
            if (_snapshot!.TryGetValue(key, out var wanted))
            {
                // Never turn the caster's real spell into a caster-less copy
                // because a delayed roster still describes the old object here.
                if (entry.State.Spell && !_copies.Contains(entry.Instance) && entry.State.Caster == _session.Slot)
                {
                    _casters[key] = _session.Slot;
                    _pending.Remove(key);
                    continue;
                }
                _casters[key] = wanted.Caster;
                _pending.Remove(key);
                // Only the caster's genuinely local spell remains real. An
                // unrelated spell loaded from our older save becomes a copy.
                if (!wanted.Spell || wanted.Caster != _session.Slot)
                    _copies.Add(entry.Instance);
                Update(entry.Instance, wanted);
            }
            else if (entry.State.Spell && !_copies.Contains(entry.Instance) && entry.State.Caster == _session.Slot
                && !(_broken.TryGetValue(key, out long broke) && time - broke < 3000))
            {
                // Its lifetime belongs to this caster, not an older owner
                // snapshot. Ask the owner to restore a lost replica instead.
                if (!_pending.TryGetValue(key, out long last) || time - last >= 1000)
                {
                    _pending[key] = time;
                    _session.Send(new PlaceableChangePacket(_place!, PlaceableChangeKind.Added, entry.State), _owner);
                }
            }
            else if (!_pending.TryGetValue(key, out long since) || time - since > 2500)
                Remove(entry.Instance);
        }
        foreach (var (key, state) in _snapshot!)
        {
            if (now.ContainsKey(key) || (_taken.TryGetValue(key, out long when) && time - when < 3000))
                continue;
            Create(state);
        }
        foreach (string key in _taken.Keys.Where(k => !_snapshot.ContainsKey(k)).ToList())
            _taken.Remove(key);
    }

    private void ReceiveChange(RemotePlayer from, PlaceableChangePacket packet)
    {
        if (packet.Kind == PlaceableChangeKind.Broken)
        {
            ReceiveBroken(from, packet);
            return;
        }
        if (!Ready || _ownership.Role != AreaRole.Owner || packet.Place != _ownership.Place || !_ownership.Others.Contains(from.Slot))
            return;
        Prepare();
        if (packet.Kind == PlaceableChangeKind.Request)
        {
            _owed = true;
            return;
        }
        if (!packet.Object.Valid || packet.Kind is not (PlaceableChangeKind.Added or PlaceableChangeKind.Removed))
            return;
        var now = Read();
        bool found = now.TryGetValue(packet.Object.Key, out var entry);
        if (packet.Kind == PlaceableChangeKind.Added)
        {
            if (!found)
                Create(packet.Object with { Caster = from.Slot });
        }
        else if (found)
        {
            // Spell replicas cannot delete another player's real spell.
            if (!entry.State.Spell || entry.State.Caster == from.Slot)
                Remove(entry.Instance);
        }
        _owed = true;
    }

    // Follower: the owner's copy of our spell was broken - ours breaks as the game breaks it (a boulder: its user event 3,
    // the dismissal, with its end animation and the caster's buff put right; anything else destroyed).
    private void ReceiveBroken(RemotePlayer from, PlaceableChangePacket packet)
    {
        if (!Ready || _ownership.Role != AreaRole.Follower || from.Slot != _ownership.Owner || packet.Place != _ownership.Place)
            return;
        if (!Read().TryGetValue(packet.Object.Key, out var entry) || _copies.Contains(entry.Instance))
            return;
        _broken[packet.Object.Key] = Environment.TickCount64;
        if (entry.Instance.IsCulled)
            Game.CallBuiltin("instance_activate_object", entry.Instance);
        if (entry.Instance.Get("object_index").AsInt == (int)GameObjectId.o_runic_boulder)
        {
            entry.Instance.Set("HP", 0);
            Game.CallBuiltinAs("event_user", entry.Instance, entry.Instance, 3);
        }
        else
            entry.Instance.Destroy();
    }

    private void Create(PlacedObjectState state)
    {
        int obj = Gm.AssetGetIndex(state.Object);
        if (!state.Valid || obj < 0 || !Game.CallBuiltin("object_exists", obj).AsBool)
            return;
        // Create events set up collision/light/sound; later alarms see owner=noone
        // on spell replicas, so they can't run caster damage or buffs.
        GmValue previousType = Game.Global["locationRoomEntityType"];
        Instance made;
        try
        {
            Game.Global["locationRoomEntityType"] = "dynamic";
            made = Gm.Create<GameInstance>(state.X, state.Y, 0, (GameObjectId)obj).Instance.Persist();
        }
        finally { Game.Global["locationRoomEntityType"] = previousType; }
        if (made.IsNone || !made.Exists)
            return;
        _copies.Add(made);
        _casters[state.Key] = state.Caster;
        if (!made.Get("HP").IsUndefined)
        {
            made.Set("HP", state.Health);
            made.Set("max_hp", state.Health);
        }
        Update(made, state);
    }

    private void Update(Instance item, PlacedObjectState state)
        => Awake(item, () =>
        {
            if (!item.Get("timestamp").IsUndefined)
                item.Set("timestamp", state.Timestamp);
            // Health is also synchronized by BreakableSync. Never overwrite a
            // freshly applied hit with an older placement snapshot.
            if (!item.Get("HP").IsUndefined && _copies.Contains(item))
                item.Set("HP", Math.Min(item.Get("HP").AsReal, state.Health));
            if (state.Spell && _copies.Contains(item))
            {
                item.Set("owner", -4);
                item.Set("duration", state.Duration);
            }
            return true;
        });

    private void Remove(Instance item)
    {
        // User event 3 creates this visual before destroying a real boulder.
        // Replicas must not run that event (it also changes the caster's buff),
        // but a plain Destroy otherwise skips the dismissal animation entirely.
        bool showEnd = !item.IsCulled && item.Exists
            && item.Get("object_index").AsInt == (int)GameObjectId.o_runic_boulder;
        Awake(item, () =>
        {
            if (showEnd)
                Gm.Create<GameInstance>(item.Get("x").AsReal, item.Get("y").AsReal, 0, GameObjectId.o_runicboulder_end);
            if (!item.Get("death_sound").IsUndefined)
                item.Set("death_sound", -4);
            if (!item.Get("play_death").IsUndefined)
                item.Set("play_death", false);
            if (!item.Get("last_attacker").IsUndefined)
                item.Set("last_attacker", -4);
            return true;
        });
        // Run Destroy for collision, wallgrid, sound and light cleanup, including
        // the culling controller's bookkeeping for off-screen objects.
        _removedByUs.Add(item);
        item.Destroy();
        _copies.Remove(item);
    }

    private static double Number(Instance item, string name)
        => item.Get(name) is { Kind: GmKind.Real } value ? value.AsReal : 0;

    private static T Awake<T>(Instance item, Func<T> action)
    {
        bool culled = item.IsCulled;
        if (culled)
            Game.CallBuiltin("instance_activate_object", item);
        try { return action(); }
        finally
        {
            if (culled)
                Game.CallBuiltin("instance_deactivate_object", item);
        }
    }
}
