using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

// The game script whose "dropped" log line marks the player's own drops (the patcher makes it hookable).
[assembly: HookScript(nameof(Scripts.scr_actionsLogItem))]

namespace StoneshardMP.Features.Loot;

// Live ground loot where players are together (legacy StoneshardMP's loot sync). The place's owner (AreaOwnership:
// whoever got there first) has the real loot, and the others follow:
// - The owner sends a snapshot of all its loot when a follower arrives (or asks), then every few frames what changed:
//   new loot at once (in the air or not), loot that left (picked up by anyone).
// - A follower takes it: twins from the same save are bound, the rest made from the owner's data, anything else
//   removed. Its own pickups go to the owner, which removes them too. Its own drops go to the owner, which makes
//   them; they come back in its changes, and the follower's own copy becomes the one synced item. Its own drops are
//   what it drops by itself, the ammo its arrows leave by their target, and the items it throws.
// - Throws are shared: loot goes out the moment it appears, with its throw if it's in the air, and the other game flies
//   its copy along the same arc to the same tile (GroundItem.Flight / Fly).
// Ground items come from StoneForge's GroundItems, off-screen (culled) ones too; the tables are by instance id.
// Loot sync shares no state with a place nobody else is in: there, WorldSync shares it as the location's save.
public sealed class LootSync
{
    // (Every this many frames - often, so a throw is caught early in its arc; a follower asks again for a snapshot
    // after this many checks without one; the first milliseconds after one, loot still turning up is the area loading;
    // a drop window after the "dropped" line; a follower's drop the owner hasn't brought back by then is removed; at
    // most this many of a follower's drops a second - against a runaway loop; loot this close to where we dropped it
    // is ours.)
    private const int Interval = 4;
    private const int SnapshotWait = 15;
    private const long SettleMs = 3000;
    private const long DropWindowMs = 1500;
    private const long DropPendingMs = 5000;
    private const int DropsPerSecond = 10;
    private const double DropReach = 80;

    // A follower's flags on loot: came from the other game (never a drop of ours), a drop or not is decided, it is our
    // drop, sent to the owner and waiting for its copy.
    [Flags]
    private enum Mark { FromOwner = 1, Decided = 2, OurDrop = 4, Sent = 8 }

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    private readonly AreaOwnership _ownership;
    private readonly Random _random = new();
    private int _frame;
    private long _dropUntil;
    // Where our arrows just dropped their ammo, and until when that counts as our drop.
    private readonly List<(double X, double Y, long Until)> _shots = new();
    // Our place, and our role there (and whom we follow).
    private string? _place;
    private bool _owning, _following;
    private int _owner = -1;
    // Owner: the followers here (a new one gets a snapshot), and a snapshot owed to everyone.
    private readonly HashSet<int> _followers = new();
    private bool _snapshotOwed;
    private long _dropWindowStart;
    private int _dropCount;
    // Follower: whether we have the owner's snapshot (and when), and checks without one.
    private bool _synced;
    private long _syncedAt;
    private int _waiting;

    // Owner: our loot -> its sync id (what it goes by on the wire); sync id -> our loot, as last sent; our loot -> the
    // drop token of the follower it came from.
    private readonly Dictionary<Instance, long> _uidOf = new();
    private readonly Dictionary<long, Instance> _known = new();
    private readonly Dictionary<Instance, string> _tokenOf = new();
    private long _nextUid;
    // Follower: our loot -> the owner's sync id, and back; our drops sent to the owner (token -> our loot) and when;
    // our marks on loot.
    private readonly Dictionary<Instance, long> _hidOf = new();
    private readonly Dictionary<long, Instance> _map = new();
    private readonly Dictionary<string, Instance> _pending = new();
    private readonly Dictionary<Instance, long> _sentAt = new();
    private readonly Dictionary<Instance, Mark> _marks = new();
    // What never changes about an item, read once: whether it's persistent, whether it's static. (Each read of a culled
    // item walks the room's deactivated instances - thousands - and a static one is woken to be read: every few frames
    // for every item, that was most of a frame.)
    private readonly Dictionary<Instance, bool> _persistent = new();
    private readonly Dictionary<Instance, bool> _static = new();
    // What was on the ground at the last look, and the frame it was in: an item in it isn't gone (no need to ask the
    // game) until the next frame.
    private readonly HashSet<Instance> _present = new();
    private int _presentFrame = -1;
    private long _nextToken;

