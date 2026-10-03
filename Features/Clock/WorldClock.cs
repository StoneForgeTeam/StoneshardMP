using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using StoneshardMP.Net.Packets;
using StoneForge;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Clock;

// Legacy StoneshardMP's "On move" model, without its timer: one completed normal action is one world turn.
// Clients notify the host; the host serializes those actions and tells every game who caused each one. The actor
// has already advanced through its normal action, while every other player performs one safe idle turn - but a
// client in the host's area doesn't: its units there are the host's (AreaUnits - their AI off, out of its turn loop),
// moved by the host's idle turn and streamed to it, so it only takes the host's clock.
public sealed class WorldClock
{
    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    private readonly Queue<int> _remoteActions = new();
    private bool _tracking;
    private int _turns;
    private int _tick;
    // Client: idle turns owed for other players' actions, run from Tick once the world is ready (never inside the
    // network handler, as the host's are).
    private int _idleTurns;

    public WorldClock(Session session, Func<bool> inSharedWorld)
    {
        _session = session;
        _inSharedWorld = inSharedWorld;
        session.On<WorldActionPacket>(RequestAction);
        session.On<WorldTickPacket>(ReceiveAction);
    }

    public void Clear()
    {
        _remoteActions.Clear();
        _tracking = false;
        _turns = 0;
        _tick = 0;
        _idleTurns = 0;
    }

    public void Tick()
    {
        // (A client making its character isn't in the host's world: its actions aren't the host's turns, nor is the
        // host's clock its intro's.)
        if (!_session.Connected || !_inSharedWorld())
        {
            Clear();
            return;
        }
        int turns = Gml.MpWorldTurns();
        if (turns < 0)
        {
            Clear();
            return;
        }
        if (!_tracking)
        {
            _tracking = true;
            _turns = turns;
            return;
        }

        if (_session.Mode == Session.SessionMode.Host)
        {
            // Publish an action the host took before accepting queued client actions, so simultaneous turns
            // remain two distinct world turns rather than accidentally collapsing into one.
            if (turns != _turns)
                Publish(_session.Slot, turns);
            while (_remoteActions.Count > 0 && Gml.MpWorldTickReady())
            {
                int source = _remoteActions.Dequeue();
                Gml.MpWorldTick();
                Publish(source, Gml.MpWorldTurns());
            }
            return;
        }

        if (turns != _turns)
        {
            _turns = turns;
            _session.Send(new WorldActionPacket(), to: 0);
            return;
        }
        while (_idleTurns > 0 && Gml.MpWorldTickReady())
        {
            _idleTurns--;
            if (!InHostsArea())
                Gml.MpWorldTick();
            _turns = Gml.MpWorldTurns();
        }
    }

    private void RequestAction(RemotePlayer sender, WorldActionPacket packet)
    {
        if (_session.Mode == Session.SessionMode.Host)
            _remoteActions.Enqueue(sender.Slot);
    }

    private void Publish(int source, int turns)
    {
        _turns = turns;
        string clock = Gml.MpWorldClock();
        _tick++;
        _session.Send(new WorldTickPacket(_tick, (byte)source, clock));
    }

    private void ReceiveAction(RemotePlayer sender, WorldTickPacket packet)
    {
        if (_session.Mode != Session.SessionMode.Client || sender.Slot != 0 || !_inSharedWorld())
            return;
        int tick = packet.Tick;
        int source = packet.Source;
        string clock = packet.Clock;
        if (tick <= _tick)
            return;
        _tick = tick;

        // The player who made this action already ran its native global turn. Everyone else mirrors exactly
        // one idle turn, including NPC/unit processing, when their own game is in a safe state - unless this game
        // is in the host's area: the host's turn moved its units, and they come from it.
        if (source != _session.Slot && !InHostsArea())
            _idleTurns++;
        ApplyClock(clock);
    }

    // Whether we're where the host is (the host owns that area's units).
    private bool InHostsArea()
    {
        var host = _session.Players.FirstOrDefault(p => p.Slot == 0);
        var mine = PlayerState.Parse(Gml.MpPlayerState());
        return host?.State != null && mine != null && host.State.Place == mine.Place;
    }

    private static void ApplyClock(string clock)
    {
        string[] fields = clock.Split('|');
        if (fields.Length != 5 ||
            !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) ||
            !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) ||
            !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hours) ||
            !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int days) ||
            !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int months))
            return;
        Gml.MpWorldClockApply(seconds, minutes, hours, days, months);
    }
}
