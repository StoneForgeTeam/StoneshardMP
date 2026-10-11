using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Clock;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

// How long the player is busy (the game asks before every action): the gate on our turn in a round.
[assembly: HookScript(nameof(Scripts.scr_unitTurnGetTime))]

namespace StoneshardMP.Features.Rounds;

// Shared turn-based rounds (legacy: scr_mp_round_mode, scr_mp_turn_hold, scr_mp_hold_enemy_phase, scr_mp_tick_step's
// rounds). Where two or more players share a place and any of them needs turns - in combat, bleeding to death, on fire
// (TurnReasons) - play there goes in rounds instead of each action being a world turn: each player in turn, in slot
// order, one action each, then the enemies, all together. The place's owner (AreaOwnership) runs the rounds - its units
// are the real ones - and tells the others there where they are (RoundPacket); each of them tells it why they need turns
// and when they've acted (TurnStatusPacket).
// - The gate: until it's our turn, and once we've taken it, the game counts the player as busy (scr_unitTurnGetTime):
//   no walking, attacking, skipping or using anything.
// - The enemies: the owner's own action would set them off at once (its player's alarm 4: the units' turns). It's held
//   till every player has acted; then they all move. A round where the owner was skipped has its world turn run then.
// - A player who hasn't acted in 30 s is skipped, so nobody holds a round up for good.
// - Others' actions aren't world turns of their own in a place with a round: the round's is (WorldClock asks Active) - a
//   player elsewhere would otherwise move the round's enemies mid-round.
// A new owner (the old one left mid-round) starts the rounds afresh.
public sealed class TurnRounds
{
    private const long TimeoutMs = 30000, StatusEveryMs = 1000;

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly AreaOwnership _ownership;
    private readonly Func<bool> _inSharedWorld;

    // Everyone: our reason (read every few frames), the last round we acted in, the game's turn count last seen.
    private TurnReason _reason;
    private int _actedRound = -1, _turns = -1, _frame;
    // Follower: what we last told the owner, and when.
    private (TurnReason, int) _told;
    private long _toldAt;
    // Owner: each follower's reason and last round acted in; who was skipped this round; when the round last moved on;
    // whether the enemies' turn has begun (and when); what was last sent, and when.
    private readonly Dictionary<int, (TurnReason Reason, int ActedRound)> _status = new();
    private readonly HashSet<int> _skipped = new();
    private long _progressAt, _sentAt, _enemiesAt;
    private int _actedCount;
    private bool _enemiesTurn;
    private string _sent = "";

    public TurnRounds(ModContext context, Session session, AreaOwnership ownership, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _ownership = ownership;
        _inSharedWorld = inSharedWorld;
        ownership.Changed += (_, _) => Restart();
        Scripts.scr_unitTurnGetTime.After(context, call =>
        {
            if (GateShut && call.Result.AsReal <= 0)
                call.Result = 1;
        });
        // (The units' turns: the player's alarm 4 - held, while the owner waits for the others, a frame at a time.)
        context.OnCode("gml_Object_o_player_Alarm_4", before: (self, _) =>
        {
            if (!HoldEnemies(self))
                return false;
            self.Alarm[4] = 1;
            return true;
        });
        session.On<TurnStatusPacket>(ReceiveStatus);
        session.On<RoundPacket>(ReceiveRound);
    }

    /// <summary>Whether rounds are on in our place (as its owner last said, or as we run them).</summary>
    public bool Active { get; private set; }

    /// <summary>The round's number.</summary>
    public int Round { get; private set; }

    /// <summary>The players in the round, in turn order.</summary>
    public IReadOnlyList<RoundSeat> Seats { get; private set; } = Array.Empty<RoundSeat>();

    /// <summary>Whose turn it is: the first player who hasn't acted; null once all have - the enemies'.</summary>
    public int? Current
    {
        get
        {
            foreach (var seat in Seats)
                if (!seat.Acted)
                    return seat.Slot;
            return null;
        }
    }

    /// <summary>Whether we're in the round.</summary>
    public bool InRound => Active && Seats.Any(s => s.Slot == _session.Slot);

    /// <summary>Whether a player is in the round (their actions aren't world turns of their own meanwhile).</summary>
    public bool Plays(int slot) => Active && Seats.Any(s => s.Slot == slot);

    /// <summary>Why we need turns.</summary>
    public TurnReason Reason => _reason;

    // The gate: in the round, and it isn't our turn - someone's before us, we've taken ours, or it's the enemies'.
    private bool GateShut => InRound && Current != _session.Slot;

    public void Clear()
    {
        Active = false;
        Round = 0;
        Seats = Array.Empty<RoundSeat>();
        _reason = TurnReason.None;
        _actedRound = -1;
        _turns = -1;
        _told = default;
        _status.Clear();
        _skipped.Clear();
        _enemiesTurn = false;
        _sent = "";
    }

    // Our role in the place changed (a new place, or a new owner): no round here until its owner says.
    private void Restart()
    {
        if (Active)
            _context.Log("Rounds over (the place's owner changed)");
        Active = false;
        Seats = Array.Empty<RoundSeat>();
        _told = default;
        _status.Clear();
        _skipped.Clear();
        _enemiesTurn = false;
        _sent = "";
    }

    public void Tick()
    {
        if (!_session.Connected || !_inSharedWorld() || !Gm.InGame || OurPlayer.Instance.IsNone)
        {
            if (Active || _turns >= 0)
                Clear();
            return;
        }
        if (++_frame % 10 == 0)
            _reason = TurnReasons.Ours();
        // (An action on our turn - the game counted a turn: we've acted this round.)
        int turns = GameClock.Turns();
        if (turns != _turns)
        {
            if (_turns >= 0 && InRound && Current == _session.Slot)
                Acted();
            _turns = turns;
        }
        if (_ownership.Role == AreaRole.Owner)
            RunRounds();
        else if (_ownership.Role == AreaRole.Follower)
            TellOwner();
    }

