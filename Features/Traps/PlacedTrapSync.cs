using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Traps;

// Traps players set - a claw trap (o_bear_trap) set down, caltrops (o_trap_caltrops) thrown - where players are together.
// The game makes a player's trap its faction's ("Player"), and before, it was only in its setter's game: a follower's
// trap caught nothing (the enemies that walk into it are the owner's), and nobody else saw one.
// - The place's owner (AreaOwnership) has the real ones: its list - each trap, armed or spent, its uses left - goes to
//   everyone there as it changes, and their games match it: made, armed, spent or taken away.
// - A follower setting one down, re-arming, disarming or picking one up tells the owner, which does the same; till its
//   list says so, the follower's stays as the follower left it.
// - They catch enemies only: never a player (anyone's, nor another player's stand-in), and in a follower's game nothing
//   at all - the owner's units walk into the owner's, and what they do there (a trap sprung, a caltrop's last use) comes
//   back in the list.
// (Map traps are TrapSync's: spotted and spent by anyone, there in every game from the place's build.)
public sealed class PlacedTrapSync
{
    private const int Interval = 6;
    private const long PendingMs = 2500;

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly AreaOwnership _ownership;
    private readonly PlayerManager _players;
    private readonly Func<bool> _inSharedWorld;
    private int _frame;
    private string? _place;
    private string _others = "";
    // The players' traps here, by key: the instance and how it was at the last look (kept while it's culled - off
    // screen, its variables can't be read).
    private readonly Dictionary<string, (Instance Trap, string Entry)> _local = new();
    // Owner: what it last sent. Follower: the owner's last list, by key; our own changes not in it yet (sent when).
    private string _sent = "";
    private Dictionary<string, string>? _owner;
    private readonly Dictionary<string, long> _pending = new();

    public PlacedTrapSync(ModContext context, Session session, AreaOwnership ownership, PlayerManager players, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _ownership = ownership;
        _players = players;
        _inSharedWorld = inSharedWorld;
        session.On<PlacedTrapsPacket>(ReceiveList);
        session.On<PlacedTrapChangePacket>(ReceiveChange);
        // (Something on a trap's cell: a player's trap catches only enemies - and in a follower's game, nothing.)
        context.OnCode("gml_Object_o_trap_Other_17", before: (trap, _) => Spared(trap));
    }

    public void Clear()
    {
        _place = null;
        _others = "";
        _local.Clear();
        _sent = "";
        _owner = null;
        _pending.Clear();
    }

    private bool Shared => _session.Connected && _inSharedWorld() && Gm.InGame;

    // Whether a player's trap lets what's stepped on it be.
    private bool Spared(Instance trap)
    {
        if (!Shared || trap.IsNone || !IsPlayers(trap))
            return false;
        if (_place != null && _ownership.Place == _place && _ownership.Role == AreaRole.Follower)
            return true;
        GmValue targ = trap.Get("targ");
        if (targ.Kind is not (GmKind.Instance or GmKind.Real) || Instance.Of(targ) is not { IsNone: false } unit || !unit.Exists)
            return false;
        int obj = unit.Get("object_index").AsInt;
        return obj == (int)GameObjectId.o_player || Gm.ObjectIsAncestor(obj, (int)GameObjectId.o_player) || obj == _players.ObjectIndex;
    }

    public void Tick()
    {
        string? place = OurPlayer.State()?.Place;
        var here = place == null
            ? new List<int>()
            : _session.Players.Where(p => p.State?.Place == place).Select(p => p.Slot).OrderBy(s => s).ToList();
        if (!Shared || place == null || here.Count == 0 || Rooms.IsChanging)
        {
            if (_place != null)
                Clear();
            return;
        }
        if (place != _place)
        {
            Clear();
            _place = place;
        }
        string others = string.Join(",", here);
        bool fresh = others != _others;
        _others = others;
        if (!fresh && ++_frame % Interval != 0)
            return;
        var gone = Look();
        if (_ownership.Place != place)
            return;
        if (_ownership.Role == AreaRole.Owner)
        {
            string list = string.Join("\n", _local.Values.Select(v => v.Entry).OrderBy(e => e, StringComparer.Ordinal));
            if (list != _sent || fresh)
            {
                _sent = list;
                _session.Send(new PlacedTrapsPacket(place, list));
            }
        }
        else if (_ownership.Role == AreaRole.Follower)
            Follow(gone);
    }

