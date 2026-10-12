using System.Linq;
using StoneForge;

// A town NPC shown again (scr_invis_disable_npc): hooked to keep it from crashing the game.
[assembly: HookScript(nameof(Scripts.scr_invis_disable_npc))]

namespace StoneshardMP.Features.Areas;

// A town NPC out of sight - on another floor of an inn, gone through a door - comes back through one of its doors (its
// target_array, o_NPC_target ids its room's creation code gives it), unless it has none, when it's back where it started
// (xstart / ystart). With doors, but none of them in the room, the game's scr_invis_disable_npc has nowhere to put it and
// stops ("I64 argument is unset", in scr_pat_prototype): the host, a client entering the inn a second time. Such an NPC
// has its doors taken away first - it's back where it started instead.
public sealed class NpcDoorGuard
{
    public NpcDoorGuard(ModContext context)
    {
        Scripts.scr_invis_disable_npc.Before(context, call =>
        {
            // (Only when it's to pick a door itself: no position given, no door given.)
            if (call.Args.Length > 1 && !(call.Args[1].Kind == GmKind.Real && call.Args[1].AsReal == -4))
                return false;
            if (call.Args.Length > 2 && Instance.Of(call.Args[2]) is { IsNone: false })
                return false;
            Instance npc = call.Self;
            using GmArray? doors = npc.Get("target_array").AsArray;
            if (doors == null || doors.Length == 0 || doors.Any(door => Instance.Of(door).Exists))
                return false;
            using (GmArray none = GmArray.Create())
                npc["target_array"] = none;
            context.Log($"{Gm.ObjectGetName(npc.Get("object_index").AsInt)} had no door here to come back through: back where it started");
            return false;
        });
    }
}
