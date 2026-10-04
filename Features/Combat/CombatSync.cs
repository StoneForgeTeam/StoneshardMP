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

namespace StoneshardMP.Features.Combat;

// Combat where players are together. Each game resolves the fights its own character is in - it has the real stats,
// gear, buffs and skills - and the host's units are the real ones (AreaUnits):
// - A client's attacks: the client rolls them against its copy of the host's unit, as the game does (scr_attack: hit,
//   dodge, block, crit, its own weapon); what they did - the damage, and whether it left the unit with none - goes to
//   the host, which deals it to the real unit as from that client's stand-in. Whether it dies is the host's to say: the
//   client's copy is kept alive, and goes when the host's roster says so (its corpse and loot come with the host's).
//   Every attack on one of the host's units in a client's game is the client's own: there, their AI is off.
// - The host's units' attacks on a client: its enemies go for the client's stand-in as for the player (it's in the
//   "Player" faction list). Their attack on it isn't resolved on the host: the client's game has its copy of the unit
//   attack the client's character, with the character's real armour, dodge and block.
// - Kills: a unit a client hit that dies on the host is that client's kill too - their game gives them its XP, worked
//   out from their copy of it as the game works it out (o_enemy's Destroy).
// (Still to come: skills and spells, effects, players knocked out.)
public sealed class CombatSync
{
    private readonly ModContext _context;
    private readonly Session _session;
    private readonly AreaUnits _areaUnits;
    private readonly PlayerManager _players;
    private readonly Func<bool> _inSharedWorld;
    // Client: attacks under way (one may start inside another: a counterattack), each on one of the host's units with
    // its health before - or null, an attack on anything else.
    private readonly Stack<(Instance Target, long HostId, double Health)?> _attacks = new();
    // Host: which players (slots) have hit each of our units (by sync id) - its XP is theirs too when it dies.
    private readonly Dictionary<long, HashSet<int>> _hitBy = new();

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
                return true;
            }
            _attacks.Push(Started(call));
            return false;
        });
        Scripts.scr_attack.After(context, _ =>
        {
            if (_attacks.Count > 0 && _attacks.Pop() is { } attack)
                Finished(attack);
        });
        // (A unit's death: o_enemy's Destroy, its children's too - they inherit it.)
        context.OnCode("gml_Object_o_enemy_Destroy_0", before: (self, _) =>
        {
            Destroyed(self);
            return false;
        });
        session.On<UnitHitPacket>(ReceiveHit);
        session.On<EnemyAttackPacket>(ReceiveAttack);
        session.On<UnitKilledPacket>(ReceiveKill);
    }

    public void Clear()
    {
        _attacks.Clear();
        _hitBy.Clear();
    }

    // ---- a client's attacks ----

    // Client: an attack starting - on one of the host's units, its health now.
    private (Instance, long, double)? Started(ScriptCall call)
    {
        if (_session.Mode != Session.SessionMode.Client || !_inSharedWorld() || call.Args.Length < 1)
            return null;
        Instance target = UnitGrid.InstanceOf(call.Args[0]);
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
        Game.CallScript("scr_simple_damage", attacker, unit, hit.Damage, 200);
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
        Instance target = UnitGrid.InstanceOf(call.Args[0]);
        if (target.IsNone || !target.Exists || target.Get("object_index").AsInt != _players.ObjectIndex)
            return false;
        Instance attacker = call.Self;
        // (As scr_attack starts: nothing dealt yet, and the attacker's turn taken - unless it's a forced attack.)
        attacker["attack_result"] = "";
        attacker["is_deal_damage"] = false;
        if (!attacker.Get("force_attack").AsBool)
            Game.CallScript("scr_unitTurnNext", attacker, attacker.Get("visible").AsBool ? Game.Global["TurnDelay"] : 3);
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
        GmValue forced = unit.Get("force_attack");
        unit["force_attack"] = true;
        try
        {
            Game.CallScript("scr_attack", unit, player);
        }
        finally
        {
            if (unit.Exists)
                unit["force_attack"] = forced.IsUndefined ? false : forced;
        }
    }

    // ---- kills ----

    // Host: one of our units destroyed - killed (no health left, and a full destroy: its corpse and loot) - is a kill for
    // every player who hit it.
    private void Destroyed(Instance unit)
    {
        if (_session.Mode != Session.SessionMode.Host || _hitBy.Count == 0 || _areaUnits.SyncIdOf(unit) is not { } unitId
            || !_hitBy.Remove(unitId, out var hitters))
            return;
        if (unit.Get("HP").AsReal > 0 || !unit.Get("is_full_destroy").AsBool)
            return;
        foreach (int slot in hitters)
            _session.Send(new UnitKilledPacket(unitId), slot);
    }

    // Client: a unit we hit was killed - its XP is ours, as the game gives it (o_enemy's Destroy): its gain_xp, less for a
    // weaker tier than our level, through scr_get_XP; logged as the game logs a kill.
    private void ReceiveKill(RemotePlayer from, UnitKilledPacket kill)
    {
        if (_session.Mode != Session.SessionMode.Client || from.Slot != 0 || !Gm.InGame)
            return;
        Instance unit = _areaUnits.LocalOf(kill.UnitId);
        if (unit.IsNone)
        {
            _context.Log($"The host's unit {kill.UnitId} we hit was killed, but we've no copy of it for its XP");
            return;
        }
        double level = Game.CallScript("scr_atr", default, "LVL").AsReal;
        double xp = unit.Get("gain_xp").AsReal * Math.Min(1 - 0.15 * (level / 5 - unit.Get("Tier").AsReal), 1);
        double gained = Game.CallScript("scr_get_XP", default, xp).AsReal;
        if (gained > 0)
        {
            using var name = GmArray.From(new[] { Game.CallScript("scr_actionsLogGetName", default, unit) });
            Game.CallScript("scr_actionsLogXP", default, "death", name, gained);
        }
        _context.Log($"Killed the host's unit {kill.UnitId}: {gained} XP");
    }
}
