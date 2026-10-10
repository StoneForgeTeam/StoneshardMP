using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

// The turn a follower's ground effects count down in, and the end of the units' turns: what's made in them is the game's
// own (a cloud spreading, a fire catching), not our player's.
[assembly: HookScript(nameof(Scripts.scr_global_turn))]
[assembly: HookScript(nameof(Scripts.scr_unitTurnBreak))]

namespace StoneshardMP.Features.GroundEffects;

// Ground effects where players are together - fire, acid, smoke and poison clouds, blood and lava (the game's tile marks:
// c_tile_mark, saved with a place as scr_locationRoomEntityMarksSaveDataGet keeps them) - follow the area owner's.
// - The owner sends its list when it changes (and every few seconds); each follower makes it so: what's missing is made,
//   what the owner hasn't got is taken away, and the rest take the owner's turns left. A follower's copies don't count
//   down on their own (each turn's tick is undone), so one never ends before the owner's does: it ends when the owner's
//   starts its end animation, or is gone from the list.
// - What a follower's own player makes (a fire bomb, an acid flask, a smoke bomb's cloud) goes to the owner, which makes
//   it there. Made means made outside the world's turns: a follower's units don't act, so what appears in its game outside
//   them is its player's doing. What's made during them (a cloud spreading, a fire catching) is the owner's to say.
// - Copies don't spread (a cloud's alarm 2 - the cloud each copy makes is on the list already).
// - The place's own (a dungeon's lava, a room's braziers: "static") are in every game already, and aren't sent.
// Each game's ground effects hurt its own player and the units in it; the owner's units are the real ones (AreaUnits).
public sealed class GroundEffectSync
{
    // (Their parents: what's under each is synced too - c_skill_aura_smoke is every cloud, smoke and poison.)
    private static readonly GameObjectId[] Kinds =
    {
        GameObjectId.o_p_fire, GameObjectId.o_inferno_tile, GameObjectId.o_bone_fire, GameObjectId.o_acidpool,
        GameObjectId.c_skill_aura_smoke, GameObjectId.o_blood_puddle, GameObjectId.o_lava_tile,
    };
    // (The tick each kind's turn runs - user event 0; a child's may call its parent's, so each copy's is undone once a turn.)
    private static readonly string[] TurnEvents =
    {
        "c_tile_mark", "o_p_fire", "o_p_fire_burning_offering", "o_inferno_tile", "o_lava_tile", "c_skill_aura_smoke",
    };
    private const int Interval = 6;
    private const long PendingMs = 2500, RetryMs = 1000, RefreshMs = 3000;

    private readonly Session _session;
    private readonly AreaOwnership _ownership;
    private readonly Func<bool> _inSharedWorld;
    // Made by syncing, here: on a follower, the owner's; on the owner, a follower's. They don't spread.
    private readonly HashSet<Instance> _copies = new();
    // Follower: made by our player, not sent yet.
    private readonly HashSet<Instance> _ours = new();
    // Follower: sent to the owner, by key - kept while it isn't on its list yet.
    private readonly Dictionary<string, long> _pending = new();
    // When each key was last made from the list (not again straight away: one may destroy itself as it's made).
    private readonly Dictionary<string, long> _made = new();
    // Copies whose turn has been undone this turn.
    private readonly HashSet<Instance> _heldThisTurn = new();
    private Dictionary<string, GroundEffectState>? _snapshot;
    private string? _place;
    private int _owner = -1, _worldTurn, _frame;
    private bool _making, _owed;
    private string _others = "", _last = "";
    private long _sent, _asked;

    public GroundEffectSync(ModContext context, Session session, AreaOwnership ownership, Func<bool> inSharedWorld)
    {
        _session = session;
        _ownership = ownership;
        _inSharedWorld = inSharedWorld;
        session.On<GroundEffectsPacket>(ReceiveList);
        session.On<GroundEffectChangePacket>(ReceiveChange);
        Scripts.scr_global_turn.Before(context, _ =>
        {
            _worldTurn++;
            _heldThisTurn.Clear();
            return false;
        });
        Scripts.scr_global_turn.After(context, _ => _worldTurn = Math.Max(0, _worldTurn - 1));
        Scripts.scr_unitTurnBreak.Before(context, _ =>
        {
            _worldTurn++;
            return false;
        });
        Scripts.scr_unitTurnBreak.After(context, _ => _worldTurn = Math.Max(0, _worldTurn - 1));
        // (Every tile mark's Create runs c_tile_mark's: one our player made, on a follower.)
        context.OnCode("gml_Object_c_tile_mark_Create_0", after: (self, _) => Created(self));
        foreach (string obj in TurnEvents)
            context.OnCode($"gml_Object_{obj}_Other_10", before: (self, _) => { HoldTurn(self); return false; });
        context.OnCode("gml_Object_c_skill_aura_smoke_Alarm_2", before: (self, _) => _copies.Contains(self.Persist()));
    }

