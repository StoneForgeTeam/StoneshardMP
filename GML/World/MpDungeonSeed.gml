/// @stoneforge return double
/// @stoneforge param kind int
/// @stoneforge param dungeonFloor int
// A seed for the dungeon at the player's world-map cell - kind 1: the layout of floor dungeonFloor, 2: which floors
// are special - from the world seed instead of irandom, so every game in this world builds the same dungeon. Mixed
// with the in-game day (a dungeon that resets gets a new layout, the same everywhere that day), and for a floor the
// game rejected and is building again (MpDungeonRetryNote), the attempt. -1: a prologue dungeon (as vanilla).
// (Legacy: scr_mp_dungeon_seed.)
function MpDungeonSeed(kind, dungeonFloor)
{
    if (global.playerGridX == -4)
        return -1;
    var _day = floor(scr_timeGetTimestamp() / 1440) + 1;
    var _retry = 0;
    if (kind == 1 && variable_global_exists("mp_dungeon_retries"))
        _retry = ds_map_find_value_ext(global.mp_dungeon_retries, MpDungeonRetryKey(dungeonFloor), 0);
    var _mix = global.seed + global.playerGridX * 73856093 + global.playerGridY * 19349663 + (10 + kind) * 83492791
        + dungeonFloor * 50331653 + _day * 2654435761 + _retry * 40503;
    return abs(_mix) mod 2147483647;
}