    public LootSync(ModContext context, Session session, AreaOwnership ownership, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _ownership = ownership;
        _inSharedWorld = inSharedWorld;
        session.On<LootPacket>((sender, packet) => Profiler.Measure(context, "loot received", () => Receive(sender, packet)));
        Scripts.scr_actionsLogItem.Before(context, call =>
        {
            if (call.Args.Length > 0 && call.Args[0].AsString == "playerDropItems")
                _dropUntil = Environment.TickCount64 + DropWindowMs;
            return false;
        });
        // One of our arrows landing (o_arrow's user event 1 drops its ammo by its target with scr_loot_drop): far from
        // us - our drop all the same.
        context.OnCode("gml_Object_o_arrow_Other_11", before: (self, _) =>
        {
            if (self.Exists && IsPlayer(self.Get("owner")))
                _shots.Add((self.Get("target_x").AsReal, self.Get("target_y").AsReal, Environment.TickCount64 + DropWindowMs));
            return false;
        });
    }

    public void Clear()
    {
        _place = null;
        _owning = _following = false;
        _followers.Clear();
        _synced = false;
        Reset();
    }

    // The loot tables start over (a new area, or a new role in it).
    private void Reset()
    {
        _uidOf.Clear();
        _known.Clear();
        _tokenOf.Clear();
        _hidOf.Clear();
        _map.Clear();
        _pending.Clear();
        _sentAt.Clear();
        _marks.Clear();
        _persistent.Clear();
        _static.Clear();
        _present.Clear();
        _presentFrame = -1;
    }

    // Each frame.
    public void Tick()
    {
        if (++_frame % Interval != 0)
            return;
        Step();
    }

    /// <summary>Now, not at the next tick: what we've just dropped (as our own drops - by our player) goes to the owner, or
    /// as the owner what's new goes to the followers. For our player's death (DeathSync): once it's gone, nothing of
    /// ours is sent.</summary>
    public void Flush()
    {
        _dropUntil = Environment.TickCount64 + DropWindowMs;
        Step();
    }

    private void Step()
    {
        string? place = null;
        if (_session.Connected && Gm.InGame && _inSharedWorld() && !Rooms.IsChanging)
            place = OurPlayer.State()?.Place;
        // Who's here with us, and who runs it (AreaOwnership): the place's owner, or we follow them.
        var here = place == null ? new List<RemotePlayer>() : _session.Players.Where(p => p.State?.Place == place).ToList();
        bool ours = place != null && _ownership.Place == place;
        bool owning = ours && _ownership.Role == AreaRole.Owner && here.Count > 0;
        bool following = ours && _ownership.Role == AreaRole.Follower;
        int owner = following ? _ownership.Owner : -1;
        if (place != _place || owning != _owning || following != _following || owner != _owner)
        {
            // A new place, role or owner: the tables start over (and an owner's snapshot goes out).
            _place = place;
            _owning = owning;
            _following = following;
            _owner = owner;
            _followers.Clear();
            _synced = false;
            _waiting = 0;
            _snapshotOwed = true;
            Reset();
        }
        if (_owning)
            OwnerTick(here);
        else if (_following)
            FollowerTick();
        // (What was seen holds for this tick only: packets handled before the next ask the game.)
        _presentFrame = -1;
    }

    // ---- what's on the ground ----

