using System.Collections.Generic;
using System.Linq;
using StoneForge;

// A town NPC shown again (scr_invis_disable_npc): hooked to keep it from crashing the game.
[assembly: HookScript(nameof(Scripts.scr_invis_disable_npc))]

namespace StoneshardMP.Features.Areas;

// A town NPC out of sight - on another floor of an inn, gone through a door - comes back through one of its doors (its
// target_array, o_NPC_target ids its room's creation code gives it), unless it has none, when it's back where it started
// (xstart / ystart). The game's scr_invis_disable_npc stops ("I64 argument is unset", in scr_pat_prototype) when the door
// it settles on isn't in the room: one it was given, or - picking one itself - the door for the time of day, when no
// open door of its is here to take instead (the host, a client entering the inn a second time; a client once the host
// had left the tavern, #40). Such an NPC has no doors for that call - it's back where it started instead - and its own
// back after it: a trader's stall (c_TraidingSpot) reads its target_array every step, and with none left, the game
// stopped as the host came into Osbrook (#49).
public sealed class NpcDoorGuard
{
    // The NPCs whose doors are set aside for the call under way, and their doors.
    private readonly Stack<(Instance Npc, GmArray? Doors)> _aside = new();

    public NpcDoorGuard(ModContext context)
    {
        Scripts.scr_invis_disable_npc.Before(context, call =>
        {
            _aside.Push((default, null));
            // (Only when it's to come back through a door: no position given.)
            if (call.Args.Length > 1 && !(call.Args[1].Kind == GmKind.Real && call.Args[1].AsReal == -4))
                return false;
            Instance npc = call.Self;
            GmArray? doors = npc.Get("target_array").AsArray;
            if (doors == null || doors.Length == 0 || HasDoor(npc, doors, call.Args.Length > 2 ? call.Args[2] : GmValue.Undefined))
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

    // Whether scr_invis_disable_npc will end on a door that's here: the one it was given; or, picking one itself, the door
    // for the time before now (time_period - 1) if it's here, else any open one of its (image_index 1) it takes instead.
    private static bool HasDoor(Instance npc, GmArray doors, GmValue given)
    {
        if (Instance.Of(given) is { IsNone: false } door)
            return door.Exists;
        int time = npc.Get("time_period").AsInt - 1;
        if (time < 0)
            time = 3;
        if (time < doors.Length && Instance.Of(doors[time]).Exists)
            return true;
        return doors.Any(d => Instance.Of(d) is { IsNone: false } here && here.Exists && here.Get("image_index").AsReal == 1);
    }
}
