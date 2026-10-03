/// @stoneforge return string
// Host: a player has just come into our world - list what to copy them (MpWorldCopyItem): every location's stored
// state and every world-map tile we have (areas' seeds, dungeons' layouts). Kept in globals while it goes out a few
// a frame. "locations|tiles" - how many of each. (Legacy: scr_mp_world_copy_to.)
function MpWorldCopyBegin()
{
    global.mp_copy_locations = [];
    global.mp_copy_tiles = [];
    if (!variable_global_exists("locationsRoomsDataMap") || !variable_global_exists("globalmapLocationsDataMap"))
        return "0|0";
    var _locations = ds_map_keys_to_array(global.locationsRoomsDataMap);
    for (var _l = 0; _l < array_length(_locations); _l++)
    {
        var _rooms = ds_map_find_value(global.locationsRoomsDataMap, _locations[_l]);
        if (!ds_exists(_rooms, ds_type_map))
            continue;
        var _roomTags = ds_map_keys_to_array(_rooms);
        for (var _r = 0; _r < array_length(_roomTags); _r++)
        {
            var _presets = ds_map_find_value(_rooms, _roomTags[_r]);
            if (!ds_exists(_presets, ds_type_map))
                continue;
            var _presetTags = ds_map_keys_to_array(_presets);
            for (var _p = 0; _p < array_length(_presetTags); _p++)
                array_push(global.mp_copy_locations, [_locations[_l], _roomTags[_r], _presetTags[_p]]);
        }
    }
    // Tiles are keyed "x_y".
    var _tiles = ds_map_keys_to_array(global.globalmapLocationsDataMap);
    for (var _t = 0; _t < array_length(_tiles); _t++)
    {
        var _tag = string(_tiles[_t]);
        var _sep = string_pos("_", _tag);
        if (_sep > 1)
            array_push(global.mp_copy_tiles, [real(string_copy(_tag, 1, _sep - 1)), real(string_delete(_tag, 1, _sep))]);
    }
    return string(array_length(global.mp_copy_locations)) + "|" + string(array_length(global.mp_copy_tiles));
}
