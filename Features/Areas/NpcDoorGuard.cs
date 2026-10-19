using System.Collections.Generic;
using System.Linq;
using StoneForge;

// A town NPC shown again (scr_invis_disable_npc): hooked to keep it from crashing the game.
[assembly: HookScript(nameof(Scripts.scr_invis_disable_npc))]

namespace StoneshardMP.Features.Areas;

// A town NPC out of sight - on another floor of an inn, gone through a door - comes back through one of its doors (its
// target_array, o_NPC_target ids its room's creation code gives it), unless it has none, when it's back where it started
// (xstart / ystart). With doors, but none of them in the room, the game's scr_invis_disable_npc has nowhere to put it and
// stops ("I64 argument is unset", in scr_pat_prototype): the host, a client entering the inn a second time. Such an NPC
// has no doors for that call - it's back where it started instead - and its own back after it: a trader's stall
// (c_TraidingSpot) reads its target_array every step, and with none left, the game stopped as the host came into
// Osbrook (#49).
public sealed class NpcDoorGuard
{
    // The NPCs whose doors are set aside for the call under way, and their doors.
    private readonly Stack<(Instance Npc, GmArray? Doors)> _aside = new();

    public NpcDoorGuard(ModContext context)
    {
        Scripts.scr_invis_disable_npc.Before(context, call =>
        {
            _aside.Push((default, null));
            // (Only when it's to pick a door itself: no position given, no door given.)
            if (call.Args.Length > 1 && !(call.Args[1].Kind == GmKind.Real && call.Args[1].AsReal == -4))
                return false;
            if (call.Args.Length > 2 && Instance.Of(call.Args[2]) is { IsNone: false })
                return false;
            Instance npc = call.Self;
            GmArray? doors = npc.Get("target_array").AsArray;
            if (doors == null || doors.Length == 0 || doors.Any(door => Instance.Of(door).Exists))
            {
                doors?.Dispose();
                return false;
            }
            using (GmArray none = GmArray.Create())
                npc["target_array"] = none;
            _aside.Pop();
            _aside.Push((npc.Persist(), doors));
            context.Log($"{Gm.ObjectGetName(npc.Get("object_index").AsInt)} had no door here to come back through: back where it started");
            return false;
        });
        Scripts.scr_invis_disable_npc.After(context, _ =>
        {
            if (_aside.Count == 0 || _aside.Pop() is not (var npc, { } doors))
                return;
            using (doors)
                if (npc.Exists)
                    npc["target_array"] = doors;
        });
    }
}
