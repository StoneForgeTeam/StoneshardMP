/// @stoneforge return void
/// @stoneforge param unit GmValue
/// @stoneforge param cellX int
/// @stoneforge param cellY int
// Move a remote ghost's underlying dummy unit through the same collision/occupancy grids a normal unit uses. Its
// visual sprite is drawn separately in C#, so this only controls targeting and where the game considers it to be.
function MpGhostUnitMove(unit, cellX, cellY)
{
    if (!instance_exists(unit) || !instance_exists(o_controller))
        return;
    with (unit)
    {
        var _x = cellX * 26 + 13;
        var _y = cellY * 26 + 13;
        if (xx == _x && yy == _y)
            return;
        var _oldX = xx div 26;
        var _oldY = yy div 26;
        with (o_controller)
            scr_collision_clear(newgrid, _oldX, _oldY, true);
        scr_enemy_poly_cell_clear(_oldX, _oldY);
        if (is_poly_cell)
        {
            scr_enemy_poly_cell_posgrid_clear(_oldX, _oldY);
            scr_enemy_poly_cell_posgrid_fill(cellX, cellY);
        }
        else
        {
            with (o_controller)
            {
                if (ds_grid_get_ext(posgrid, _oldX, _oldY) == other.id)
                    ds_grid_set_ext(posgrid, _oldX, _oldY, -4);
                ds_grid_set_ext(posgrid, cellX, cellY, other.id);
            }
        }
        xx = _x;
        yy = _y;
        if (abs(cellX - _oldX) > 2 || abs(cellY - _oldY) > 2)
        {
            x = xx;
            y = yy;
            draw_x = x;
            draw_y = y;
            diff_x = 0;
            diff_y = 0;
        }
        scr_enemy_poly_cell_fill(cellX, cellY);
        with (o_controller)
            scr_collision_add_enemy(newgrid, cellX, cellY);
    }
}