    // Every ground item in the room - off screen too - but not an item in flight (a thrown item waits, hidden, where it
    // will land, and is loot once it has) nor a persistent one.
    private List<GroundItem> All()
    {
        var flying = Carried();
        var all = GroundItems.All().Where(item => !flying.Contains(item.Instance) && !Persistent(item)).ToList();
        _present.Clear();
        foreach (GroundItem item in all)
            _present.Add(item.Instance);
        _presentFrame = _frame;
        return all;
    }

    private bool Persistent(GroundItem item)
    {
        if (!_persistent.TryGetValue(item.Instance, out bool persistent))
            _persistent[item.Instance] = persistent = item.Instance.Get("persistent").AsBool;
        return persistent;
    }

    private bool Static(GroundItem item)
    {
        if (!_static.TryGetValue(item.Instance, out bool isStatic))
            _static[item.Instance] = isStatic = item.IsStatic;
        return isStatic;
    }

    // The items in the air in a throw (o_physical_shell carries its item as loot_object).
    private static HashSet<Instance> Carried()
    {
        var carried = new HashSet<Instance>();
        foreach (Instance shell in Instances.All(GameObjectId.o_physical_shell))
            if (Carrying(shell) is { IsNone: false } item)
                carried.Add(item);
        return carried;
    }

    // The item a throw carries (none if it carries none that's still there).
    private static Instance Carrying(Instance shell)
        => Instance.Of(shell.Get("loot_object")) is { IsNone: false, Exists: true } item ? item : default;

    // Loot removed from the world, culled or not (Instance.Destroy takes a culled one out of the culling controller's
    // list first). Never an item in flight: the throw reads it when it lands (removing it first crashed the game).
    private static void Destroy(Instance item)
    {
        if (!Carried().Contains(item))
            item.Destroy();
    }

    // Whether loot really left the world (picked up, destroyed) - culled loot is still there. One seen on the ground
    // this frame isn't; any other is asked about.
    private bool Gone(Instance item) => !(_presentFrame == _frame && _present.Contains(item)) && item.IsGone;

    // Loot's matching key: what the game hashes it by (scr_locationRoomEntityLootInstanceGetHash) - a weapon's idName or
    // the object's name, and its floored position - so twins from the same save match.
    private static string Key(GroundItem item)
    {
        string name = item.ObjectName;
        if (name == "o_weapon_loot" && item.Instance.Get("data").AsDsMap is { } data)
            name = data.Get("idName", "N/A").AsString;
        return $"{name}_{Math.Floor(item.X)}_{Math.Floor(item.Y)}";
    }

    // What's on the ground with its matching key, worked out when first asked.
    private Lazy<List<(GroundItem Item, string Key)>> Here() => new(() => All().Select(item => (item, Key(item))).ToList());

    private static JsonNode? Flight(GroundItem item) => item.Flight is { } flight ? JsonNode.Parse(flight.ToJson()) : null;

    // Loot made from its saved state (another game's): where it lay, then put back in the air if it was in a throw.
    private static GroundItem? Create(string? json, JsonNode? flight)
    {
        if (json is not { Length: > 0 } || GroundItems.Create(json) is not { } item)
            return null;
        if (flight != null && ItemFlight.FromJson(flight.ToJsonString()) is { } arc)
            item.Fly(arc);
        return item;
    }

    private bool Has(Instance item, Mark mark) => _marks.TryGetValue(item, out Mark marks) && (marks & mark) != 0;
    private void Set(Instance item, Mark mark) => _marks[item] = _marks.GetValueOrDefault(item) | mark;

    private static bool IsPlayer(GmValue unit) => Units.IsPlayer(unit);


    // ---- owner ----

    private void OwnerTick(List<RemotePlayer> here)
    {
        // Someone new here: everyone gets a snapshot (one list of ids for all).
        foreach (var player in here)
            if (_followers.Add(player.Slot))
                _snapshotOwed = true;
        _followers.IntersectWith(here.Select(p => p.Slot));
        if (_snapshotOwed)
        {
            _snapshotOwed = false;
            SendToFollowers(LootKind.Snapshot, OwnerSnapshot());
            return;
        }
        if (OwnerDiff() is { } changes)
            SendToFollowers(LootKind.Changes, changes);
    }

