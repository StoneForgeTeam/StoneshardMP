/// @stoneforge return string
// Where and how our player is drawn this frame, for the others' copies of us ("" when there's no player): the place
// (room, "#f<floor>" in a dungeon - every floor of one is the same room - and "@x_y", the world-map cell), then what o_player draws with - stX /
// stY / stScale* (scr_spriteTransformUpdate: the bob and lean), frame, animation row, depth, visible, tilt, hit
// flash (diss), alpha - its shadow as scr_draw_self_shadow places it, its cell, and its health/energy. "|" between.
function MpPlayerState()
{
    if (!instance_exists(o_player))
        return "";
    var _place = room_get_name(room);
    if (variable_global_exists("floor_counter") && global.floor_counter > 0)
        _place += "#f" + string(global.floor_counter);
    // (And the world-map cell: neighbouring areas of the world map are built in the same room.)
    if (variable_global_exists("playerGridX"))
        _place += "@" + string(global.playerGridX) + "_" + string(global.playerGridY);
    var _s = "";
    with (o_player)
    {
        var _row = 0;
        for (var _i = 0; _i < 5; _i++)
        {
            if (global.playerSpriteArray[_i] == sprite_index)
            {
                _row = _i;
                break;
            }
        }
        var _shAlpha = 0;
        if (isGround == 1 && (!hasFootwave) && (!is_sleeping) && (is_life || image_speed == 0))
            _shAlpha = shadow_spr_alpha * shadow_spr_alpha_multiplier;
        _s = _place + "|" + string(stX) + "|" + string(stY) + "|" + string(stScaleX) + "|" + string(stScaleY)
            + "|" + string(image_index) + "|" + string(_row) + "|" + string(depth) + "|" + string(visible ? 1 : 0)
            + "|" + string(image_angle) + "|" + string(diss) + "|" + string(image_alpha)
            + "|" + string(shadow_spr) + "|" + string(draw_x + shadow_spr_shift[0] * image_xscale * shadow_scale)
            + "|" + string(draw_y + (5.5 + shadow_spr_shift[1]) * shadow_scale)
            + "|" + string(image_xscale * shadow_scale) + "|" + string(image_yscale * shadow_scale) + "|" + string(_shAlpha)
            + "|" + string(xx div 26) + "|" + string(yy div 26)
            + "|" + string(HP) + "|" + string(max_hp) + "|" + string(MP) + "|" + string(max_mp);
    }
    return _s;
}
