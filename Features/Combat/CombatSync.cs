using System;
using System.Collections.Generic;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

// The game's attack: hooked to see what a client's attacks do to the host's units, and to send the host's units'
// attacks on clients to their games.
[assembly: HookScript(nameof(Scripts.scr_attack))]
// A unit put on another cell (a knockback, a pull), and effects put on it: a client's own, on its copies of the host's
// units, go to the host.
[assembly: HookScript(nameof(Scripts.scr_change_coordinat))]
[assembly: HookScript(nameof(Scripts.scr_effect_create))]
[assembly: HookScript(nameof(Scripts.scr_effect_update))]
// (A knockback - o_knockback's, from an attack or a skill: ours when its owner is our player.)
[assembly: HookScript(nameof(Scripts.scr_knockback))]

namespace StoneshardMP.Features.Combat;

// Combat where players are together. Each game resolves the fights its own character is in - it has the real stats,
// gear, buffs and skills - and the host's units are the real ones (AreaUnits):
// - A client's attacks: the client rolls them against its copy of the host's unit, as the game does (scr_attack: hit,
//   dodge, block, crit, its own weapon); what they did - the damage, and whether it left the unit with none - goes to
//   the host, which deals it to the real unit as from that client's stand-in. Whether it dies is the host's to say: the
//   client's copy is kept alive, and goes when the host's roster says so (its corpse and loot come with the host's).
//   Every attack on one of the host's units in a client's game is the client's own: there, their AI is off.
// - What else a client's own actions do to the host's units goes to the host too, from the game's own scripts for it:
//   a unit put on another cell (scr_change_coordinat) inside our player's attack or a knockback our player owns is
//   moved there on the host (UnitMovedPacket); an effect put on one (scr_effect_create - a stun, a bleed - or refreshed:
//   scr_effect_update) inside our attack or knockback, or one our player owns (a skill's), is put on the real one, from
//   the client's stand-in (UnitEffectPacket). Otherwise the host's unit stays where it was and acts unstunned, and the
//   two games' units drift apart. Only ours: the game moves units and refreshes their own buffs (No Retreat...) for
//   reasons of its own, which are the host's to have. The host's roster then carries both back (AreaUnits: its cells,
//   and the effects on its units).
// - The host's units' attacks on a client: its enemies go for the client's stand-in as for the player (it's in the
//   "Player" faction list). Their attack on it isn't resolved on the host: the client's game has its copy of the unit
//   attack the client's character, with the character's real armour, dodge and block.
// - Kills: XP is shared. When a unit any player took part in dies on the host, every player in that place within 20
//   tiles gets its XP - a client's game works it out from its copy of the unit as the game does (o_enemy's Destroy), and
//   the host's comes from the game's own death code.
// (Still to come: skills' and spells' damage, players knocked out.)
public sealed class CombatSync
{
    // (How near a kill a player gets its share of the XP, in tiles - the game's own reach for its player.)
    private const int KillXpReach = 20;

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly AreaUnits _areaUnits;
    private readonly PlayerManager _players;
    private readonly Func<bool> _inSharedWorld;
    // Client: attacks under way (one may start inside another: a counterattack), each on one of the host's units with
    // its health before - or null, an attack on anything else.
    private readonly Stack<(Instance Target, long HostId, double Health)?> _attacks = new();
    // Host: which players (slots) have hit each of our units (by sync id) - when it dies, a player took part.
    private readonly Dictionary<long, HashSet<int>> _hitBy = new();
    // Client: inside the host's unit's attack on us (ReceiveAttack) - what it does to itself is the host's; and inside a
    // refresh of an effect (scr_effect_update), which makes one when it must - sent as the refresh, not again as made.
    private int _hostsAttack, _refreshing;
    // Client: our player's own action under way - its attack, or a knockback it owns (each pushed as whether it's ours:
    // they nest) - while what it does to the host's units is ours to send.
    private readonly Stack<bool> _actions = new();
    private int _ourActions;

