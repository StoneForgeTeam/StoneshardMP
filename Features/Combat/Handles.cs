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

    // GameMaker's instance ids start here: a smaller number is an object's index.
    private const int FirstInstanceId = 100000;

    /// <summary>Who an owner names (a spell's, an effect's, a summon's): an instance or its id - or an object, which names
    /// its first instance. The game's skills are owned by o_player itself (o_skill's Create: owner = o_player), so what
    /// they cast is too - read as an id, it was never our player's, and a follower's Planar Exchange and Wormhole weren't
    /// sent to the owner (#29). None if it names none.</summary>
    public static Instance Owner(GmValue owner)
    {
        if (owner.Kind == GmKind.Real && owner.AsReal >= 0 && owner.AsReal < FirstInstanceId)
            return Game.CallBuiltin("object_exists", owner.AsInt).AsBool
                ? Kept(Game.CallBuiltin("instance_find", owner.AsInt, 0))
                : default;
        return Instance.Of(owner);
    }

    /// <summary>Whether an object is a player's character: o_player, or one of the characters made from it (o_dervish
    /// - Mahir - and the others: the player is always one of these, never o_player itself).</summary>
    public static bool IsPlayerObject(int obj)
        => obj == (int)GameObjectId.o_player || Gm.ObjectIsAncestor(obj, (int)GameObjectId.o_player);
}
