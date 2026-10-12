using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Traps;

// Traps where players are together - a union, not an owner snapshot: any player can spot a trap, and any can spend one.
// - Discovered: spotted by anyone, revealed in every game there.
// - Spent: disarmed by anyone (o_trap's alarm 2), or sprung (its animation's end) - is_disarm, in the game's own terms -
//   is harmless in every game there: disarmed, shown spent, off the marks grid (as the game's disarm leaves it). Before,
//   a trap one player disarmed was still live in the other's game.
// - Its XP: shared, as a kill's is (CombatSync) - a player who disarms one says so (o_trap's alarm 2 made it spent, our
//   player its target), and every player in that place within XpReach tiles of it gets its XP, as the game gives it.
// Kept for this room, including traps currently culled or not yet created; everyone sends theirs when someone arrives.
// (Traps players set - their faction's - are PlacedTrapSync's: re-armed, they'd be spent again here.)
public sealed class TrapSync
{
    private const int Interval = 6;
    // (How near a disarmed trap a player gets its XP, in tiles - as near a kill.)
    private const int XpReach = 20;
    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    private readonly HashSet<string> _discovered = new();
    private readonly HashSet<string> _disarmed = new();
    // Traps whose XP we've had (a disarm's said once, but never paid twice), and whether each trap in alarm 2 was spent
    // before it.
    private readonly HashSet<string> _xpTaken = new();
    private readonly Dictionary<Instance, bool> _wasSpent = new();
    private string? _place;
    private string _others = "";
    private int _frame;

    public TrapSync(ModContext context, Session session, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _inSharedWorld = inSharedWorld;
        session.On<TrapDiscoveredPacket>(Receive);
        // A disarm's roll (o_trap's alarm 2, and its children's through event_inherited).
        context.OnCode("gml_Object_o_trap_Alarm_2",
            before: (trap, _) =>
            {
                _wasSpent[trap.Persist()] = trap.Get("is_disarm").AsBool;
                return false;
            },
            after: (trap, _) => Disarmed(trap));
    }

    public void Clear()
    {
        _place = null;
        _others = "";
        _frame = 0;
        _discovered.Clear();
        _disarmed.Clear();
        _xpTaken.Clear();
        _wasSpent.Clear();
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
            if (IsPlayers(trap))
                continue;
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

    // After a disarm's roll: one of the world's traps our player has just disarmed - the others there are told now, its XP
    // theirs too.
    private void Disarmed(Instance trap)
    {
        if (!_wasSpent.Remove(trap.Persist(), out bool wasSpent) || wasSpent || !trap.Get("is_disarm").AsBool || IsPlayers(trap))
            return;
        Instance player = OurPlayer.Instance;
        string? place = OurPlayer.State()?.Place;
        if (player.IsNone || !Instance.Of(trap.Get("target")).Equals(player.Persist()) || !Ready(place))
            return;
        Enter(place!);
        string key = KeyOf(trap);
        _disarmed.Add(key);
        _xpTaken.Add(key);
        if (_session.Players.Any(p => p.State?.Place == place))
            _session.Send(new TrapDiscoveredPacket(place!, key, true, Xp: true));
    }

    // Another player disarmed a trap near us: its XP ours too, as the game gives it (scr_get_XP of its gain_XP), and the
    // actions log says so as it does.
    private void TakeXp(RemotePlayer from, Instance trap, string key)
    {
        if (_xpTaken.Contains(key) || Instances.All(GameObjectId.o_player).FirstOrDefault() is not { IsNone: false } player)
            return;
        var trapCell = new Cell((int)(trap.Get("x").AsReal / 26), (int)(trap.Get("y").AsReal / 26));
        var ourCell = new Cell((int)(player.Get("x").AsReal / 26), (int)(player.Get("y").AsReal / 26));
        if (ourCell.DistanceTo(trapCell) > XpReach || trap.Get("gain_XP") is not { Kind: GmKind.Real } xp)
            return;
        _xpTaken.Add(key);
        double gained = Game.CallScript("scr_get_XP", default, xp).AsReal;
        using (GmArray names = GmArray.From(new[] { (GmValue)from.Name, Game.CallScript("scr_actionsLogGetName", default, trap) }))
            Game.CallScript("scr_actionsLogXP", default, "disarmTrapSuccess", names, gained);
        _context.Log($"{from.Name} disarmed a trap near us: {gained} XP");
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
            if (KeyOf(trap) != packet.Key || IsPlayers(trap))
                continue;
            Reveal(trap);
            if (packet.Disarmed)
                Disarm(trap);
            if (packet.Xp)
                TakeXp(from, trap, packet.Key);
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

    private static bool IsPlayers(Instance trap)
        => trap.Get("faction_key") is { Kind: GmKind.String } faction && faction.AsString == "Player";

    private static string KeyOf(Instance trap)
        => $"{Gm.ObjectGetName(trap.Get("object_index").AsInt)}_{Math.Floor(trap.Get("x").AsReal)}_{Math.Floor(trap.Get("y").AsReal)}";
}