    // The players' traps here now: _local brought up to date; the keys of those gone since (picked up, used up).
    private List<string> Look()
    {
        foreach (Instance trap in Instances.All(GameObjectId.o_trap))
            if (IsPlayers(trap))
                _local[KeyOf(trap)] = (trap, EntryOf(trap));
        var gone = _local.Where(t => t.Value.Trap.IsGone).Select(t => t.Key).ToList();
        foreach (string key in gone)
            _local.Remove(key);
        return gone;
    }

    // Follower: ours as the owner has them - but for our own changes, which go to the owner first.
    private void Follow(List<string> gone)
    {
        if (_owner == null)
            return;
        long now = Environment.TickCount64;
        // (Ours picked up: away on the owner too.)
        foreach (string key in gone)
            if (_owner.ContainsKey(key) && !_pending.ContainsKey(key))
            {
                _pending[key] = now;
                _session.Send(new PlacedTrapChangePacket(_place!, _owner[key], false), _ownership.Owner);
            }
        // (Ours set down, re-armed, disarmed: the same on the owner.)
        bool match = false;
        foreach (var (key, (_, entry)) in _local)
        {
            if (_owner.TryGetValue(key, out string? theirs) && Armed(theirs) == Armed(entry))
                continue;
            if (_pending.TryGetValue(key, out long since))
            {
                // (The owner hasn't taken it: its way, then.)
                if (now - since > PendingMs)
                {
                    _pending.Remove(key);
                    match = true;
                }
                continue;
            }
            _pending[key] = now;
            _session.Send(new PlacedTrapChangePacket(_place!, entry, true), _ownership.Owner);
        }
        foreach (string key in _pending.Keys.ToList())
            if (now - _pending[key] > PendingMs)
            {
                _pending.Remove(key);
                match = true;
            }
        if (match)
            Match();
    }

    private void ReceiveList(RemotePlayer from, PlacedTrapsPacket packet)
    {
        if (!Shared || packet.Place != _place || _ownership.Role != AreaRole.Follower || from.Slot != _ownership.Owner)
            return;
        _owner = packet.Traps.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToDictionary(KeyOfEntry, e => e);
        // (Ours the owner now has as we left it: no longer waiting.)
        foreach (string key in _pending.Keys.ToList())
        {
            bool mine = _local.TryGetValue(key, out var local);
            bool theirs = _owner.TryGetValue(key, out string? entry);
            if (mine == theirs && (!mine || Armed(local.Entry) == Armed(entry!)))
                _pending.Remove(key);
        }
        Match();
    }

    // Follower: ours made as the owner's list has them (not those of ours still on their way to it).
    private void Match()
    {
        if (_owner == null)
            return;
        Look();
        foreach (var (key, (trap, _)) in _local.ToList())
            if (!_owner.ContainsKey(key) && !_pending.ContainsKey(key))
            {
                _local.Remove(key);
                trap.Destroy();
            }
        foreach (var (key, entry) in _owner)
        {
            if (_pending.ContainsKey(key))
                continue;
            if (_local.TryGetValue(key, out var local))
            {
                if (local.Trap.Exists)
                    Set(local.Trap, entry);
            }
            else if (Make(entry) is { IsNone: false } made)
                _local[key] = (made, EntryOf(made));
        }
    }

