using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Traps;

// Traps where players are together - a union, not an owner snapshot: any player can spot a trap, and any can spend one.
// - Discovered: spotted by anyone, revealed in every game there.
// - Spent: disarmed by anyone (o_trap's alarm 2), or sprung (its animation's end) - is_disarm, in the game's own terms -
//   is harmless in every game there: disarmed, shown spent, off the marks grid (as the game's disarm leaves it). Before,
//   a trap one player disarmed was still live in the other's game.
// Kept for this room, including traps currently culled or not yet created; everyone sends theirs when someone arrives.
public sealed class TrapSync
{
    private const int Interval = 6;
    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    private readonly HashSet<string> _discovered = new();
    private readonly HashSet<string> _disarmed = new();
    private string? _place;
    private string _others = "";
    private int _frame;

    public TrapSync(Session session, Func<bool> inSharedWorld)
    {
        _session = session;
        _inSharedWorld = inSharedWorld;
        session.On<TrapDiscoveredPacket>(Receive);
    }

    public void Clear()
    {
        _place = null;
        _others = "";
        _frame = 0;
        _discovered.Clear();
        _disarmed.Clear();
    }

    public void Tick()
    {
        string? place = OurPlayer.State()?.Place;
        if (!Ready(place))
        {
            if (!_session.Connected || !_inSharedWorld() || !Gm.InGame)
                Clear();
            return;
        }
        Enter(place!);
        string others = string.Join(",", _session.Players
            .Where(p => p.State?.Place == place).Select(p => p.Slot).OrderBy(s => s));
        bool fresh = others != _others;
        _others = others;
        if (!fresh && ++_frame % Interval != 0)
            return;

        foreach (Instance trap in Instances.All(GameObjectId.o_trap))
        {
            string key = KeyOf(trap);
            // Spent: ours to tell, or another's to make so here.
            if (trap.Get("is_disarm").AsBool)
            {
                if (_disarmed.Add(key) && !fresh && others.Length > 0)
                    _session.Send(new TrapDiscoveredPacket(place!, key, true));
            }
            else if (_disarmed.Contains(key))
                Disarm(trap);
            if (_discovered.Contains(key))
            {
                Reveal(trap);
                continue;
            }
            if (!trap.Get("locate").AsBool && !trap.Get("visible").AsBool)
                continue;
            _discovered.Add(key);
            if (!fresh && others.Length > 0)
                _session.Send(new TrapDiscoveredPacket(place!, key, false));
        }
        // Everyone sends what they know when someone arrives: the owner may not have spotted or spent them.
        if (fresh && others.Length > 0)
        {
            foreach (string key in _discovered)
                _session.Send(new TrapDiscoveredPacket(place!, key, false));
            foreach (string key in _disarmed)
                _session.Send(new TrapDiscoveredPacket(place!, key, true));
        }
    }

    private bool Ready(string? place)
        => _session.Connected && _inSharedWorld() && Gm.InGame && place != null && !Rooms.IsChanging;

    private void Enter(string place)
    {
        if (_place == place)
            return;
        Clear();
        _place = place;
    }

    private void Receive(RemotePlayer from, TrapDiscoveredPacket packet)
    {
        string? place = OurPlayer.State()?.Place;
        // The packet carries its area: a reliable discovery can arrive before
        // the sender's next player-state update after a room transition.
        if (!Ready(place) || packet.Place != place)
            return;
        Enter(place!);
        _discovered.Add(packet.Key);
        if (packet.Disarmed)
            _disarmed.Add(packet.Key);
        // Culled instances cannot have their variables written. Their keys remain
        // in the sets and Tick applies them when they reactivate.
        foreach (Instance trap in Instances.All(GameObjectId.o_trap))
        {
            if (KeyOf(trap) != packet.Key)
                continue;
            Reveal(trap);
            if (packet.Disarmed)
                Disarm(trap);
        }
    }

    private static void Reveal(Instance trap)
    {
        // scr_trap_find's discovery flags, without running another perception roll,
        // awarding discovery statistics, or triggering/disarming the trap.
        trap.Set("locate", true);
        if (!trap.Get("is_spawner").AsBool)
            trap.Set("visible", true);
    }

    // Spent here as another game spent it - as o_trap's alarm 2 leaves a disarmed one, without its roll, sound or speech:
    // disarmed, its last frame (user event 9 shows a spent trap so), and off the marks grid, which steers units around it.
    private static void Disarm(Instance trap)
    {
        if (trap.Get("is_disarm").AsBool)
            return;
        trap.Set("is_disarm", true);
        trap.Set("image_speed", 0);
        Game.CallBuiltinAs("event_user", trap, trap, 9);
        if (Instances.All(GameObjectId.o_controller).FirstOrDefault() is { IsNone: false } controller
            && controller.Get("markgrid").Kind == GmKind.Real)
            Game.CallBuiltin("ds_grid_set", controller.Get("markgrid"), trap.Get("grid_x"), trap.Get("grid_y"), -4);
    }

    private static string KeyOf(Instance trap)
        => $"{Gm.ObjectGetName(trap.Get("object_index").AsInt)}_{Math.Floor(trap.Get("x").AsReal)}_{Math.Floor(trap.Get("y").AsReal)}";
}
