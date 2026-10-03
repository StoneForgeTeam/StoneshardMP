/// @stoneforge return void
/// @stoneforge param state string
// Another game's world-map tile (MpTileExport): take its seeds (-1: not set there, keep ours) and its dungeon's values,
// so the areas and dungeon there are built and behave as theirs - a floor they've built is rebuilt from their saved
// graph. Not the tile we're standing on: it's built here already, and we'd take it on our next visit. (Legacy:
// scr_mp_tile_apply.)
function MpTileApply(state)
{
    if (!instance_exists(o_player) || global.playerGridX == -4)
        return;
    var _s = json_parse(state);
    if (!is_struct(_s) || !is_array(_s.seeds))
        return;
    if (_s.x == global.playerGridX && _s.y == global.playerGridY)
        return;
    for (var _i = 0; _i < array_length(_s.keys); _i++)
    {
        if (_s.seeds[_i] != -1)
            scr_globaltile_set(_s.keys[_i], _s.seeds[_i], _s.x, _s.y);
    }
    if (!is_struct(_s.dungeon))
        return;
    var _names = variable_struct_get_names(_s.dungeon);
    for (var _d = 0; _d < array_length(_names); _d++)
    {
        var _key = _names[_d];
        var _v = variable_struct_get(_s.dungeon, _key);
        if (is_struct(_v) && variable_struct_exists(_v, "m"))
        {
            var _map = json_decode(_v.m);
            if (_map != -1)
                scr_globaltile_dungeon_set_map(_key, _map, _s.x, _s.y);
        }
        else if (is_struct(_v) && variable_struct_exists(_v, "l"))
        {
            var _list = ds_list_create();
            for (var _l = 0; _l < array_length(_v.l); _l++)
                ds_list_add(_list, _v.l[_l]);
            scr_globaltile_dungeon_set_list(_key, _list, _s.x, _s.y);
        }
        else
            scr_globaltile_dungeon_set(_key, _v, _s.x, _s.y);
    }
}