    private void Acted()
    {
        if (_actedRound == Round)
            return;
        _actedRound = Round;
        _context.Log($"Round {Round}: acted");
        if (_ownership.Role == AreaRole.Follower)
            TellOwner(force: true);
    }

    // ---- follower ----

    private void TellOwner(bool force = false)
    {
        long now = Environment.TickCount64;
        var status = (_reason, _actedRound);
        if (!force && status == _told && now - _toldAt < StatusEveryMs)
            return;
        _told = status;
        _toldAt = now;
        _session.Send(new TurnStatusPacket((byte)_reason, _actedRound), to: _ownership.Owner);
    }

    private void ReceiveRound(RemotePlayer from, RoundPacket round)
    {
        if (_ownership.Role != AreaRole.Follower || from.Slot != _ownership.Owner)
            return;
        if (round.Active && (!Active || round.Round != Round))
            _context.Log($"Round {round.Round}: {string.Join(", ", round.Seats.Select(s => s.Slot))}, then the enemies");
        Active = round.Active;
        Round = round.Round;
        Seats = round.Seats;
    }

    // ---- owner ----

    private void ReceiveStatus(RemotePlayer from, TurnStatusPacket status)
    {
        if (_ownership.Role == AreaRole.Owner)
            _status[from.Slot] = ((TurnReason)status.Reason, status.ActedRound);
    }

    // Owner: the round in our place - who's in it, who's acted, the enemies' turn, the next round.
    private void RunRounds()
    {
        long now = Environment.TickCount64;
        var here = _ownership.Others.ToList();
        foreach (int gone in _status.Keys.Where(slot => !_session.Players.Any(p => p.Slot == slot)).ToList())
            _status.Remove(gone);
        TurnReason ReasonOf(int slot) => slot == _session.Slot ? _reason : _status.GetValueOrDefault(slot).Reason;
        var slots = here.Prepend(_session.Slot).OrderBy(s => s).ToList();
        bool active = slots.Count >= 2 && slots.Any(s => ReasonOf(s) != TurnReason.None);
        if (!active)
        {
            if (Active)
                _context.Log("Rounds over");
            Active = false;
            Seats = Array.Empty<RoundSeat>();
            _enemiesTurn = false;
            Broadcast(now);
            return;
        }
        if (!Active)
        {
            Active = true;
            NewRound(now);
        }
        bool ActedIn(int slot) => _skipped.Contains(slot)
            || (slot == _session.Slot ? _actedRound == Round : (_status.TryGetValue(slot, out var told) ? told.ActedRound : -1) == Round);
        Seats = slots.Select(s => new RoundSeat(s, (byte)ReasonOf(s), ActedIn(s))).ToArray();
        int acted = Seats.Count(s => s.Acted);
        if (acted != _actedCount)
        {
            _actedCount = acted;
            _progressAt = now;
        }
        if (acted < Seats.Count)
        {
            // (Waited long enough for whoever's turn it is: skipped.)
            if (now - _progressAt > TimeoutMs && Current is { } late)
            {
                _skipped.Add(late);
                _context.Log($"Round {Round}: {NameOf(late)} didn't act in {TimeoutMs / 1000} s - skipped");
                _progressAt = now;
            }
        }
        else if (!_enemiesTurn)
        {
            // Everyone's acted: the enemies' turn - our held alarm 4 goes now; or, if we were skipped, a world turn.
            _enemiesTurn = true;
            _enemiesAt = now;
            Instance player = OurPlayer.Instance;
            if (_skipped.Contains(_session.Slot) && Math.Max(player.Alarm[4], player.Alarm[1]) <= 0)
                GameClock.Tick(_context);
        }
        else if (now - _enemiesAt > 100 && GameClock.TickReady())
            // (Their turn's over - everything it set off has finished: the next round.)
            NewRound(now);
        Broadcast(now);
    }

    private void NewRound(long now)
    {
        Round++;
        _skipped.Clear();
        _enemiesTurn = false;
        _actedCount = 0;
        _progressAt = now;
        _context.Log($"Round {Round}");
    }

    // Owner: whether the units' turn (our player's alarm 4, about to run) waits - in a round, for the others to act. Our
    // own action set it off: we've acted. (Never mid-loop: only as it starts, enemy_iteration 0.)
    private bool HoldEnemies(Instance player)
    {
        if (_ownership.Role != AreaRole.Owner || !InRound || _enemiesTurn || player.Get("enemy_iteration").AsReal != 0)
            return false;
        if (_actedRound != Round && !_skipped.Contains(_session.Slot))
            Acted();
        return Seats.Any(s => s.Slot != _session.Slot && !s.Acted);
    }

    private void Broadcast(long now)
    {
        string text = $"{Active}|{Round}|{string.Join(",", Seats)}";
        if (text == _sent && now - _sentAt < StatusEveryMs)
            return;
        _sent = text;
        _sentAt = now;
        var packet = new RoundPacket(Active, Round, Seats.ToArray());
        foreach (int follower in _ownership.Others)
            _session.Send(packet, follower);
    }

    /// <summary>A player's name: ours, or another's.</summary>
    public string NameOf(int slot) => slot == _session.Slot ? "You" : _session.Players.FirstOrDefault(p => p.Slot == slot)?.Name ?? "?";
}