    // Owner: loot's sync id, given on first use.
    private long Uid(Instance item)
    {
        if (!_uidOf.TryGetValue(item, out long uid))
            _uidOf[item] = uid = ++_nextUid;
        return uid;
    }

    // Owner: one loot item for followers - {u: sync id, k: matching key, j: its saved state, f: its throw if it's in
    // the air, t: the drop token a follower gave it ("" if none) - so the follower whose drop it is keeps its own
    // instead of getting a second}.
    private JsonObject Entry(GroundItem item) => new()
    {
        ["u"] = Uid(item.Instance), ["k"] = Key(item), ["j"] = item.ToJson() ?? "", ["f"] = Flight(item),
        ["t"] = _tokenOf.GetValueOrDefault(item.Instance, ""),
    };

    // Owner: all our ground loot, for followers starting over - everything in it is now known.
    private string OwnerSnapshot()
    {
        _known.Clear();
        var all = new JsonArray();
        foreach (GroundItem item in All())
        {
            JsonObject entry = Entry(item);
            _known[entry["u"]!.GetValue<long>()] = item.Instance;
            all.Add(entry);
        }
        return all.ToJsonString();
    }

    // Owner, every few frames: what changed since - new loot (drops, kills, a follower's drop we made), at once, in the
    // air or not; and loot that left (picked up by anyone). {add: [entries], gone: [sync ids]}; null if nothing did.
    private string? OwnerDiff()
    {
        var add = new JsonArray();
        foreach (GroundItem item in All())
        {
            long uid = Uid(item.Instance);
            if (_known.TryAdd(uid, item.Instance))
                add.Add(Entry(item));
        }
        var gone = new JsonArray();
        foreach (var (uid, id) in _known.ToList())
            if (Gone(id))
            {
                _known.Remove(uid);
                gone.Add(uid);
            }
        return add.Count == 0 && gone.Count == 0 ? null : new JsonObject { ["add"] = add, ["gone"] = gone }.ToJsonString();
    }

    // Owner: a follower dropped something - {j: its saved state, f: its throw, t: its token}. Made here and thrown along
    // the same arc; our next diff adds it, token and all, to every follower - the one whose drop it is then keeps its own
    // copy as the synced item (FollowerAdd).
    private void OwnerDrop(string json)
    {
        if (!Gm.InstanceExists(GameObjectId.o_player) || JsonNode.Parse(json) is not JsonObject drop)
            return;
        if (Create(drop["j"]?.GetValue<string>(), drop["f"]) is { } item)
            _tokenOf[item.Instance] = drop["t"]?.GetValue<string>() ?? "";
    }

    // Owner: a follower picked these up (our sync ids) - gone from our world too; our next diff tells every follower.
    private void OwnerTaken(string json)
    {
        if (JsonNode.Parse(json) is not JsonArray uids)
            return;
        foreach (JsonNode? uid in uids)
            if (uid != null && _known.TryGetValue(uid.GetValue<long>(), out Instance id) && !Gone(id))
                Destroy(id);
    }

    // ---- follower ----

    private void FollowerTick()
    {
        if (!_synced)
        {
            // No snapshot for here yet (just arrived, or it was missed): after a while, ask for one.
            if (++_waiting >= SnapshotWait)
            {
                _waiting = 0;
                Send(LootKind.SnapshotRequest, "", _owner);
            }
            return;
        }
        long now = Environment.TickCount64;
        _shots.RemoveAll(shot => shot.Until < now);
        var (taken, drops) = FollowerStep(now <= _dropUntil, now - _syncedAt < SettleMs, now);
        if (taken.Count > 0)
            Send(LootKind.Taken, taken.ToJsonString(), _owner);
        foreach (JsonObject drop in drops)
            Send(LootKind.Dropped, drop.ToJsonString(), _owner);
    }

