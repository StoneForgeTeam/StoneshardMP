using System;
using System.Collections.Generic;
using System.Linq;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Join;

// Host: which of the world's player slots each player in the session plays (as Divinity's lobby does). The host's world
// keeps the other players' characters by world slot (1, 2...: JoinSave), not by name - and their own stash in the chest by
// the bed (PersonalStash). The host is slot 0 (the world's own character). A client plays the slot of the order it
// joined in (its session slot) unless the host swaps it, on the main menu's player list (PlayersPanel): to the next slot,
// trading places with whoever has that one. Only a player still waiting to be let in (JoinManager: the host not in its
// world yet) - not one that's been sent its character, nor into such a one's slot: what they play is decided then. For
// this session; joining again, it's the order again.
// The host can pick another slot too (HostSlot), on the main menu: the save it loads next has the host's character and
// that slot's traded (JoinSave.SwapHost) - the host plays that one, and whoever plays the slot gets the host's. So a
// client given slot 0 (Pick) is that trade: the host takes the client's slot, and the client the host's character.
public sealed class WorldSlots
{
    private readonly Session _session;
    // Session slot -> world slot, for those swapped (the rest play their own).
    private readonly Dictionary<int, int> _swapped = new();

    public WorldSlots(Session session)
    {
        _session = session;
        session.Changed += () =>
        {
            if (session.Mode != Session.SessionMode.Host)
            {
                _swapped.Clear();
                HostSlot = 0;
            }
        };
    }

    /// <summary>Whether a client (by session slot) is still waiting to be let in (JoinManager says).</summary>
    public Func<int, bool> Waiting { get; set; } = _ => false;

    /// <summary>Host: the slot whose character we'll play in the save we load next (0: our own) - traded with ours as it
    /// loads, then back to 0 (it's ours then).</summary>
    public int HostSlot { get; private set; }

    /// <summary>Host: the next slot to play (0 after the last).</summary>
    public void SwapHost()
    {
        HostSlot = (HostSlot + 1) % Math.Max(1, _session.Limit);
        Changed?.Invoke();
    }

    /// <summary>Host: the save's loaded, with the slot's character ours now.</summary>
    public void HostLoaded()
    {
        HostSlot = 0;
        Changed?.Invoke();
    }

    /// <summary>Which slot's character a player plays, as the lobby shows it: the host's pick (HostSlot); a client's
    /// world slot - or 0 when the host's taking theirs (the client gets the host's character).</summary>
    public int Plays(int sessionSlot)
    {
        if (sessionSlot == 0)
            return HostSlot;
        int world = Of(sessionSlot);
        return HostSlot != 0 && world == HostSlot ? 0 : world;
    }

    /// <summary>Host: a player (by session slot) is to play this slot's character - the lobby's dropdown. The host: its
    /// pick, for the save it loads next. A client (still waiting to be let in): slot 0 is the host's character, the host
    /// taking theirs; another moves them there, trading with whoever has it (not one already let in). Whether it was
    /// done.</summary>
    public bool Pick(int sessionSlot, int target)
    {
        int count = Math.Max(1, _session.Limit);
        if (target < 0 || target >= count)
            return false;
        if (sessionSlot == 0)
        {
            HostSlot = target;
            Changed?.Invoke();
            return true;
        }
        if (!CanSwap(sessionSlot))
            return false;
        int from = Of(sessionSlot);
        if (target == 0)
        {
            HostSlot = from;
            Changed?.Invoke();
            return true;
        }
        // (Off the host's character first, if they had it.)
        if (HostSlot == from)
            HostSlot = 0;
        if (target != from)
        {
            int holder = HolderOf(target);
            if (holder >= 0 && Settled(holder))
                return false;
            // (A slot nobody here plays is its session slot's: whoever joins as that one later takes the slot left.)
            Set(sessionSlot, target);
            Set(holder >= 0 ? holder : target, from);
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Someone's slot changed (a swap).</summary>
    public event Action? Changed;

    /// <summary>The world slot a player in the session plays (by their session slot).</summary>
    public int Of(int sessionSlot) => _swapped.TryGetValue(sessionSlot, out int slot) ? slot : sessionSlot;

    /// <summary>Whether a player can be moved: a client still waiting to be let in.</summary>
    public bool CanSwap(int sessionSlot) => sessionSlot > 0 && Waiting(sessionSlot);

    /// <summary>Moves a client to the next world slot (1 after the last), trading places with whoever plays it - skipping
    /// one that's been let in already. Their new slot (their old one if there's nowhere to go).</summary>
    public int Swap(int sessionSlot)
    {
        int count = Math.Max(1, _session.Limit - 1), from = Of(sessionSlot);
        if (!CanSwap(sessionSlot))
            return from;
        for (int step = 1; step < count + 1; step++)
        {
            int to = (from - 1 + step) % count + 1;
            if (to == from)
                break;
            int holder = HolderOf(to);
            if (holder >= 0 && Settled(holder))
                continue;
            // (A slot nobody here plays is its session slot's: whoever joins as that one later takes the slot left.)
            Set(sessionSlot, to);
            Set(holder >= 0 ? holder : to, from);
            Changed?.Invoke();
            return to;
        }
        return from;
    }

    // Who plays a world slot: the session slot it's assigned to (-1: none that's here). One that's free with nobody's
    // swapped to it is its own session slot's - if that player's here.
    private int HolderOf(int worldSlot)
    {
        foreach (int slot in Present())
            if (Of(slot) == worldSlot)
                return slot;
        return -1;
    }

    // The session slots of the clients here, and any swapped who've left (their slot still taken this session).
    private IEnumerable<int> Present() => _session.Players.Select(p => p.Slot).Concat(_swapped.Keys).Where(s => s > 0).Distinct();

    private void Set(int sessionSlot, int worldSlot)
    {
        if (worldSlot == sessionSlot)
            _swapped.Remove(sessionSlot);
        else
            _swapped[sessionSlot] = worldSlot;
    }

    // A player here who's been let in (their slot's decided); not one who's left, nor one still waiting.
    private bool Settled(int sessionSlot) => _session.Players.Any(p => p.Slot == sessionSlot) && !Waiting(sessionSlot);

    public void Clear()
    {
        _swapped.Clear();
        HostSlot = 0;
    }
}
