using System;
using System.Linq;
using StoneForge;

namespace StoneshardMP.Features.Areas;

// A unit moved through, or taken out of, the game's grids the way a normal unit's own movement does it: the collision
// grid (o_controller's newgrid), the position grid (its posgrid: who stands where - targeting, the mouse, pathing) and
// a big unit's extra cells (scr_enemy_poly_cell_*). For units this game doesn't move itself: another player's unit,
// and an area's units a follower takes from the owner (AreaUnits). (Legacy: scr_mp_player_unit_move.)
internal static class UnitGrid
{
    /// <summary>A cell's size, in pixels.</summary>
    public const int Cell = 26;

    /// <summary>The cell a unit stands on, from its xx / yy.</summary>
    public static (int X, int Y) CellOf(Instance unit) => (CellOf(unit.Get("xx").AsReal), CellOf(unit.Get("yy").AsReal));

    // (GameMaker's div, positions being positive.)
    private static int CellOf(double position) => (int)Math.Truncate(position / Cell);

    /// <summary>The game's controller and its grids - the collision grid (newgrid) and the position grid (posgrid) - for
    /// moving units (null with no controller: not in a game). The same for the room: found once for many units.</summary>
    public readonly record struct Grids(Instance Controller, GmValue Collisions, GmValue Positions);

    public static Grids? Current()
        => Controller() is { IsNone: false } controller ? new Grids(controller, controller.Get("newgrid"), controller.Get("posgrid")) : null;

    /// <summary>Moves a unit to a cell in the game's grids. Its own x / y follow only for a jump of more than two cells -
    /// for a short move an area unit walks there by its own step - or always with <paramref name="snap"/>: a unit drawn
    /// from elsewhere (another player's, from their state) never walks. (A unit that walks clears the cell it leaves in the
    /// position grid as it starts, whoever's there by then: our player, come onto it - the game then crashes on it.)</summary>
    public static void Move(Instance unit, int cellX, int cellY, bool snap = false)
    {
        if (Current() is { } grids)
            Move(unit, cellX, cellY, grids, snap: snap);
    }

    /// <summary>Moves a unit, with the room's grids already found, and whether it's a big unit (more than one cell:
    /// is_poly_cell) if that's known - it never changes; null: read it.</summary>
    public static void Move(Instance unit, int cellX, int cellY, Grids grids, bool? poly = null, bool snap = false)
    {
        if (!unit.Exists)
            return;
        // (By its id: what the position grid holds, and what the game's scripts are handed.)
        unit = unit.Persist();
        double x = cellX * Cell + 13, y = cellY * Cell + 13;
        double oldXx = unit.Get("xx").AsReal, oldYy = unit.Get("yy").AsReal;
        if (oldXx == x && oldYy == y)
            return;
        int oldX = CellOf(oldXx), oldY = CellOf(oldYy);
        var (controller, collisions, positions) = grids;
        bool isPoly = poly ?? unit.Get("is_poly_cell").AsBool;
        Game.CallScript("scr_collision_clear", controller, collisions, oldX, oldY, true);
        // (A big unit's extra cells: the game's scripts do nothing for one of a single cell.)
        if (isPoly)
            Game.CallScript("scr_enemy_poly_cell_clear", unit, oldX, oldY);
        if (isPoly)
        {
            Game.CallScript("scr_enemy_poly_cell_posgrid_clear", unit, oldX, oldY);
            Game.CallScript("scr_enemy_poly_cell_posgrid_fill", unit, cellX, cellY);
        }
        else
        {
            if (At(controller, positions, oldX, oldY).Equals(unit))
                Game.CallScript("ds_grid_set_ext", controller, positions, oldX, oldY, -4);
            Game.CallScript("ds_grid_set_ext", controller, positions, cellX, cellY, unit);
        }
        unit["xx"] = x;
        unit["yy"] = y;
        if (snap || Math.Abs(cellX - oldX) > 2 || Math.Abs(cellY - oldY) > 2)
        {
            unit["x"] = x;
            unit["y"] = y;
            unit["draw_x"] = x;
            unit["draw_y"] = y;
            unit["diff_x"] = 0;
            unit["diff_y"] = 0;
        }
        if (isPoly)
            Game.CallScript("scr_enemy_poly_cell_fill", unit, cellX, cellY);
        Game.CallScript("scr_collision_add_enemy", controller, collisions, cellX, cellY);
    }

