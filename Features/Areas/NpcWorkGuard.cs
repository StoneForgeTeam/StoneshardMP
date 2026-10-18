using StoneForge;

// A town NPC's work (scr_npc_work): hooked to keep it from crashing the game.
[assembly: HookScript(nameof(Scripts.scr_npc_work))]

namespace StoneshardMP.Features.Areas;

// A town NPC's work routine (o_village_standing's do_peasant_work, from scr_npc_work) reads is_work when it has no
// working_array to work through - a flag only its room's creation code gives it, and only to those that need it. One
// without its room's setup has neither, and the game stopped: the host, at the mill, "Variable
// o_npc_miller_dog.is_work not set before reading it" (#41). The units we make get it (AreaUnits' RoomDefaults); any
// other without it gets the game's own default, false, before it works.
public sealed class NpcWorkGuard
{
    public NpcWorkGuard(ModContext context)
    {
        Scripts.scr_npc_work.Before(context, call =>
        {
            Instance npc = call.Self;
            if (npc.Exists && npc.Get("is_work").IsUndefined)
            {
                npc["is_work"] = false;
                context.Log($"{Gm.ObjectGetName(npc.Get("object_index").AsInt)} had no work flag: given the game's default");
            }
            return false;
        });
    }
}