    public CombatSync(ModContext context, Session session, AreaUnits areaUnits, PlayerManager players, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _areaUnits = areaUnits;
        _players = players;
        _inSharedWorld = inSharedWorld;
        Scripts.scr_attack.Before(context, call =>
        {
            if (AttackOnPlayer(call))
            {
                _attacks.Push(null);
                Begin(false);
                return true;
            }
            _attacks.Push(Started(call));
            Begin(IsOurPlayer(call.Self));
            return false;
        });
        Scripts.scr_attack.After(context, _ =>
        {
            End();
            if (_attacks.Count > 0 && _attacks.Pop() is { } attack)
                Finished(attack);
        });
        // (A knockback our player owns: what it does is ours.)
        Scripts.scr_knockback.Before(context, call =>
        {
            Begin(call.Self.Exists && IsOurPlayer(Instance.Of(call.Self.Get("owner"))));
            return false;
        });
        Scripts.scr_knockback.After(context, _ => End());
        // (A unit's death: o_enemy's Destroy, its children's too - they inherit it.)
        context.OnCode("gml_Object_o_enemy_Destroy_0", before: (self, _) =>
        {
            Destroyed(self);
            return false;
        });
        // (A client's own actions on the host's units: where they put them, and the effects they put on them.)
        Scripts.scr_change_coordinat.After(context, CoordinatesChanged);
        Scripts.scr_effect_create.After(context, EffectCreated);
        Scripts.scr_effect_update.Before(context, _ =>
        {
            _refreshing++;
            return false;
        });
        Scripts.scr_effect_update.After(context, call =>
        {
            _refreshing = Math.Max(0, _refreshing - 1);
            EffectRefreshed(call);
        });
        session.On<UnitMovedPacket>(ReceiveMove);
        session.On<UnitEffectPacket>(ReceiveEffect);
        session.On<UnitHitPacket>(ReceiveHit);
        session.On<EnemyAttackPacket>(ReceiveAttack);
        session.On<UnitKilledPacket>(ReceiveKill);
    }

    public void Clear()
    {
        _attacks.Clear();
        _hitBy.Clear();
        _hostsAttack = 0;
        _refreshing = 0;
        _actions.Clear();
        _ourActions = 0;
    }

    // An action starting (whether it's our player's own), and the latest ending.
    private void Begin(bool ours)
    {
        bool counted = ours && _session.Mode == Session.SessionMode.Client;
        _actions.Push(counted);
        if (counted)
            _ourActions++;
    }

    private void End()
    {
        if (_actions.Count > 0 && _actions.Pop())
            _ourActions--;
    }

    // Whether an instance is our own player's character.
    private static bool IsOurPlayer(Instance unit)
        => !unit.IsNone && unit.Exists && OurPlayer.Instance is { IsNone: false } player && unit.Persist().Equals(player.Persist());

    // ---- a client's attacks ----

    // Client: an attack starting - on one of the host's units, its health now.
    private (Instance, long, double)? Started(ScriptCall call)
    {
        if (_session.Mode != Session.SessionMode.Client || !_inSharedWorld() || call.Args.Length < 1)
            return null;
        Instance target = Instance.Of(call.Args[0]);
        if (target.IsNone || !target.Exists || _areaUnits.HostIdOf(target) is not { } hostId)
            return null;
        return (target.Persist(), hostId, target.Get("HP").AsReal);
    }

    // Client: the attack done - what it took off the unit goes to the host, and our copy stays alive till the host
    // says otherwise.
    private void Finished((Instance Target, long HostId, double Health) attack)
    {
        if (!attack.Target.Exists)
            return;
        double health = attack.Target.Get("HP").AsReal, damage = attack.Health - health;
        if (damage <= 0)
            return;
        bool killed = health <= 0;
        if (killed)
            attack.Target["HP"] = 1;
        _session.Send(new UnitHitPacket(attack.HostId, (float)damage, killed));
        _context.Log($"Hit the host's unit {attack.HostId}: {damage} damage{(killed ? ", leaving it none" : "")}");
    }

    // Host: a client's hit, dealt to our unit as the game deals damage (scr_simple_damage: the flash, its morale, its
    // reaction to being hit), from their stand-in - the unit's last attacker, so it turns on them. Left with none, it
    // dies in its next step.
    private void ReceiveHit(RemotePlayer from, UnitHitPacket hit)
    {
        if (_session.Mode != Session.SessionMode.Host || !Gm.InGame)
            return;
        Instance unit = _areaUnits.UnitOf(hit.UnitId);
        if (unit.IsNone)
        {
            _context.Log($"{from.Name} hit unit {hit.UnitId}, which isn't here any more");
            return;
        }
        if (!_hitBy.TryGetValue(hit.UnitId, out var hitters))
            _hitBy[hit.UnitId] = hitters = new();
        hitters.Add(from.Slot);
        Instance attacker = _players.UnitOf(from.Slot);
        if (!attacker.IsNone)
            unit["last_attacker"] = attacker;
        StoneForge.Combat.Hit(unit, hit.Damage, attacker);
        // (Their copy had no health left: neither has ours - the two may have differed by a rounding.)
        if (hit.Killed && unit.Exists && unit.Get("HP").AsReal > 0)
            unit["HP"] = 0;
        _context.Log($"{from.Name} hit unit {hit.UnitId} ({Gm.ObjectGetName(unit.Get("object_index").AsInt)}): {hit.Damage} damage, "
            + $"{unit.Get("HP").AsReal} health left");
    }

    // ---- the host's units attacking clients ----

