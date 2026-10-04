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

    /// <summary>Moves a unit to a cell in the game's grids. Its own x / y follow only for a jump of more than two cells:
    /// for a short move it's drawn there smoothly - another player's from their state, an area unit by its own step.</summary>
    public static void Move(Instance unit, int cellX, int cellY)
    {
        Instance controller = Controller();
        if (!unit.Exists || controller.IsNone)
            return;
        // (By its id: what the position grid holds, and what the game's scripts are handed.)
        unit = unit.Persist();
        double x = cellX * Cell + 13, y = cellY * Cell + 13;
        if (unit.Get("xx").AsReal == x && unit.Get("yy").AsReal == y)
            return;
        var (oldX, oldY) = CellOf(unit);
        GmValue collisions = controller.Get("newgrid"), positions = controller.Get("posgrid");
        Game.CallScript("scr_collision_clear", controller, collisions, oldX, oldY, true);
        Game.CallScript("scr_enemy_poly_cell_clear", unit, oldX, oldY);
        if (unit.Get("is_poly_cell").AsBool)
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
        if (Math.Abs(cellX - oldX) > 2 || Math.Abs(cellY - oldY) > 2)
        {
            unit["x"] = x;
            unit["y"] = y;
            unit["draw_x"] = x;
            unit["draw_y"] = y;
            unit["diff_x"] = 0;
            unit["diff_y"] = 0;
        }
        Game.CallScript("scr_enemy_poly_cell_fill", unit, cellX, cellY);
        Game.CallScript("scr_collision_add_enemy", controller, collisions, cellX, cellY);
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
