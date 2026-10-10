using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Gathering;

// Gathering where players are together. What a place is built with to be picked - herbs, mushrooms, sticks, stones and
// the other items lying there from the start ("static" ground items, which LootSync leaves alone: every game has them
// from the place's build), and berry bushes and nests (o_interactive_harvest: picked once, then bare) - was each game's
// own, so both players could pick the same plant; it only evened out as the place's save was passed on (WorldSync).
// - Picking one (ours gone from the ground, or our bush picked) tells the others at once, and it's gone - or picked -
//   in their games too.
// - Players coming together: the place's owner (AreaOwnership) sends what's left - its built-with items still on the
//   ground, its bushes picked - and the others' match it: what the owner's lost, theirs lose.
// Matched by object and position (every game builds a place alike). Two picking the same one at once both get it.
public sealed class GatherSync
{
    private const int Interval = 6;
    // (Frames in a place before its things are looked for: a place's items are still being made as it opens.)
    private const int Settle = 30;

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly AreaOwnership _ownership;
    private readonly Func<bool> _inSharedWorld;
    private int _frame;
    // The place we're keeping, and who else was there.
    private string? _place;
    private string _others = "";
    // Its built-with ground items, by key (each found once - culled ones too).
    private readonly Dictionary<string, Instance> _items = new();
    private bool _built;
    // Frames in this place so far; the owner's snapshot to send once settled; what came before we'd settled.
    private int _settled;
    private bool _snapshotDue;
    private readonly List<GatherPacket> _early = new();
    // Bushes the others have picked that were off screen here (their own variables can't be set till they're back).
    private readonly HashSet<string> _toHarvest = new();
    // Our bush being picked: whether it was already.
    private readonly Stack<bool> _picking = new();

    public GatherSync(ModContext context, Session session, AreaOwnership ownership, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _ownership = ownership;
        _inSharedWorld = inSharedWorld;
        session.On<GatherPacket>(Receive);
        // (Picking a bush or a nest - ours: it was whole, and now isn't.)
        context.OnCode("gml_Object_o_interactive_harvest_Other_10",
            before: (bush, _) =>
            {
                _picking.Push(!bush.IsNone && bush.Get("is_execute").AsBool);
                return false;
            },
            after: (bush, _) =>
            {
                if (_picking.Count > 0 && !_picking.Pop() && !bush.IsNone && bush.Exists && bush.Get("is_execute").AsBool)
                    Picked(bush);
            });
    }

    public void Clear()
    {
        _place = null;
        _others = "";
        _items.Clear();
        _built = false;
        _settled = 0;
        _snapshotDue = false;
        _early.Clear();
        _toHarvest.Clear();
        _picking.Clear();
    }

    public void Tick()
    {
        string? place = OurPlayer.State()?.Place;
        var here = place == null
            ? new List<int>()
            : _session.Players.Where(p => p.State?.Place == place).Select(p => p.Slot).OrderBy(s => s).ToList();
        if (!_session.Connected || !_inSharedWorld() || !Gm.InGame || place == null || here.Count == 0 || Rooms.IsChanging)
        {
            if (_place != null)
                Clear();
            return;
        }
        string others = string.Join(",", here);
        bool fresh = place != _place || others != _others;
        if (place != _place)
        {
            // (What's come for it already stays.)
            var early = _early.Where(p => p.Place == place).ToList();
            Clear();
            _place = place;
            _early.AddRange(early);
        }
        _others = others;
        if (fresh)
            _snapshotDue = true;
        if (_settled < Settle)
        {
            if (++_settled < Settle)
                return;
            foreach (GatherPacket early in _early)
                Take(early);
            _early.Clear();
        }
        else if (!_snapshotDue && ++_frame % Interval != 0)
            return;
        Build();
        // (Ours picked up since: gone for the others.)
        var gone = _items.Where(item => item.Value.IsGone).Select(item => item.Key).ToList();
        foreach (string key in gone)
            _items.Remove(key);
        if (gone.Count > 0)
            _session.Send(new GatherPacket(place, false, string.Join("\n", gone), ""));
        HarvestWaiting();
        // (Players come together: the owner's, as it is.)
        bool snapshot = _snapshotDue;
        _snapshotDue = false;
        if (snapshot && _ownership.Place == place && _ownership.Role == AreaRole.Owner)
        {
            var picked = Bushes().Where(b => b.Exists && b.Get("is_execute").AsBool).Select(KeyOf);
            _session.Send(new GatherPacket(place, true, string.Join("\n", _items.Keys), string.Join("\n", picked)));
        }
    }