    /// <summary>Whether a unit may take a cell in the position grid: it's free, or it's the unit's own - another unit there
    /// (our player, an area unit, for a moment) would be overwritten in it, and the game then reads the wrong unit there.
    /// (Legacy: mp_ghost_step.)</summary>
    public static bool CanTake(Instance unit, int cellX, int cellY)
    {
        if (Controller() is not { IsNone: false } controller)
            return false;
        Instance occupant = At(controller, controller.Get("posgrid"), cellX, cellY);
        return occupant.IsNone || !occupant.Exists || occupant.Equals(unit.Persist())
            || !Gm.ObjectIsAncestor(occupant.Get("object_index").AsInt, (int)GameObjectId.o_unit);
    }

    /// <summary>Takes a unit out of the world quietly: out of the grids and the player's list of units to run each turn,
    /// then destroyed without its Destroy event (no loot, corpse or kill credit).</summary>
    public static void Remove(Instance unit)
    {
        if (!unit.Exists)
            return;
        unit = unit.Persist();
        var (x, y) = CellOf(unit);
        if (Controller() is { IsNone: false } controller)
        {
            GmValue positions = controller.Get("posgrid");
            Game.CallScript("scr_collision_clear", controller, controller.Get("newgrid"), x, y, true);
            if (At(controller, positions, x, y).Equals(unit))
                Game.CallScript("ds_grid_set_ext", controller, positions, x, y, -4);
        }
        Game.CallScript("scr_enemy_poly_cell_clear", unit, x, y);
        Game.CallScript("scr_enemy_poly_cell_posgrid_clear", unit, x, y);
        // (Even mid-turn: a destroyed unit can't stay in the list the turn walks.)
        RemoveFromTurns(listed => listed.Equals(unit), betweenTurnsOnly: false);
        unit.Destroy(runDestroyEvent: false);
    }

    /// <summary>How many units the player's list of units to run each turn holds (-1 with no player).</summary>
    public static int TurnsCount()
        => Instances.All(GameObjectId.o_player).FirstOrDefault() is { IsNone: false } player && player.Get("enemylist").AsDsList is { } units
            ? units.Count : -1;

    /// <summary>Takes units out of the player's list of units to run each turn (o_player's enemylist) - with
    /// <paramref name="betweenTurnsOnly"/>, only between turns (its enemy_iteration 0), never while the turn walks it.</summary>
    public static void RemoveFromTurns(Func<Instance, bool> remove, bool betweenTurnsOnly = true)
    {
        Instance player = Instances.All(GameObjectId.o_player).FirstOrDefault();
        if (player.IsNone || (betweenTurnsOnly && player.Get("enemy_iteration").AsReal != 0) || player.Get("enemylist").AsDsList is not { } units)
            return;
        for (int i = units.Count - 1; i >= 0; i--)
            if (InstanceOf(units[i]) is { IsNone: false } listed && remove(listed))
                units.RemoveAt(i);
    }

    /// <summary>The instance a value the game keeps for one names - a reference, or its number - by its id (none if it
    /// names none).</summary>
    public static Instance InstanceOf(GmValue value) => value.Kind switch
    {
        GmKind.Instance => value.AsInstance.Persist(),
        GmKind.Real when value.AsInt >= 0 => Instance.FromId(value.AsInt),
        _ => default,
    };

    private static Instance At(Instance controller, GmValue grid, int x, int y)
        => InstanceOf(Game.CallScript("ds_grid_get_ext", controller, grid, x, y, -4));

    private static Instance Controller() => Instances.All(GameObjectId.o_controller).FirstOrDefault();
}