    /// <summary>Dev tools: a line on where it's got to.</summary>
    public string DevSummary => $"copies {_copies.Count}, ours unsent {_ours.Count}, pending {_pending.Count}, owner list "
        + (_snapshot == null ? "none yet" : _snapshot.Count.ToString()) + (_worldTurn > 0 ? ", in a world turn" : "");

    public void Clear()
    {
        _place = null;
        _owner = -1;
        _copies.Clear();
        _ours.Clear();
        _pending.Clear();
        _made.Clear();
        _heldThisTurn.Clear();
        _snapshot = null;
        _others = _last = "";
        _sent = _asked = 0;
        _owed = true;
    }

    private bool Ready => _session.Connected && _inSharedWorld() && Gm.InGame && !Rooms.IsChanging
        && _ownership.Place == OurPlayer.State()?.Place && _ownership.Role != AreaRole.Alone;

    private void Prepare()
    {
        if (_place == _ownership.Place && _owner == _ownership.Owner)
            return;
        // (A new owner in the same place: what we made is still ours; its list is asked for afresh.)
        if (_place != _ownership.Place)
            Clear();
        _place = _ownership.Place;
        _owner = _ownership.Owner;
        _snapshot = null;
        _pending.Clear();
        _others = _last = "";
        _owed = true;
    }

    // Follower: a tile mark made outside the world's turns, and not by us - our player's.
    private void Created(Instance self)
    {
        // (Not before this place is ours to watch: what its room makes as it loads is its save's, not our player's.)
        if (_making || _worldTurn > 0 || !Ready || _ownership.Role != AreaRole.Follower || _place != _ownership.Place)
            return;
        int obj = self.Get("object_index").AsInt;
        if (Kinds.Any(k => obj == (int)k || Gm.ObjectIsAncestor(obj, (int)k)))
            _ours.Add(self.Persist());
    }

    // A copy's turn: what the tick takes off is put back first, once a turn (the owner's list says when it ends).
    private void HoldTurn(Instance self)
    {
        if (_ownership.Role != AreaRole.Follower)
            return;
        Instance item = self.Persist();
        if (!_copies.Contains(item) || !_heldThisTurn.Add(item))
            return;
        if (item.Get("duration") is { Kind: GmKind.Real } duration && duration.AsReal > 0)
            item.Set("duration", duration.AsReal + 1);
        if (item.Get("activation_duration") is { Kind: GmKind.Real } activation)
            item.Set("activation_duration", activation.AsReal + 1);
    }

    public void Tick()
    {
        if (!Ready)
        {
            if (!_session.Connected || !_inSharedWorld() || !Gm.InGame)
                Clear();
            return;
        }
        Prepare();
        if (++_frame % Interval != 0)
            return;
        var now = Read();
        long time = Environment.TickCount64;
        if (_ownership.Role == AreaRole.Owner)
        {
            string others = string.Join(",", _ownership.Others.OrderBy(s => s));
            string json = JsonSerializer.Serialize(now.Values.Select(v => v.State).OrderBy(v => v.Key).ToArray());
            if (_owed || others != _others || json != _last || time - _sent >= RefreshMs)
            {
                byte[] data = JoinCompression.Compress(json);
                foreach (int slot in _ownership.Others)
                    _session.Send(new GroundEffectsPacket(_place!, data), slot);
                _last = json;
                _others = others;
                _sent = time;
                _owed = false;
            }
        }
        else
        {
            foreach (var (key, entry) in now)
            {
                if (!_ours.Remove(entry.Instance) || _snapshot?.ContainsKey(key) == true)
                    continue;
                _pending[key] = time;
                _session.Send(new GroundEffectChangePacket(_place!, GroundEffectChangeKind.Added, entry.State), _owner);
            }
            if (_snapshot != null)
                Reconcile(now, time);
            else if (time - _asked >= RetryMs)
            {
                _asked = time;
                _session.Send(new GroundEffectChangePacket(_place!, GroundEffectChangeKind.Request, default), _owner);
            }
        }
        _copies.RemoveWhere(i => i.IsGone);
        _ours.RemoveWhere(i => i.IsGone);
    }

