/// @stoneforge return void
/// @stoneforge param state string
// Another game's world-map tile (MpTileExport): take its seeds (-1: not set there, keep ours) and its dungeon's
// layout, so the areas and dungeon there are built as theirs. Not the tile we're standing on: it's built here
// already (we'd take it on our next visit). (Legacy: scr_mp_tile_apply.)
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
    if (is_struct(_s.dungeon))
    {
        var _names = variable_struct_get_names(_s.dungeon);
        for (var _d = 0; _d < array_length(_names); _d++)
            scr_globaltile_dungeon_set(_names[_d], variable_struct_get(_s.dungeon, _names[_d]), _s.x, _s.y);
    }
}
