using System;
using System.Linq;
using StoneForge;
using StoneshardMP.Net;

// A world turn (scr_global_turn) and the time it lets pass (scr_timeUpdate): hooked to share a turn's time out.
[assembly: HookScript(nameof(Scripts.scr_global_turn))]
[assembly: HookScript(nameof(Scripts.scr_timeUpdate))]

namespace StoneshardMP.Features.Clock;

// Time shared out among the players: a world turn lets 30 seconds pass (scr_global_turn's scr_timeUpdate(30)), and every
// player's move is one (WorldClock) - so with more players the day went by that many times as fast. Each turn's time is
// 30 seconds over the number of players in the world now: with two, a move is 15 seconds; with three, 10. What's left
// of a second carries to the next turn (the game keeps whole seconds), so time still adds up exactly.
// Not in a round (TurnRounds): there everyone acts and then the world takes one turn - the round's - which keeps its 30.
// Only a turn's own time: sleeping, travelling and the rest let their time pass as ever.
public sealed class TurnTime
{
    private const double SecondsPerTurn = 30;

    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld, _inRound;
    // Whether a world turn's under way (its time is the one shared out), and the part of a second carried over.
    private int _turns;
    private double _carried;

    public TurnTime(ModContext context, Session session, Func<bool> inSharedWorld, Func<bool> inRound)
    {
        _session = session;
        _inSharedWorld = inSharedWorld;
        _inRound = inRound;
        Scripts.scr_global_turn.Before(context, _ =>
        {
            _turns++;
            return false;
        });
        Scripts.scr_global_turn.After(context, _ => _turns = Math.Max(0, _turns - 1));
        Scripts.scr_timeUpdate.Before(context, call =>
        {
            if (Share(call) is not { } seconds)
                return false;
            call.Args[0] = seconds;
            call.Result = Scripts.scr_timeUpdate.CallOriginal(call);
            return true;
        });
    }

    /// <summary>How many players are in the world: us, and everyone else in it (with a state: in the game, not making a
    /// character).</summary>
    public int Players => 1 + _session.Players.Count(p => p.State != null);

    public void Clear()
    {
        _turns = 0;
        _carried = 0;
    }

    // A world turn's own time (its 30 seconds, nothing else) in a world shared with others, out of a round: its share,
    // whole seconds; null to leave the call as it is.
    private double? Share(ScriptCall call)
    {
        if (_turns == 0 || call.Args.Length != 1 || call.Args[0].AsReal != SecondsPerTurn || !_session.Connected
            || !_inSharedWorld() || _inRound())
            return null;
        int players = Players;
        if (players <= 1)
            return null;
        double share = SecondsPerTurn / players + _carried;
        double whole = Math.Floor(share);
        _carried = share - whole;
        return whole;
    }
}
