using System;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

// The game's crime for hitting a town NPC - an attack, an arrow, a thrown item, damage the player dealt: every way in runs
// it as the NPC.
[assembly: HookScript(nameof(Scripts.scr_npc_attack_crime))]

namespace StoneshardMP.Features.Crimes;

// Crimes where players are together: a follower's assault on the owner's NPCs.
// - The game's scr_npc_attack_crime is how an NPC takes being hit: it counts the hits up to its tolerance (warning the
//   player at the last - its "npc_attack_warning" dialogue), then, in a town, sets the faction's crime, calls the guards
//   and turns the NPC and the rest of the town on the player (scr_npc_transition_to_enemy, scr_villagePanicOn). In a
//   follower's game the owner's NPCs are copies with their AI off, so nothing there reacts - and the owner's real NPC
//   took the follower's hit as plain damage (CombatSync), which the game doesn't count as one.
// - So a follower's crime on one of the owner's NPCs goes to the owner (AssaultPacket), which runs it on the real one:
//   its count, the guards, the town's panic. The NPCs turn on the "Player" side, which every player's stand-in is on.
// - The warning is the follower's: the owner's game would show it to the owner's player, so it's taken away there and
//   sent back (AssaultWarningPacket), and the follower's game shows it with its copy of the NPC. Choosing to fight on in
//   it - or hitting the NPC again - comes back as Warned: past the tolerance.
// - The follower's own game runs the crime too, on its copy: its own faction record (wanted, jail) is its player's.
// (The guards head for the owner's player - the game's "move to player" - and fight whoever's on the Player side there.)
public sealed class CrimeSync
{
    private readonly ModContext _context;
    private readonly Session _session;
    private readonly AreaUnits _areaUnits;
    private readonly AreaOwnership _ownership;
    private readonly Func<bool> _inSharedWorld;
    // Owner: running a follower's assault (its warning isn't ours to show).
    private bool _replaying;

    public CrimeSync(ModContext context, Session session, AreaUnits areaUnits, AreaOwnership ownership, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _areaUnits = areaUnits;
        _ownership = ownership;
        _inSharedWorld = inSharedWorld;
        Scripts.scr_npc_attack_crime.Before(context, call =>
        {
            Assaulted(call.Self);
            return false;
        });
        session.On<AssaultPacket>(ReceiveAssault);
        session.On<AssaultWarningPacket>(ReceiveWarning);
    }

    private bool Together => _session.Connected && _inSharedWorld() && Gm.InGame && _ownership.Role != AreaRole.Alone;

    // Follower: our player hit one of the owner's NPCs.
    private void Assaulted(Instance npc)
    {
        if (_replaying || !Together || _ownership.Role != AreaRole.Follower || _areaUnits.Applying || !npc.Exists
            || _areaUnits.OwnerIdOf(npc) is not { } unitId)
            return;
        bool warned = npc.Get("attack_count") is { Kind: GmKind.Real } count && npc.Get("attack_tolerance") is { Kind: GmKind.Real } tolerance
            && count.AsReal >= tolerance.AsReal;
        _session.Send(new AssaultPacket(unitId, warned), _ownership.Owner);
    }

    // Owner: a follower's assault, run on the real NPC as the game runs its own player's.
    private void ReceiveAssault(RemotePlayer from, AssaultPacket packet)
    {
        if (!Together || _ownership.Role != AreaRole.Owner || !_ownership.Others.Contains(from.Slot))
            return;
        Instance npc = _areaUnits.UnitOf(packet.UnitId);
        if (npc.IsNone || !npc.Exists || !Gm.ObjectIsAncestor(npc.Get("object_index").AsInt, (int)GameObjectId.o_NPC))
            return;
        if (packet.Warned && npc.Get("attack_tolerance") is { Kind: GmKind.Real } tolerance
            && npc.Get("attack_count") is { Kind: GmKind.Real } count && count.AsReal < tolerance.AsReal)
            npc.Set("attack_count", tolerance.AsReal);
        var before = Warnings(npc);
        _replaying = true;
        try { Game.CallScript("scr_npc_attack_crime", npc); }
        finally { _replaying = false; }
        // (Its warning, made for the follower: not shown here.)
        bool warn = false;
        foreach (Instance trigger in Warnings(npc).Where(t => !before.Contains(t)))
        {
            trigger.Destroy();
            warn = true;
        }
        if (warn)
            _session.Send(new AssaultWarningPacket(packet.UnitId), from.Slot);
        _context.Log($"{from.Name} assaulted unit {packet.UnitId}{(packet.Warned ? " (warned)" : "")}{(warn ? ": warned them" : "")}");
    }

    // Follower: the NPC warns our player - as the game does, at the end of the turn, with our copy of it.
    private void ReceiveWarning(RemotePlayer from, AssaultWarningPacket packet)
    {
        if (!Together || _ownership.Role != AreaRole.Follower || from.Slot != _ownership.Owner)
            return;
        Instance npc = _areaUnits.LocalOf(packet.UnitId);
        if (npc.IsNone || Warnings(npc).Count > 0)
            return;
        var trigger = Gm.Create<GameInstance>(npc.Get("x").AsReal, npc.Get("y").AsReal, 0, GameObjectId.o_npc_attack_warning_trigger).Instance;
        trigger.Set("owner", npc);
        trigger.Set("dialog_id", "npc_attack_warning");
    }

    // The warnings waiting for an NPC (o_npc_attack_warning_trigger: its owner's dialogue, at the end of the turn).
    private static System.Collections.Generic.List<Instance> Warnings(Instance npc)
        => Instances.All(GameObjectId.o_npc_attack_warning_trigger)
            .Where(t => Instance.Of(t.Get("owner")).Persist().Equals(npc.Persist())).Select(t => t.Persist()).ToList();
}
