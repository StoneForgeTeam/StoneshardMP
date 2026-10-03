/// @stoneforge return string
/// @stoneforge param tileX double
/// @stoneforge param tileY double
// A world-map tile as JSON for the others (MpTileApply): the seeds its areas are built from (-1: not set here), and
// its dungeon, if it has one - every shared value (MpDungeonKeyShared): numbers and text as they are, its maps (the
// saved floor graphs, which rooms dropped what, the boss's name) as {m: their JSON}, its level list as {l: [...]}.
// "" outside a world. (Legacy: scr_mp_tile_send, and the dungeon keys of scr_mp_world_copy_to.)
function MpTileExport(tileX, tileY)
{
    if (!variable_global_exists("globalmapLocationsDataMap") || global.playerGridX == -4)
        return "";
    var _keys = ["seed", "growSeed", "mobsSeed", "presetSeed", "containersSeed", "Trade_Seed"];
    var _seeds = array_create(array_length(_keys), -1);
    for (var _i = 0; _i < array_length(_keys); _i++)
        _seeds[_i] = scr_globaltile_get(_keys[_i], tileX, tileY, -1, global.globaltile_lookup_save);
    var _dungeon = -1;
    var _map = scr_globaltile_get("dungeon", tileX, tileY, -1, global.globaltile_lookup_save);
    if (_map != -1 && ds_exists(_map, ds_type_map))
    {
        _dungeon = {};
        var _names = ds_map_keys_to_array(_map);
        for (var _d = 0; _d < array_length(_names); _d++)
        {
            var _key = _names[_d];
            if (!is_string(_key) || !MpDungeonKeyShared(_key))
                continue;
            var _v = ds_map_find_value(_map, _key);
            if (ds_map_is_map(_map, _key))
                variable_struct_set(_dungeon, _key, { m: json_encode(_v) });
            else if (ds_map_is_list(_map, _key))
            {
                var _items = [];
                for (var _l = 0; _l < ds_list_size(_v); _l++)
                    array_push(_items, ds_list_find_value(_v, _l));
                variable_struct_set(_dungeon, _key, { l: _items });
            }
            else if (is_string(_v) || is_real(_v) || is_int64(_v) || is_int32(_v) || is_bool(_v))
                variable_struct_set(_dungeon, _key, _v);
        }
    }
    return json_stringify({ x: tileX, y: tileY, keys: _keys, seeds: _seeds, dungeon: _dungeon });
}
