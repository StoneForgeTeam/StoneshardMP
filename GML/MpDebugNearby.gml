/// @stoneforge return string
/// @stoneforge param radius int
// Diagnostics (Ctrl+Shift+D): every instance within radius cells of the player - object, cell, sprite and frame,
// visible, depth, and for units their state and animation flags - one per line, sorted by the caller.
function MpDebugNearby(radius)
{
    if (!instance_exists(o_player))
        return "";
    var _px = o_player.x;
    var _py = o_player.y;
    var _out = "";
    with (all)
    {
        if (point_distance(x, y, _px, _py) > radius * 26)
            continue;
        var _spr = (sprite_exists(sprite_index) ? sprite_get_name(sprite_index) : "-");
        var _line = object_get_name(object_index) + " @" + string(x div 26) + "," + string(y div 26)
            + " spr=" + _spr + "#" + string(floor(image_index)) + " vis=" + string(visible) + " depth=" + string(depth);
        if (object_is_ancestor(object_index, o_unit))
        {
            _line += " state=" + string(variable_instance_exists(id, "state") ? state : "-")
                + " is_life=" + string(variable_instance_exists(id, "is_life") ? is_life : "-")
                + " spr_render=" + string(variable_instance_exists(id, "spr") && sprite_exists(spr) ? sprite_get_name(spr) : "-")
                + " ai=" + string(variable_instance_exists(id, "ai_is_on") ? ai_is_on : "-")
                + " mp_seen=" + string(variable_instance_exists(id, "mp_area_seen") ? mp_area_seen : "-");
        }
        _out += _line + "\n";
    }
    return _out;
}
