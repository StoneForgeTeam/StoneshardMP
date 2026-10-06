using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Breakables;

// Crates, barrels, furniture and everything else that can be broken (o_stuff with can_broke: c_broken's kinds, containers,
// doors...) where players are together: one health pool for each, that anyone can hurt. Each game watches the HP of
// those on screen; what one loses goes to the others as damage (BreakableKind.Damage), taken off theirs - so two hitting
// it at once add up. One that breaks here (its HP gone, c_broken's step destroys it: c_broken's destroy - its debris,
// its noise - which every kind runs) goes to the others (Broken), whose breaks the same way: its HP made 0. (Destroyed
// with HP left - a door swapped for its other state, say - isn't broken.) When players come together, the place's owner (AreaOwnership)
// sends what's been hurt or broken since it came (State: what's left of each) - the others' copies may be older. One off
// screen when its news comes gets it when it's next on. Matched by object and position: every game builds a place alike.
// Where nobody else is, nothing's sent: leaving, the place's save is (WorldSync).
public sealed class BreakableSync
{
    // (Every this many frames.)
    private const int Interval = 6;

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly AreaOwnership _ownership;
    private readonly Func<bool> _inSharedWorld;
    private int _frame;
    // The place we're keeping, and who else was there; the HP each breakable on screen had at the last look; what's been
    // hurt or broken here since we came (what's left: 0 broken); news for those that weren't on screen (damage to take
    // off, or broken); those we're breaking as another game said (not a break of ours to send).
    private string? _place;
    private string _others = "";
    private readonly Dictionary<string, double> _last = new();
    // (Each on screen at the last look's key, by instance: a container moves itself to its cell's middle as it breaks.)
    private readonly Dictionary<Instance, string> _keys = new();
    private readonly Dictionary<string, double> _hurt = new();
    private readonly Dictionary<string, (double Damage, double? Left, bool Broken)> _pending = new();
    private readonly HashSet<string> _breaking = new();

    public BreakableSync(ModContext context, Session session, AreaOwnership ownership, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _ownership = ownership;
        _inSharedWorld = inSharedWorld;
        session.On<BreakablePacket>(Receive);
        // c_broken's destroy: its debris - something broken (HP gone).
        context.OnCode("gml_Object_c_broken_Destroy_0", before: (self, _) =>
        {
            if (_place != null && !self.IsNone && self.Get("HP").AsReal <= 0)
                Broke(_keys.GetValueOrDefault(self) ?? KeyOf(self));
            return false;
        });
    }

    public void Clear()
    {
        _place = null;
        _others = "";
        _last.Clear();
        _keys.Clear();
        _hurt.Clear();
        _pending.Clear();
        _breaking.Clear();
    }

    public void Tick()
    {
        string? place = OurPlayer.State()?.Place;
        bool shared = _session.Connected && _inSharedWorld() && Gm.InGame && place != null && !Rooms.IsChanging;
        if (!shared)
        {
            if (_place != null)
                Clear();
            return;
        }
        // (A new place: everything here starts over. Someone's come or gone: the owner sends what's hurt here.)
        var here = _session.Players.Where(p => p.State?.Place == place).Select(p => p.Slot).OrderBy(s => s).ToList();
        string others = string.Join(",", here);
        if (place != _place)
        {
            Clear();
            _place = place;
        }
        bool fresh = others != _others;
        _others = others;
        if (!fresh && ++_frame % Interval != 0)
            return;
        bool owner = _ownership.Place == place && _ownership.Role == AreaRole.Owner;
        if (fresh && owner && here.Count > 0 && _hurt.Count > 0)
        {
            foreach (var (key, left) in _hurt)
                Send(left <= 0 ? BreakableKind.Broken : BreakableKind.State, key, left);
            _context.Log($"Breakables here: sent {_hurt.Count} hurt or broken to the others");
        }
        _keys.Clear();
        foreach (Instance stuff in Instances.All(GameObjectId.o_stuff))
        {
            if (!stuff.Get("can_broke").AsBool)
                continue;
            string key = KeyOf(stuff);
            _keys[stuff] = key;
            double hp = stuff.Get("HP").AsReal;
            if (_pending.Remove(key, out var news))
            {
                Apply(stuff, key, hp, news.Damage, news.Left, news.Broken);
                continue;
            }
            // (Hurt here since the last look: to the others, if there are any.)
            if (_last.TryGetValue(key, out double was) && hp < was)
            {
                _hurt[key] = Math.Max(0, hp);
                if (here.Count > 0 && hp > 0)
                    Send(BreakableKind.Damage, key, was - hp);
            }
            _last[key] = hp;
        }
    }

    // Broken here: to the others there - unless it's another game's break we made.
    private void Broke(string key)
    {
        if (OurPlayer.State()?.Place != _place)
            return;
        bool others = _session.Players.Any(p => p.State?.Place == _place);
        _last.Remove(key);
        _hurt[key] = 0;
        if (!_breaking.Remove(key) && others)
            Send(BreakableKind.Broken, key, 0);
    }

    // Another game's news, on ours: its damage taken off, what's left made what it has (if it's less), or broken. Its HP
    // now (0: it breaks in its step).
    private double Apply(Instance stuff, string key, double hp, double damage, double? left, bool broken)
    {
        double now = broken ? 0 : Math.Max(0, Math.Min(hp - damage, left ?? double.MaxValue));
        if (now < hp)
        {
            if (now <= 0)
                _breaking.Add(key);
            stuff.Set("HP", now);
            _hurt[key] = now;
        }
        _last[key] = Math.Min(hp, now);
        return now;
    }

    private void Send(BreakableKind kind, string key, double amount)
        => _session.Send(new BreakablePacket(_place!, key, kind, amount));

    private void Receive(RemotePlayer from, BreakablePacket packet)
    {
        if (!Gm.InGame || packet.Place != _place || OurPlayer.State()?.Place != packet.Place)
            return;
        double damage = packet.Kind == BreakableKind.Damage ? packet.Amount : 0;
        double? left = packet.Kind == BreakableKind.State ? packet.Amount : null;
        bool broken = packet.Kind == BreakableKind.Broken;
        Instance found = Instances.All(GameObjectId.o_stuff).FirstOrDefault(s => s.Get("can_broke").AsBool && KeyOf(s) == packet.Key);
        if (!found.IsNone)
        {
            Apply(found, packet.Key, found.Get("HP").AsReal, damage, left, broken);
            return;
        }
        // (Not on screen - or not here at all, broken already: kept for when it is.)
        var was = _pending.GetValueOrDefault(packet.Key);
        _pending[packet.Key] = (was.Damage + damage, left is { } l ? Math.Min(l, was.Left ?? l) : was.Left, was.Broken || broken);
    }

    // A breakable's key: its object and position - the same one in every game, which build the place alike.
    private static string KeyOf(Instance stuff)
        => $"{Gm.ObjectGetName(stuff.Get("object_index").AsInt)}_{Math.Floor(stuff.Get("x").AsReal)}_{Math.Floor(stuff.Get("y").AsReal)}";
}