    // The place's built-with ground items, found once.
    private void Build()
    {
        if (_built)
            return;
        _built = true;
        var counts = new Dictionary<string, int>();
        foreach (GroundItem item in GroundItems.All())
        {
            if (!item.IsStatic)
                continue;
            string key = KeyOf(item.Instance);
            // (Two alike in one cell: told apart by order.)
            int n = counts.GetValueOrDefault(key);
            counts[key] = n + 1;
            _items[n == 0 ? key : $"{key}#{n}"] = item.Instance;
        }
    }

    private static IEnumerable<Instance> Bushes() => Instances.All(GameObjectId.o_interactive_harvest, includeCulled: true);

    // Ours just picked: to the others.
    private void Picked(Instance bush)
    {
        if (_place == null || OurPlayer.State()?.Place != _place)
            return;
        _session.Send(new GatherPacket(_place, false, "", KeyOf(bush)));
    }

    private void Receive(RemotePlayer from, GatherPacket packet)
    {
        if (!Gm.InGame || packet.Place != OurPlayer.State()?.Place)
            return;
        // (Not settled in yet: when we are.)
        if (_place != packet.Place || _settled < Settle || Rooms.IsChanging)
        {
            _early.Add(packet);
            return;
        }
        Take(packet);
    }

    private void Take(GatherPacket packet)
    {
        Build();
        var items = Split(packet.Items);
        // Gone: theirs picked (or, the owner's snapshot, every one it hasn't got).
        var gone = packet.Snapshot ? _items.Keys.Where(key => !items.Contains(key)).ToList() : items.Where(_items.ContainsKey).ToList();
        foreach (string key in gone)
        {
            Instance item = _items[key];
            _items.Remove(key);
            if (!item.IsGone)
                item.Destroy();
        }
        foreach (string key in Split(packet.Harvested))
            _toHarvest.Add(key);
        HarvestWaiting();
        if (packet.Snapshot && gone.Count > 0)
            _context.Log($"Gathering here: {gone.Count} picked thing(s) gone, as the owner has it");
    }

    // The others' picked bushes: ours picked too - those on screen now (a culled one waits till it's back).
    private void HarvestWaiting()
    {
        if (_toHarvest.Count == 0)
            return;
        foreach (Instance bush in Bushes())
        {
            if (!bush.Exists || !_toHarvest.Remove(KeyOf(bush)) || bush.Get("is_execute").AsBool)
                continue;
            // (As picking it does, without the berries.)
            if (bush.Get("highsign") is { Kind: GmKind.Instance or GmKind.Real } sign && Instance.Of(sign) is { IsNone: false } s && s.Exists)
                s.Destroy();
            bush["highsign"] = -4;
            bush["mask_index"] = Gm.AssetGetIndex("s_empty");
            bush["image_index"] = 1;
            bush["is_execute"] = true;
        }
    }

    private static HashSet<string> Split(string keys) => keys.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    // A thing's key: its object and position - the same one in every game, which build the place alike.
    private static string KeyOf(Instance thing)
        => $"{Gm.ObjectGetName(thing.Get("object_index").AsInt)}_{Math.Floor(thing.Get("x").AsReal)}_{Math.Floor(thing.Get("y").AsReal)}";
}
