using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Traps;

// Discovery is a union, not an owner snapshot: any player can spot a trap.
// Keep discoveries for this room, including traps currently culled or not yet created.
public sealed class TrapSync
{
    private const int Interval = 6;
    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    private readonly HashSet<string> _discovered = new();
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
            if (_discovered.Contains(key))
            {
                Reveal(trap);
                continue;
            }
            if (!trap.Get("locate").AsBool && !trap.Get("visible").AsBool)
                continue;
            _discovered.Add(key);
            if (!fresh && others.Length > 0)
                _session.Send(new TrapDiscoveredPacket(place!, key));
        }
        // Everyone sends their discoveries when someone arrives: the owner may not have spotted them.
        if (fresh && others.Length > 0)
        {
            foreach (string key in _discovered)
                _session.Send(new TrapDiscoveredPacket(place!, key));
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
        // Culled instances cannot have their variables written. Their keys remain
        // in _discovered and Tick applies discovery when they reactivate.
        foreach (Instance trap in Instances.All(GameObjectId.o_trap))
        {
            if (KeyOf(trap) == packet.Key)
                Reveal(trap);
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

    private static string KeyOf(Instance trap)
        => $"{Gm.ObjectGetName(trap.Get("object_index").AsInt)}_{Math.Floor(trap.Get("x").AsReal)}_{Math.Floor(trap.Get("y").AsReal)}";
}