    // The ground effects here, by key (the place's own left out).
    private Dictionary<string, (Instance Instance, GroundEffectState State)> Read()
    {
        var result = new Dictionary<string, (Instance, GroundEffectState)>();
        foreach (GameObjectId kind in Kinds)
        {
            foreach (Instance item in Instances.All(kind, includeCulled: true))
            {
                Awake(item, () =>
                {
                    if (item.Get("persistent").AsBool || item.Get("roomEntityType") is { Kind: GmKind.String } type && type.AsString == "static")
                        return false;
                    var state = new GroundEffectState(Gm.ObjectGetName(item.Get("object_index").AsInt),
                        item.Get("x").AsReal, item.Get("y").AsReal, Number(item, "duration"), item.Get("is_execute").AsBool,
                        Number(item, "activation_duration"), item.Get("is_activate").AsBool);
                    result[state.Key] = (item.Persist(), state);
                    return true;
                });
            }
        }
        return result;
    }

    private void ReceiveList(RemotePlayer from, GroundEffectsPacket packet)
    {
        if (!Ready || _ownership.Role != AreaRole.Follower || from.Slot != _ownership.Owner || packet.Place != _ownership.Place)
            return;
        Prepare();
        var states = JsonSerializer.Deserialize<GroundEffectState[]>(JoinCompression.Decompress(packet.Data));
        if (states == null || states.Any(s => !s.Valid))
            return;
        var list = new Dictionary<string, GroundEffectState>();
        foreach (var state in states)
            list[state.Key] = state;
        // (Applied on the next tick, after what we made this frame is sent: a list can't take away what's just been made.)
        _snapshot = list;
    }

    private void Reconcile(Dictionary<string, (Instance Instance, GroundEffectState State)> now, long time)
    {
        foreach (var (key, entry) in now)
        {
            if (_snapshot!.TryGetValue(key, out var wanted))
            {
                _pending.Remove(key);
                _copies.Add(entry.Instance);
                Update(entry.Instance, entry.State, wanted);
            }
            // (Ours, not on the owner's list yet; or one ending of itself - its end animation playing.)
            else if ((_pending.TryGetValue(key, out long since) && time - since <= PendingMs) || entry.State.Executing)
                continue;
            else
                Remove(entry.Instance);
        }
        foreach (var (key, state) in _snapshot!)
        {
            // (One ending on the owner that we no longer have: let it be.)
            if (now.ContainsKey(key) || state.Executing || (_made.TryGetValue(key, out long when) && time - when < RetryMs))
                continue;
            _made[key] = time;
            Create(state);
        }
        foreach (string key in _pending.Keys.Where(k => time - _pending[k] > PendingMs).ToList())
            _pending.Remove(key);
    }

    private void ReceiveChange(RemotePlayer from, GroundEffectChangePacket packet)
    {
        if (!Ready || _ownership.Role != AreaRole.Owner || packet.Place != _ownership.Place || !_ownership.Others.Contains(from.Slot))
            return;
        Prepare();
        _owed = true;
        if (packet.Kind != GroundEffectChangeKind.Added || !packet.Effect.Valid)
            return;
        if (!Read().ContainsKey(packet.Effect.Key))
            Create(packet.Effect);
    }

    // One made from another game's: its Create as the game's own (the place's dynamic entities), then its state.
    private void Create(GroundEffectState state)
    {
        int obj = Gm.AssetGetIndex(state.Object);
        if (obj < 0 || !Game.CallBuiltin("object_exists", obj).AsBool || !Kinds.Any(k => obj == (int)k || Gm.ObjectIsAncestor(obj, (int)k)))
            return;
        GmValue previousType = Game.Global["locationRoomEntityType"];
        Instance made;
        _making = true;
        try
        {
            Game.Global["locationRoomEntityType"] = "dynamic";
            made = Gm.Create<GameInstance>(state.X, state.Y, 0, (GameObjectId)obj).Instance.Persist();
        }
        finally
        {
            Game.Global["locationRoomEntityType"] = previousType;
            _making = false;
        }
        // (Its Create may have put itself out: a fire where there's one already, a cloud on a wall.)
        if (made.IsNone || !made.Exists)
            return;
        _copies.Add(made);
        Update(made, default, state);
    }

    // A copy given the owner's turns left, its ending, and a cloud's thickening.
    private static void Update(Instance item, GroundEffectState had, GroundEffectState wanted)
        => Awake(item, () =>
        {
            if (had.Duration != wanted.Duration && item.Get("duration").Kind == GmKind.Real)
                item.Set("duration", wanted.Duration);
            if (wanted.Executing && !had.Executing)
                item.Set("is_execute", true);
            if (item.Get("activation_duration").Kind == GmKind.Real)
            {
                if (had.Activation != wanted.Activation)
                    item.Set("activation_duration", wanted.Activation);
                // (A cloud thickening: user event 3 - it puts out fire under it and blocks sight.)
                if (wanted.Active && !item.Get("is_activate").AsBool)
                    Game.CallBuiltinAs("event_user", item, item, 3);
            }
            return true;
        });

    private void Remove(Instance item)
    {
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