    // Host: one of our units attacking a player's stand-in - not resolved here (the stand-in has none of their stats):
    // sent to their game, and the unit's turn goes on as the attack would have taken it.
    private bool AttackOnPlayer(ScriptCall call)
    {
        if (_session.Mode != Session.SessionMode.Host || call.Args.Length < 1)
            return false;
        Instance target = Instance.Of(call.Args[0]);
        if (target.IsNone || !target.Exists || target.Get("object_index").AsInt != _players.ObjectIndex)
            return false;
        Instance attacker = call.Self;
        // (As scr_attack starts: nothing dealt yet, and the attacker's turn taken - unless it's a forced attack.)
        attacker["attack_result"] = "";
        attacker["is_deal_damage"] = false;
        if (!attacker.Get("force_attack").AsBool)
            Units.EndTurn(attacker);
        call.Result = 0;
        GmValue slot = target.Get("mp_slot");
        if (slot.IsUndefined || _areaUnits.SyncIdOf(attacker) is not { } unitId)
            return true;
        _session.Send(new EnemyAttackPacket(unitId), slot.AsInt);
        return true;
    }

    // Client: one of the host's units attacks us - our copy of it attacks our character, as a forced attack (its turn
    // is the host's; ours aren't run here).
    private void ReceiveAttack(RemotePlayer from, EnemyAttackPacket attack)
    {
        if (_session.Mode != Session.SessionMode.Client || from.Slot != 0 || !_inSharedWorld() || !Gm.InGame)
            return;
        Instance unit = _areaUnits.LocalOf(attack.UnitId), player = OurPlayer.Instance;
        if (unit.IsNone || player.IsNone)
        {
            _context.Log($"The host's unit {attack.UnitId} attacked us, but {(unit.IsNone ? "we've no copy of it" : "we've no character")}");
            return;
        }
        _hostsAttack++;
        try { StoneForge.Combat.Attack(unit, player, forced: true); }
        finally { _hostsAttack--; }
    }

    // ---- what else a client's actions do to the host's units ----

    // Client: one of the host's units, as our action left it - ours to send, or not (null): the roster's own doing, the
    // host's unit's own attack, or not one of the host's.
    private (Instance Copy, long HostId)? Ours(GmValue unit) => Ours(Instance.Of(unit));

    private (Instance Copy, long HostId)? Ours(Instance copy)
    {
        if (_session.Mode != Session.SessionMode.Client || _areaUnits.Applying || _hostsAttack > 0 || !_inSharedWorld())
            return null;
        copy = copy.IsNone ? copy : copy.Persist();
        if (copy.IsNone || !copy.Exists || _areaUnits.HostIdOf(copy) is not { } hostId)
            return null;
        return (copy, hostId);
    }

    // Client: scr_change_coordinat(x, y, unit = self...) put one of the host's units on another cell - a knockback, a
    // pull: the host moves the real one there.
    private void CoordinatesChanged(ScriptCall call)
    {
        if (_ourActions == 0)
            return;
        Instance unit = call.Args.Length > 2 && !call.Args[2].IsUndefined ? Instance.Of(call.Args[2]) : call.Self;
        if (Ours(unit) is not var (copy, hostId))
            return;
        Cell cell = Units.CellOf(copy);
        _areaUnits.Moved(copy, cell);
        _session.Send(new UnitMovedPacket(hostId, (short)cell.X, (short)cell.Y));
        _context.Log($"Moved the host's unit {hostId} to {cell}");
    }

    // Client: scr_effect_create(effect, duration, target, owner, stage...) put an effect on one of the host's units: the
    // host puts it on the real one. (Made inside a refresh: sent as the refresh.)
    private void EffectCreated(ScriptCall call)
    {
        if (_refreshing > 0 || call.Args.Length < 3 || Instance.Of(call.Result).IsNone)
            return;
        bool ourOwn = call.Args.Length > 3 && IsOurPlayer(Instance.Of(call.Args[3]));
        if ((_ourActions == 0 && !ourOwn) || Ours(call.Args[2]) is not var (copy, hostId))
            return;
        string name = Gm.ObjectGetName(call.Args[0].AsInt);
        double stage = call.Args.Length > 4 && !call.Args[4].IsUndefined ? call.Args[4].AsReal : 1;
        SendEffect(copy, hostId, name, call.Args[1].AsReal, stage, refresh: false);
    }

    // Client: scr_effect_update(effect, target, duration, stacks) refreshed an effect on one of the host's units (or
    // made it): the host does the same to the real one.
    private void EffectRefreshed(ScriptCall call)
    {
        if (_ourActions == 0 || call.Args.Length < 3 || Ours(call.Args[1]) is not var (copy, hostId))
            return;
        string name = Gm.ObjectGetName(call.Args[0].AsInt);
        double stacks = call.Args.Length > 3 && !call.Args[3].IsUndefined ? call.Args[3].AsReal : 1;
        SendEffect(copy, hostId, name, call.Args[2].AsReal, stacks, refresh: true);
    }

