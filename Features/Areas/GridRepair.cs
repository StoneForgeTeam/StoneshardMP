using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Players;

namespace StoneshardMP.Features.Areas;

// Cells the game believes a unit stands on, with none there: freed. A unit's cell is blocked in the room's path grid
// (o_controller.newgrid: scr_collision_add_enemy - 1000000 there, and 18 in its cleangrid, where a wall or a thing has
// 1000000) and freed as it leaves. Where players are together, units move in ways the game's own walking doesn't - other
// players' stand-ins snapped to where their player is, the owner's units moved by another game's knockback or swap,
// copies made and taken away - and a cell some of those left stayed blocked: nobody could go there, enemies neither, until
// a player walked over it, or the room was loaded again (#37). Every couple of seconds, while other players are here, the
// cells around each player blocked for a unit with no unit on them or beside them (one walking holds two cells; a big one,
// more) are freed as the game frees one.
internal sealed class GridRepair
{
    private const int Every = 120;
    private const int ReachX = 20, ReachY = 14;
    private const double Blocked = 1000000, ForUnit = 18;
    private readonly ModContext _context;
    private readonly PlayerManager _players;
    private int _frame;

    public GridRepair(ModContext context, PlayerManager players)
    {
        _context = context;
        _players = players;
    }

    public void Tick()
    {
        if (++_frame % Every != 0 || !Gm.InGame || _players.ObjectIndex < 0)
            return;
        List<Instance> standIns = Instances.All(_players.ObjectIndex);
        if (standIns.Count == 0 || Units.Current() is not { } grids)
            return;
        GmValue path = Field(grids.Collisions, "grid");
        GmValue clean = Field(Field(grids.Controller, "cleangrid"), "grid");
        if (path.Kind != GmKind.Real || clean.Kind != GmKind.Real)
            return;
        int width = Game.CallBuiltin("ds_grid_width", path).AsInt, height = Game.CallBuiltin("ds_grid_height", path).AsInt;

        // (Where a unit is or is going - off screen too - and the cells beside: a big one's, wider.)
        var held = new HashSet<(int, int)>();
        foreach (Instance unit in Instances.All(GameObjectId.o_unit, includeCulled: true))
        {
            GmValue poly = unit.Get("is_poly_cell");
            int reach = poly.IsUndefined || poly.AsBool ? 3 : 1;
            foreach (var (xName, yName) in new[] { ("x", "y"), ("xx", "yy") })
            {
                int cx = (int)Math.Floor(unit.Get(xName).AsReal / 26), cy = (int)Math.Floor(unit.Get(yName).AsReal / 26);
                for (int dx = -reach; dx <= reach; dx++)
                    for (int dy = -reach; dy <= reach; dy++)
                        held.Add((cx + dx, cy + dy));
            }
        }

        var looked = new HashSet<(int, int)>();
        int freed = 0;
        foreach (Instance player in standIns.Append(OurPlayer.Instance).Where(p => !p.IsNone && p.Exists))
        {
            int px = (int)Math.Floor(player.Get("x").AsReal / 26), py = (int)Math.Floor(player.Get("y").AsReal / 26);
            for (int x = Math.Max(0, px - ReachX); x <= Math.Min(width - 1, px + ReachX); x++)
                for (int y = Math.Max(0, py - ReachY); y <= Math.Min(height - 1, py + ReachY); y++)
                {
                    if (held.Contains((x, y)) || !looked.Add((x, y))
                        || Game.CallBuiltin("ds_grid_get", clean, x, y).AsReal != ForUnit
                        || Game.CallBuiltin("ds_grid_get", path, x, y).AsReal != Blocked)
                        continue;
                    // (As the controller - the grid its first argument: as Units.Move frees one.)
                    Game.CallScript("scr_collision_clear", grids.Controller, grids.Collisions, x, y, true);
                    freed++;
                }
        }
        if (freed > 0)
            _context.Log($"Freed {freed} cell(s) blocked for a unit that wasn't there");
    }

    // A field of the game's grid holders - a struct, or an instance.
    private static GmValue Field(GmValue holder, string name) => holder.Kind switch
    {
        GmKind.Struct => Game.CallBuiltin("variable_struct_get", holder, name),
        _ => Instance.Of(holder) is { IsNone: false } instance && instance.Exists ? instance.Get(name) : GmValue.Undefined,
    };

    private static GmValue Field(Instance holder, string name) => holder.Exists ? holder.Get(name) : GmValue.Undefined;
}