    // Owner: a follower's change to one of the players' traps here - done here too.
    private void ReceiveChange(RemotePlayer from, PlacedTrapChangePacket packet)
    {
        if (!Shared || packet.Place != _place || _ownership.Role != AreaRole.Owner || !_ownership.Others.Contains(from.Slot))
            return;
        Look();
        string key = KeyOfEntry(packet.Trap);
        _local.TryGetValue(key, out var local);
        if (!packet.Present)
        {
            if (local.Trap is { IsNone: false } trap)
            {
                _local.Remove(key);
                trap.Destroy();
            }
            return;
        }
        if (local.Trap is { IsNone: false } existing)
        {
            if (existing.Exists)
                Set(existing, packet.Trap);
        }
        else if (Make(packet.Trap) is { IsNone: false } made)
            _local[key] = (made, EntryOf(made));
        _context.Log($"Traps here: {from.Name}'s {key} {(Armed(packet.Trap) ? "set" : "spent")}");
    }

    // ---- traps ----

    private static bool IsPlayers(Instance trap)
        => trap.Get("faction_key") is { Kind: GmKind.String } faction && faction.AsString == "Player";

    private static string KeyOf(Instance trap)
        => $"{Gm.ObjectGetName(trap.Get("object_index").AsInt)}_{Math.Floor(trap.Get("x").AsReal)}_{Math.Floor(trap.Get("y").AsReal)}";

    private static string EntryOf(Instance trap)
        => $"{Gm.ObjectGetName(trap.Get("object_index").AsInt)}|{Math.Floor(trap.Get("x").AsReal)}|{Math.Floor(trap.Get("y").AsReal)}|"
            + $"{(trap.Get("is_disarm").AsBool ? 0 : 1)}|{trap.Get("duration").AsReal}";

    private static string KeyOfEntry(string entry)
    {
        string[] f = entry.Split('|');
        return $"{f[0]}_{f[1]}_{f[2]}";
    }

    private static bool Armed(string entry) => entry.Split('|') is { Length: >= 4 } f && f[3] == "1";

    // A player's trap made as an entry has it - as setting one down makes it.
    private static Instance Make(string entry)
    {
        string[] f = entry.Split('|');
        if (f.Length < 5)
            return default;
        int obj = Gm.AssetGetIndex(f[0]);
        if (obj < 0)
            return default;
        Instance trap = Game.CallBuiltin("instance_create_depth", double.Parse(f[1]), double.Parse(f[2]), 0, obj).AsInstance;
        if (trap.IsNone)
            return default;
        trap["faction_key"] = "Player";
        trap["visible"] = true;
        trap["locate"] = true;
        trap["image_alpha"] = 1;
        Set(trap, entry, made: true);
        return trap;
    }

    // Armed or spent, and its uses left, as an entry has it.
    private static void Set(Instance trap, string entry, bool made = false)
    {
        string[] f = entry.Split('|');
        if (f.Length >= 5 && double.TryParse(f[4], out double duration))
            trap["duration"] = duration;
        bool armed = Armed(entry);
        bool isArmed = !trap.Get("is_disarm").AsBool;
        if (armed == isArmed && !made)
            return;
        GmValue markgrid = Instances.All(GameObjectId.o_controller).FirstOrDefault() is { IsNone: false } controller
            ? controller.Get("markgrid") : GmValue.Undefined;
        trap["is_disarm"] = !armed;
        if (armed)
        {
            // (As re-arming one does: its set animation played back, on the marks grid.)
            if (!made)
            {
                trap["image_index"] = trap.Get("image_number").AsReal - 1;
                trap["image_speed"] = -1;
            }
            if (markgrid.Kind == GmKind.Real)
                Game.CallBuiltin("ds_grid_set", markgrid, trap.Get("grid_x"), trap.Get("grid_y"), trap);
        }
        else
        {
            // (As TrapSync spends one: its last frame, off the marks grid.)
            trap["image_speed"] = 0;
            Game.CallBuiltinAs("event_user", trap, trap, 9);
            if (markgrid.Kind == GmKind.Real)
                Game.CallBuiltin("ds_grid_set", markgrid, trap.Get("grid_x"), trap.Get("grid_y"), -4);
        }
    }
}
