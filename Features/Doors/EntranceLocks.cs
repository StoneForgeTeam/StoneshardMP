using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Doors;

// Building entrances' locks where players are together (o_villageDoorsEnter: the granary's, a house's - going in is a
// room change, so DoorSync's open and shut aren't theirs). One player unlocking one - with its key, or picking it - left
// it locked in the others' games; and the place's saved state is its owner's, so in theirs it stayed locked for good: the
// key was needed every time. Now an entrance seen locked and then unlocked here is unlocked in every game there - the
// owner's too, which saves it so. Unlocked wins (as spent traps do): everyone sends theirs when someone arrives.
public sealed class EntranceLocks
{
    private const int Interval = 15;

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    // The place we're keeping, and who else is there; its entrances seen locked, and those unlocked since - by us, or
    // another game.
    private string? _place;
    private string _others = "";
    private readonly HashSet<string> _locked = new();
    private readonly HashSet<string> _unlocked = new();
    private int _frame;

    public EntranceLocks(ModContext context, Session session, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _inSharedWorld = inSharedWorld;
        session.On<EntranceUnlockedPacket>(Receive);
    }

    public void Clear()
    {
        _place = null;
        _others = "";
        _locked.Clear();
        _unlocked.Clear();
        _frame = 0;
    }

    public void Tick()
    {
        string? place = OurPlayer.State()?.Place;
        if (!_session.Connected || !_inSharedWorld() || !Gm.InGame || place == null || Rooms.IsChanging)
        {
            if (_place != null)
                Clear();
            return;
        }
        Enter(place);
        string others = string.Join(",", _session.Players.Where(p => p.State?.Place == place).Select(p => p.Slot).OrderBy(s => s));
        bool fresh = others != _others;
        _others = others;
        if (!fresh && ++_frame % Interval != 0)
            return;
        foreach (Instance entrance in Instances.All(GameObjectId.o_villageDoorsEnter))
        {
            // (Off screen its own variables can't be read: it's seen when it's back.)
            if (entrance.Get("is_lock") is not { Kind: GmKind.Real } lockValue)
                continue;
            string key = KeyOf(entrance);
            if (lockValue.AsReal > 0)
            {
                if (_unlocked.Contains(key))
                    Unlock(entrance);
                else
                    _locked.Add(key);
            }
            else if (_locked.Remove(key) && _unlocked.Add(key))
            {
                _context.Log($"Unlocked {key} here: to the others");
                if (others.Length > 0)
                    _session.Send(new EntranceUnlockedPacket(place, key));
            }
        }
        // (Someone's come: what's been unlocked here, theirs too.)
        if (fresh && others.Length > 0)
            foreach (string key in _unlocked)
                _session.Send(new EntranceUnlockedPacket(place, key));
    }

    private void Enter(string place)
    {
        if (_place == place)
            return;
        Clear();
        _place = place;
    }

    private void Receive(RemotePlayer from, EntranceUnlockedPacket packet)
    {
        string? place = OurPlayer.State()?.Place;
        if (!Gm.InGame || place == null || packet.Place != place || Rooms.IsChanging)
            return;
        Enter(place);
        _locked.Remove(packet.Key);
        if (!_unlocked.Add(packet.Key))
            return;
        foreach (Instance entrance in Instances.All(GameObjectId.o_villageDoorsEnter))
            if (KeyOf(entrance) == packet.Key && entrance.Get("is_lock") is { Kind: GmKind.Real } lockValue && lockValue.AsReal > 0)
            {
                Unlock(entrance);
                _context.Log($"{from.Name} unlocked {packet.Key}: unlocked here");
            }
    }

    // Unlocked as the key leaves it (o_skill_open_door_quest: its locks down to none).
    private static void Unlock(Instance entrance) => entrance["is_lock"] = 0;

    // An entrance's key: its object and position - the same entrance in every game, which build the place alike.
    private static string KeyOf(Instance entrance)
        => $"{Gm.ObjectGetName(entrance.Get("object_index").AsInt)}_{Math.Floor(entrance.Get("x").AsReal)}_{Math.Floor(entrance.Get("y").AsReal)}";
}
