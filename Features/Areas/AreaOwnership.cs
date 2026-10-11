using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Areas;

// Who runs each place players share (legacy: scr_mp_area_step, scr_mp_area_set_owner). Players can split up: each game
// runs the place its player is in, as single-player does. Where two or more are together, one of them owns the place -
// whoever got there first, for as long as they stay (then the earliest of the rest) - and the others follow: their
// units, loot and fights are the owner's (AreaUnits, LootSync, CombatSync, TurnRounds). Not always the host: a client
// alone somewhere keeps running it when the host comes, so nothing it did there (a kill, a pickup) comes undone - the
// host's own copy of the place is older.
// The host decides, from when each player came to their place, and tells each client who owns theirs (AreaOwnerPacket)
// when it changes and every second; its own it sets directly. A client that's just moved is alone until it's told.
public sealed class AreaOwnership
{
    private const long ResendMs = 1000;

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    // Host: where each player is (by slot) and since when; each shared place's owner; what each client was last told,
    // and when we last told them all.
    private readonly Dictionary<int, (string Place, long Since)> _arrivals = new();
    private readonly Dictionary<string, int> _owners = new();
    private readonly Dictionary<int, (string Place, byte Owner)> _told = new();
    private long _toldAt;

    public AreaOwnership(ModContext context, Session session, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _inSharedWorld = inSharedWorld;
        session.On<AreaOwnerPacket>(Receive);
    }

    /// <summary>What we are to the place we're in.</summary>
    public AreaRole Role { get; private set; }

    /// <summary>Who runs the place we're in, by slot: us as its owner, another as we follow them; -1 alone.</summary>
    public int Owner { get; private set; } = -1;

    /// <summary>The place our role is for (WorldMap's place string); null outside a shared world.</summary>
    public string? Place { get; private set; }

    /// <summary>Our role changed - or, following, whom we follow: (the role it was, the owner it was).</summary>
    public event Action<AreaRole, int>? Changed;

    /// <summary>The others in our place, by slot.</summary>
    public IEnumerable<int> Others
        => Place == null ? Enumerable.Empty<int>() : _session.Players.Where(p => p.State?.Place == Place).Select(p => p.Slot);

    public void Clear()
    {
        _arrivals.Clear();
        _owners.Clear();
        _told.Clear();
        Set(null, -1);
    }

    public void Tick()
    {
        string? here = OurPlayer.State()?.Place;
        if (!_session.Connected || !_inSharedWorld() || here == null)
        {
            Set(null, -1);
            return;
        }
        if (_session.Mode == Session.SessionMode.Host)
            Decide(here);
        // (A client that's moved: alone there until the host says who runs it.)
        else if (here != Place)
            Set(here, -1);
    }

    // Host: each shared place's owner - the one it had while they stay, else the earliest to arrive - ours set, and each
    // client told theirs.
    private void Decide(string here)
    {
        long now = Environment.TickCount64;
        var places = new Dictionary<int, string> { [_session.Slot] = here };
        foreach (var player in _session.Players)
            if (player.State?.Place is { } place)
                places[player.Slot] = place;
        foreach (int gone in _arrivals.Keys.Where(slot => !places.ContainsKey(slot)).ToList())
            _arrivals.Remove(gone);
        foreach (var (slot, place) in places)
            if (!_arrivals.TryGetValue(slot, out var arrival) || arrival.Place != place)
                _arrivals[slot] = (place, now);
        var owners = new Dictionary<string, int>();
        foreach (var group in places.GroupBy(p => p.Value))
        {
            var slots = group.Select(p => p.Key).ToList();
            if (slots.Count < 2)
                continue;
            owners[group.Key] = _owners.TryGetValue(group.Key, out int kept) && slots.Contains(kept)
                ? kept
                : slots.OrderBy(slot => _arrivals[slot].Since).ThenBy(slot => slot).First();
        }
        _owners.Clear();
        foreach (var (place, owner) in owners)
            _owners[place] = owner;
        Set(here, owners.GetValueOrDefault(here, -1));
        bool resend = now - _toldAt >= ResendMs;
        if (resend)
            _toldAt = now;
        foreach (var (slot, place) in places)
        {
            if (slot == _session.Slot)
                continue;
            byte owner = owners.TryGetValue(place, out int o) ? (byte)o : AreaOwnerPacket.Alone;
            if (!resend && _told.TryGetValue(slot, out var told) && told == (place, owner))
                continue;
            _told[slot] = (place, owner);
            _session.Send(new AreaOwnerPacket(place, owner), slot);
        }
    }

    // Client: who runs our place, from the host - if it's still the place we're in.
    private void Receive(RemotePlayer from, AreaOwnerPacket owner)
    {
        if (_session.Mode != Session.SessionMode.Client || from.Slot != 0 || OurPlayer.State()?.Place != owner.Place)
            return;
        Set(owner.Place, owner.Owner == AreaOwnerPacket.Alone ? -1 : owner.Owner);
    }

    private void Set(string? place, int owner)
    {
        AreaRole role = place == null || owner < 0 ? AreaRole.Alone : owner == _session.Slot ? AreaRole.Owner : AreaRole.Follower;
        bool changed = role != Role || owner != Owner;
        AreaRole was = Role;
        int wasOwner = Owner;
        Place = place;
        Role = role;
        Owner = owner;
        if (!changed)
            return;
        if (place != null)
            _context.Log($"Area {place}: {Describe(role, owner)} (was {Describe(was, wasOwner)})");
        Changed?.Invoke(was, wasOwner);
    }

    private string Describe(AreaRole role, int owner) => role switch
    {
        AreaRole.Owner => "we run it",
        AreaRole.Follower => $"{_session.Players.FirstOrDefault(p => p.Slot == owner)?.Name ?? "slot " + owner} runs it",
        _ => "alone",
    };
}
