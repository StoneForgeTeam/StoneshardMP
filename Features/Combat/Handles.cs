using System;
using StoneForge;

namespace StoneshardMP.Features.Combat;

// What the game hands a script as a unit, kept as one (Instance.Persist) - or none when it isn't an instance.
// The game makes some effects from a struct's method, so what comes as an effect's maker (the call's self, its owner)
// can be a struct, and a struct's handle can't be kept as an instance: Persist throws for it. Asking it for its
// object_index didn't tell it apart (that's how 0.4.1's check let one through), so it's asked to be kept, and a struct is
// none: never our player, our summon or one of the owner's units. Thrown in a script hook every effect, it paused the mod.
internal static class Handles
{
    public static Instance Kept(Instance thing)
    {
        if (thing.IsNone)
            return default;
        try { return thing.Persist(); }
        catch (InvalidOperationException) { return default; }
    }
}