    // Follower: the owner has this loot (an entry). Bound to ours if we have it - our own drop coming back (its token),
    // or our twin from the same save (same key: among here's, what's on the ground worked out once for a batch of
    // entries) - else made from its saved state, and flown along the owner's arc if it's in the air.
    private void FollowerAdd(JsonObject entry, Lazy<List<(GroundItem Item, string Key)>> here)
    {
        long uid = entry["u"]!.GetValue<long>();
        if (_map.TryGetValue(uid, out Instance have) && !Gone(have))
            return;
        Instance bound = default;
        string token = entry["t"]?.GetValue<string>() ?? "";
        if (token.Length > 0 && _pending.Remove(token, out Instance mine) && !Gone(mine))
            bound = mine;
        if (bound.IsNone)
        {
            string key = entry["k"]?.GetValue<string>() ?? "";
            (GroundItem Item, string Key) twin = here.Value.FirstOrDefault(found => found.Key == key && !_hidOf.ContainsKey(found.Item.Instance)
                && !Has(found.Item.Instance, Mark.OurDrop) && !Gone(found.Item.Instance));
            bound = twin.Item.Instance;
        }
        if (bound.IsNone && Create(entry["j"]?.GetValue<string>(), entry["f"]) is { } made)
            bound = made.Instance;
        if (bound.IsNone)
            return;
        Set(bound, Mark.FromOwner);
        _hidOf[bound] = uid;
        _map[uid] = bound;
    }

    // Follower: the owner's ground loot - ours made to match it: each item bound to our twin or made, and anything it
    // didn't list removed (it isn't in the owner's world). What's here now is the owner's to list, so none of it can be a
    // drop of ours. "listed N, removed M", for the log.
    private string FollowerSnapshot(string json)
    {
        Reset();
        if (JsonNode.Parse(json) is not JsonArray list)
            return "unreadable";
        var here = Here();
        foreach (var (item, _) in here.Value)
            Set(item.Instance, Mark.FromOwner);
        foreach (JsonObject entry in list.OfType<JsonObject>())
            FollowerAdd(entry, here);
        int removed = 0;
        foreach (GroundItem item in All())
            if (!_hidOf.ContainsKey(item.Instance))
            {
                Destroy(item.Instance);
                removed++;
            }
        return $"listed {list.Count}, removed {removed}";
    }

