/// @stoneforge return string
/// @stoneforge param tileX double
/// @stoneforge param tileY double
// A world-map tile as JSON for the others (MpTileApply): the seeds its areas are built from (-1: not set here), and
// its dungeon's layout - each floor's seed, and which floors hold the quest, the modification and the secret room -
// if it has one. "" outside a world. (Legacy: scr_mp_tile_send, and the dungeon keys of scr_mp_world_copy_to.)
function MpTileExport(tileX, tileY)
{
    if (!variable_global_exists("globalmapLocationsDataMap") || global.playerGridX == -4)
        return "";
    var _keys = ["seed", "growSeed", "mobsSeed", "presetSeed", "containersSeed", "Trade_Seed"];
    var _seeds = array_create(array_length(_keys), -1);
    for (var _i = 0; _i < array_length(_keys); _i++)
        _seeds[_i] = scr_globaltile_get(_keys[_i], tileX, tileY, -1, global.globaltile_lookup_save);
    var _dungeonKeys = ["DungeonSeed", "dungeon_questFloor", "dungeon_modificationFloor", "dungeon_secretFloor"];
    var _dungeon = {};
    var _any = false;
    for (var _d = 0; _d < array_length(_dungeonKeys); _d++)
    {
        // (-4: not set.)
        var _v = scr_globaltile_dungeon_get(_dungeonKeys[_d], tileX, tileY, -4);
        if (is_string(_v) || ((is_real(_v) || is_int64(_v) || is_int32(_v)) && _v != -4))
        {
            variable_struct_set(_dungeon, _dungeonKeys[_d], _v);
            _any = true;
        }
    }
    return json_stringify({ x: tileX, y: tileY, keys: _keys, seeds: _seeds, dungeon: _any ? _dungeon : -1 });
}