    private void SendEffect(Instance copy, long hostId, string name, double duration, double stage, bool refresh)
    {
        _areaUnits.EffectsChanged(copy);
        _session.Send(new UnitEffectPacket(hostId, name, (float)duration, (float)stage, refresh));
        _context.Log($"{(refresh ? "Refreshed" : "Put")} {name} ({duration}) on the host's unit {hostId}");
    }

    // Host: a client's action moved one of our units (in their game) - it's moved there here, onto that cell if it's
    // free (if it isn't, it stays, and our roster puts theirs back).
    private void ReceiveMove(RemotePlayer from, UnitMovedPacket move)
    {
        if (_session.Mode != Session.SessionMode.Host || !Gm.InGame)
            return;
        Instance unit = _areaUnits.UnitOf(move.UnitId);
        if (unit.IsNone)
            return;
        if (!Units.CanTake(unit, move.Cell))
        {
            _context.Log($"{from.Name} moved unit {move.UnitId} to {move.Cell}, which isn't free here");
            return;
        }
        Units.Move(unit, move.Cell);
        _context.Log($"{from.Name} moved unit {move.UnitId} to {move.Cell}");
    }

    // Host: a client's action put an effect on one of our units (in their game) - put on the real one as the game puts
    // one (with its immunities, the target's fortitude), from their stand-in: a new one, or a refresh.
    private void ReceiveEffect(RemotePlayer from, UnitEffectPacket effect)
    {
        if (_session.Mode != Session.SessionMode.Host || !Gm.InGame)
            return;
        Instance unit = _areaUnits.UnitOf(effect.UnitId);
        if (unit.IsNone)
            return;
        if (effect.Refresh)
            UnitEffects.Refresh(effect.Effect, unit, effect.Duration, (int)effect.Stage);
        else
            UnitEffects.Create(effect.Effect, unit, effect.Duration, _players.UnitOf(from.Slot), effect.Stage);
        // (A player's effect on it: their part in its death, should it die of it.)
        if (!_hitBy.TryGetValue(effect.UnitId, out var hitters))
            _hitBy[effect.UnitId] = hitters = new();
        hitters.Add(from.Slot);
        _context.Log($"{from.Name} put {effect.Effect} ({effect.Duration}) on unit {effect.UnitId}{(effect.Refresh ? " (a refresh)" : "")}");
    }

    // ---- kills ----

    // Host: one of our units killed (no health left, and a full destroy: its corpse and loot). When any player took part -
    // a client hit it, or we did as the game counts it (in its damage list, or its last attacker) - its XP is shared, as
    // the GML version shared it: every client in our place within 20 tiles of it gets its own (UnitKilledPacket), and we
    // get ours from the game's own death code - counted in its damage list if only clients hit it.
    private void Destroyed(Instance unit)
    {
        if (_session.Mode != Session.SessionMode.Host || _areaUnits.SyncIdOf(unit) is not { } unitId)
            return;
        _hitBy.Remove(unitId, out var hitters);
        if (unit.Get("HP").AsReal > 0 || !unit.Get("is_full_destroy").AsBool)
            return;
        Instance player = OurPlayer.Instance;
        bool weTookPart = !player.IsNone
            && (StoneForge.Combat.DamageShare(unit, player) > 0
                || Instance.Of(unit.Get("last_attacker")).Equals(player.Persist()));
        if ((hitters == null || hitters.Count == 0) && !weTookPart)
            return;
        Cell cell = Units.CellOf(unit);
        if (!weTookPart && !player.IsNone)
            StoneForge.Combat.AddDamageShare(unit, player);
        string? here = OurPlayer.State()?.Place;
        foreach (var other in _session.Players)
            if (other.State is { } state && state.Place == here
                && state.Cell.DistanceTo(cell) <= KillXpReach)
                _session.Send(new UnitKilledPacket(unitId), other.Slot);
    }

    // Client: a unit was killed near us, with a player's part in it - its XP is ours too, as the game gives it (o_enemy's Destroy): its gain_xp, less for a
    // weaker tier than our level, through scr_get_XP; logged as the game logs a kill.
    private void ReceiveKill(RemotePlayer from, UnitKilledPacket kill)
    {
        if (_session.Mode != Session.SessionMode.Client || from.Slot != 0 || !Gm.InGame)
            return;
        Instance unit = _areaUnits.LocalOf(kill.UnitId);
        if (unit.IsNone)
        {
            _context.Log($"The host's unit {kill.UnitId} was killed near us, but we've no copy of it for its XP");
            return;
        }
        double gained = StoneForge.Player.GiveXp(StoneForge.Player.KillXp(unit), killed: unit);
        _context.Log($"Killed the host's unit {kill.UnitId}: {gained} XP");
    }
}