    // Follower: what changed on the owner - loot gone from its world removed here, new loot bound or made.
    private void FollowerDiff(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject diff)
            return;
        if (diff["gone"] is JsonArray gone)
            foreach (JsonNode? uid in gone)
                if (uid != null && _map.Remove(uid.GetValue<long>(), out Instance id))
                    Destroy(id);
        if (diff["add"] is JsonArray add)
        {
            var here = Here();
            foreach (JsonObject entry in add.OfType<JsonObject>())
                FollowerAdd(entry, here);
        }
    }

    // Follower, every few frames once we have the owner's snapshot: what to tell the owner - the owner's ids of loot we
    // picked up, and our drops ({j: saved state, f: throw, t: token}).
    // - Picked up: bound loot that left our world.
    // - Dropped: sent to the owner as soon as we see it, with its throw and a token, and ours kept, in the air. The owner
    //   makes it and throws it along the same arc; its diff brings it back with the token and ours becomes the synced
    //   item (FollowerAdd). One the owner doesn't bring back within a few seconds (it didn't take it) is removed: the
    //   owner's list decides what lies here.
    // A drop is decided once, when we first see the loot: it must turn up next to our player while dropWindow is open
    // (our player just dropped something: the game's "dropped" log line), or by where one of our arrows just landed (an
    // arrow drops its ammo by its target), or have flown there in one of our own throws (an o_physical_shell of ours
    // carrying it: marked our drop while it's still in the air). Any other unbound loot isn't ours to add - the game
    // swapping an item for a new one (food changing), loot made here that the owner makes too - and is removed: the
    // owner's list decides what lies here (reporting those made the owner create copies endlessly). Except while settling
    // (the first seconds after the snapshot: loot still turning up is the area finishing loading here).
    private (JsonArray Taken, List<JsonObject> Drops) FollowerStep(bool dropWindow, bool settling, long now)
    {
        var taken = new JsonArray();
        var drops = new List<JsonObject>();
        if (OurPlayer.Instance is not { IsNone: false } player)
            return (taken, drops);
        foreach (var (uid, id) in _map.ToList())
            if (Gone(id))
            {
                _map.Remove(uid);
                taken.Add(uid);
            }
        // Items in our throws: ours.
        foreach (Instance shell in Instances.All(GameObjectId.o_physical_shell))
            if (Carrying(shell) is { IsNone: false } item && IsPlayer(shell.Get("owner")) && !Has(item, Mark.Decided))
                Set(item, Mark.OurDrop);
        // Where loot can be our drop: by our player while the drop window is open, by our arrows' landing spots.
        var near = _shots.Select(shot => (shot.X, shot.Y)).ToList();
        if (dropWindow)
            near.Add((player.Get("x").AsReal, player.Get("y").AsReal));
        foreach (GroundItem item in All())
        {
            Instance id = item.Instance;
            if (_hidOf.ContainsKey(id) || Static(item))
                continue;
            if (!Has(id, Mark.Decided))
            {
                Set(id, Mark.Decided);
                if (!Has(id, Mark.FromOwner) && near.Any(spot => Math.Sqrt(Math.Pow(item.X - spot.X, 2) + Math.Pow(item.Y - spot.Y, 2)) <= DropReach))
                    Set(id, Mark.OurDrop);
            }
            if (!Has(id, Mark.OurDrop))
            {
                if (!settling)
                    Destroy(id);
                continue;
            }
            if (Has(id, Mark.Sent))
            {
                if (now - _sentAt.GetValueOrDefault(id, now) > DropPendingMs)
                    Destroy(id);
                continue;
            }
            if (item.ToJson() is not { } json)
                continue;
            string token = $"{++_nextToken}_{_random.Next(1000000)}";
            drops.Add(new JsonObject { ["j"] = json, ["f"] = Flight(item), ["t"] = token });
            Set(id, Mark.Sent);
            _pending[token] = id;
            _sentAt[id] = now;
        }
        return (taken, drops);
    }

    // ---- network ----

    private void SendToFollowers(LootKind kind, string json)
    {
        foreach (int slot in _followers)
            Send(kind, json, slot);
    }

    private void Send(LootKind kind, string json, int to)
        => _session.Send(new LootPacket(kind, _place ?? "", JoinCompression.Compress(json)), to);

    private void Receive(RemotePlayer sender, LootPacket packet)
    {
        // Only for the place we're in, in the role it's meant for (one sent just before either of us moved on is
        // dropped).
        if (packet.Place != _place)
            return;
        string json = JoinCompression.Decompress(packet.Data);
        switch (packet.Kind)
        {
            case LootKind.Snapshot when _following && sender.Slot == _owner:
                string result = FollowerSnapshot(json);
                _synced = true;
                _syncedAt = Environment.TickCount64;
                _waiting = 0;
                _context.Log($"Ground loot here from {sender.Name}: {result}");
                break;
            case LootKind.Changes when _following && sender.Slot == _owner && _synced:
                FollowerDiff(json);
                break;
            case LootKind.SnapshotRequest when _owning:
                _snapshotOwed = true;
                break;
            case LootKind.Taken when _owning:
                OwnerTaken(json);
                break;
            case LootKind.Dropped when _owning:
                long now = Environment.TickCount64;
                if (now - _dropWindowStart >= 1000)
                {
                    _dropWindowStart = now;
                    _dropCount = 0;
                }
                if (++_dropCount <= DropsPerSecond)
                    OwnerDrop(json);
                else
                    _context.Log($"Ignored a drop from {sender.Name}: more than {DropsPerSecond} a second");
                break;
        }
    }
}
