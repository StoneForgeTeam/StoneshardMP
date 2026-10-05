using System;
using System.Linq;
using StoneForge;
using StoneForge.Objects;
using StoneshardMP.Features.Areas;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Players;

// Following another player: "Follow" on their character's right-click menu (StoneForge's ContextMenus) - "Stop
// following" while we do. From our player's Step, as the game moves it (legacy: scr_mp_follow_step, scr_mp_follow_exit):
// - Whenever our character is standing still and they're more than a cell away, it walks to the free cell next to them
//   on our side, as a ground click walks (scr_player_move) - so it keeps to rounds like any other move. The same walk
//   isn't sent again for a moment while the game hasn't started it.
// - When they leave our place we go out the way they did: the door, stairs or entrance nearest the cell we last saw
//   them on (any o_transitions_door within 2.5 cells) - through it if we can reach it (its user event 0), else walked
//   to and through (scr_delay_move_grid, as the game's own "Exit"); with none, they walked off the edge of the area:
//   onto that border cell and across it (scr_playerTileborderTransition). Once there, following carries on.
// It stops on a left click in our window (not on the UI), when an enemy's after us, when we lose them, or when they leave
// the game.
public sealed class Follow
{
    private const long ResendMs = 1500, LeaveGraceMs = 400, LeaveGiveUpMs = 20000, ArriveWaitMs = 6000;
    private const double ExitReach = 2.5 * Cell.Size;

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly PlayerManager _players;
    // Who we follow (null: nobody); the click that chose it (gone by our Step) isn't one to stop it.
    private int? _following;
    private bool _skipClick;
    // The cell we last walked them to, and when; where we last saw them, and in which place; when they left it, and when
    // we came out somewhere they weren't.
    private Cell? _lastSent, _seen;
    private long _sentAt, _goneAt, _arrivedAt;
    private string? _seenIn;
    private int _frame;

    public Follow(ModContext context, Session session, PlayerManager players)
    {
        _context = context;
        _session = session;
        _players = players;
        ContextMenus.OnOpen(context, menu =>
        {
            if (menu.Target.Get("object_index").AsInt != _players.ObjectIndex || menu.Target.Get("mp_slot") is not { Kind: GmKind.Real } slot)
                return;
            string name = NameOf(slot.AsInt);
            if (_following == slot.AsInt)
                menu.Add("Stop following", _ => Set(null), index: 0, hover: $"Stop walking after {name}");
            else
                menu.Add("Follow", _ => Set(slot.AsInt), index: 0, hover: $"Walk after {name} wherever they go - click anywhere to stop");
        });
        // (Our player's Step: where the game moves it.)
        context.OnCode("gml_Object_o_player_Step_0", after: (self, _) => Step(self));
    }

    public void Clear() => Set(null);

    private void Step(Instance me)
    {
        if (_following is not { } slot)
            return;
        if (!_session.Connected || Gm.InMainMenu)
        {
            Set(null);
            return;
        }

        // (Mid room change - going through a door after them: wait.)
        if (Rooms.IsChanging)
            return;
        if (_skipClick)
            _skipClick = false;
        // (A click of ours on the world - not on the game's UI or a mod's, and in our own window: a game in the background
        // sees the clicks made in another, played on the same PC.)
        else if (Mouse.ClickedWorld())
        {
            Set(null);
            return;
        }
        // (A fight's begun - an enemy's after us: interrupted, as the game's own walking is.)
        if (++_frame % 10 == 0 && StoneForge.Player.InCombat)
        {
            Set(null, "an enemy's after us");
            return;
        }
        RemotePlayer? them = _session.Players.FirstOrDefault(p => p.Slot == slot);
        if (them == null)
        {
            Set(null, "they left the game");
            return;
        }
        string? here = OurPlayer.State()?.Place;
        if (them.State is not { } state || state.Place != here)
        {
            Leave(me, them, here);
            return;
        }
        // Where they are in our place - the way out, if they leave.
        _goneAt = _arrivedAt = 0;
        Cell theirs = state.Cell, mine = Units.CellOf(me);
        _seen = theirs;
        _seenIn = here;
        if (mine.DistanceTo(theirs) <= 1 || !Idle(me))
            return;
        // (Not the same walk again every frame while the game hasn't started it - waiting for our turn.)
        long now = Environment.TickCount64;
        if (_lastSent == theirs && now - _sentAt < ResendMs)
            return;
        _lastSent = theirs;
        _sentAt = now;
        // The cell beside them on our side; if that's taken, the nearest free one to it (the game's own search).
        Walk(me, theirs.Offset(Math.Sign(mine.X - theirs.X), Math.Sign(mine.Y - theirs.Y)));
    }

    // They've left our place: out the way they went, or - gone through after them - wait for them there.
    private void Leave(Instance me, RemotePlayer them, string? here)
    {
        long now = Environment.TickCount64;
        if (_goneAt == 0)
            _goneAt = now;
        if (here != _seenIn)
        {
            if (_arrivedAt == 0)
                _arrivedAt = now;
            if (now - _arrivedAt > ArriveWaitMs)
                Set(null, $"lost {them.Name}");
            return;
        }
        // (A moment's grace: their state may just be late.)
        if (now - _goneAt < LeaveGraceMs)
            return;
        if (now - _goneAt > LeaveGiveUpMs || _seen is not { } seen)
        {
            Set(null, $"couldn't follow {them.Name} out");
            return;
        }
        if (now - _sentAt < ResendMs || !Idle(me))
            return;
        _sentAt = now;
        Instance door = Exits.Nearest(seen);
        if (!door.IsNone && door.Exists
            && new Point(door.Get("x").AsReal, door.Get("y").AsReal).DistanceTo(seen.Center) <= ExitReach)
        {
            _context.Log($"Following {them.Name} out by {Gm.ObjectGetName(door.Get("object_index").AsInt)}");
            Exits.Use(door);
            return;
        }
        // No way out there: they walked off the edge of the area - onto that border cell, and across.
        if (Units.CellOf(me) != seen)
        {
            StoneForge.Player.WalkTo(seen);
            return;
        }
        _context.Log($"Following {them.Name} off the edge of the area");
        StoneForge.Player.CrossAreaEdge();
    }

    // A walk to the free cell nearest one (the game's, along the line from us), as a click.
    private static void Walk(Instance me, Cell cell)
    {
        if (Units.NearestFreeCell(me, cell) is { } free)
            StoneForge.Player.WalkTo(free);
    }

    // Standing still: no path, not moving, on its cell, free to move.
    private static bool Idle(Instance me)
        => me.Get("path").AsReal <= 0.5 && !me.Get("is_moving").AsBool && !me.Get("lock_movement").AsBool
            && me.Get("xx").AsReal == me.Get("x").AsReal && me.Get("yy").AsReal == me.Get("y").AsReal;

    private void Set(int? slot, string? why = null)
    {
        if (_following is { } was && was != slot)
            _context.Log($"Stopped following {NameOf(was)}{(why != null ? ": " + why : "")}");
        _following = slot;
        _lastSent = _seen = null;
        _seenIn = null;
        _sentAt = _goneAt = _arrivedAt = 0;
        _skipClick = true;
        if (slot is { } now)
            _context.Log($"Following {NameOf(now)} - click anywhere to stop");
    }

    private string NameOf(int slot) => _session.Players.FirstOrDefault(p => p.Slot == slot)?.Name ?? "them";
}
