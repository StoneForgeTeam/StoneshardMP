using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Doors;

// Doors where players are together (legacy: scr_mp_doors_step, scr_mp_door_apply). A door opened or shut - by a player,
// or by the area's NPCs and enemies (StoneForge's Doors.OnChanged) - goes to the others there at once (DoorPacket),
// whose door opens or shuts the game's own way (Doors.SetOpen: its animation, sound and collision). A door one player
// opened is unlocked for the others: they had the key, or picked or broke the lock. Doors are matched by object and
// position: every game builds the same place alike. When players come together, the place's first player (the host if
// it's there, else the lowest slot) sends each of its doors as it first sees it (a door off screen can't be read till
// it's on), and the others' are made to match. A door that's off screen when its change comes gets it when it's next on.
public sealed class DoorSync
{
    // (Every this many frames.)
    private const int Interval = 6;

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    private int _frame;
    // The place we're keeping, and who else was there; the doors seen since (as each last was); changes sent for doors
    // that weren't on screen; whether we're opening or shutting one as another game said (not a change of ours to send).
    private string? _place;
    private string _others = "";
    private readonly Dictionary<string, bool> _known = new();
    private readonly Dictionary<string, bool> _pending = new();
    private bool _applying;

    public DoorSync(ModContext context, Session session, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _inSharedWorld = inSharedWorld;
        session.On<DoorPacket>(Receive);
        StoneForge.Doors.OnChanged(context, Changed);
    }

    public void Clear()
    {
        _place = null;
        _others = "";
        _known.Clear();
        _pending.Clear();
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
        // (A new place, or someone's come: everyone notes their doors afresh, and the first here sends all of theirs.)
        string others = string.Join(",", here);
        bool fresh = place != _place || others != _others;
        if (fresh)
        {
            _place = place;
            _others = others;
            _known.Clear();
            _pending.Clear();
        }
        else if (++_frame % Interval != 0)
            return;
        bool first = _session.Slot < here[0];
        int sent = 0;
        foreach (Instance door in StoneForge.Doors.All())
        {
            string key = KeyOf(door);
            if (_pending.Remove(key, out bool wanted))
            {
                _known[key] = wanted;
                Apply(door, wanted);
                continue;
            }
            if (_known.ContainsKey(key))
                continue;
            // (A door we've only just seen: the first here says how it is. A change after: Changed.)
            bool open = StoneForge.Doors.IsOpen(door);
            _known[key] = open;
            if (first)
            {
                _session.Send(new DoorPacket(place, key, open));
                sent++;
            }
        }
        if (fresh && first && sent > 0)
            _context.Log($"Doors here: sent all {sent} to the others");
    }

    // A door here opening or shutting: to the others, unless it's another game's change we're making.
    private void Changed(Instance door, bool open)
    {
        if (_applying || _place == null || OurPlayer.State()?.Place != _place)
            return;
        string key = KeyOf(door);
        _known[key] = open;
        _session.Send(new DoorPacket(_place, key, open));
    }

    private void Apply(Instance door, bool open)
    {
        _applying = true;
        try { StoneForge.Doors.SetOpen(door, open); }
        finally { _applying = false; }
    }

    private void Receive(RemotePlayer from, DoorPacket door)
    {
        if (!Gm.InGame || OurPlayer.State()?.Place != door.Place)
            return;
        // (As it is now counts as known: not sent back.)
        _known[door.Key] = door.Open;
        Instance found = StoneForge.Doors.All().FirstOrDefault(d => KeyOf(d) == door.Key);
        if (found.IsNone)
            _pending[door.Key] = door.Open;
        else
            Apply(found, door.Open);
    }

    // A door's key: its object and position - the same door in every game, which build the place alike.
    private static string KeyOf(Instance door)
        => $"{Gm.ObjectGetName(door.Get("object_index").AsInt)}_{Math.Floor(door.Get("x").AsReal)}_{Math.Floor(door.Get("y").AsReal)}";
}
