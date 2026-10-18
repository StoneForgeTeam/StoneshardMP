using StoneForge;

// A unit made to fight back (scr_stateAttackAbs): hooked, for Determination's strikes on the owner's units.
[assembly: HookScript(nameof(Scripts.scr_stateAttackAbs))]

namespace StoneshardMP.Features.Combat;

// Follower: our player's Determination (Intransigence) strikes what comes into its marks - the cells around us. The game
// strikes from the unit's own turn: a unit standing on a mark when its turn's events run (scr_unitTurnGetDamageFromTileMark)
// has the mark's event 7 run, which strikes it (event 11: our player's spear attack). Where we follow, the area's units
// are the owner's, copies here out of our turns - none ever took a turn here, and the skill never struck (#37). A copy
// the owner's roster puts on one of our marks has it run here as its turn would have: the strike is ours, sent to the
// owner as any attack of ours on its units is (CombatSync). The mark then makes what it struck fight back
// (scr_stateAttackAbs, its AI run there and then): not a copy - the owner runs it.
internal sealed class MarkStrikes
{
    private static ModContext? _context;
    private static bool _striking;

    public MarkStrikes(ModContext context)
    {
        _context = context;
        Scripts.scr_stateAttackAbs.Before(context, _ => _striking);
    }

    public static void Arrived(Instance unit, Cell cell)
    {
        var (x, y) = cell;
        foreach (Instance mark in Instances.All(GameObjectId.o_intransigence_mark))
        {
            // (As the turn's events do: a mark not held back - delay - on the unit's cell; the mark's event 7 finds it in
            // the position grid, where it's just been put, checks it can be struck and hasn't been by this mark already.)
            if (!mark.Exists || mark.Get("grid_x").AsInt != x || mark.Get("grid_y").AsInt != y || mark.Get("delay").AsBool
                || !CombatSync.IsOursDeep(mark))
                continue;
            _context?.Log($"Determination: the owner's {Gm.ObjectGetName(unit.Get("object_index").AsInt)} came into our mark at ({x}, {y})");
            _striking = true;
            try { Game.CallBuiltinAs("event_user", mark, mark, 7); }
            finally { _striking = false; }
        }
    }
}
