/// @stoneforge return void
// The floor just built was rejected (o_dungeon_controller's user event 5): the game drops its seed and restarts the
// room for a new one. Vanilla's new one is random; ours comes from the world seed, so count the attempt - the next
// seed mixes it in - or the same floor is built, rejected and restarted forever. Every game rejects the same layout,
// so they count alike and still build the same floor. (Legacy: scr_mp_dungeon_retry_note.)
function MpDungeonRetryNote()
{
    if (!variable_global_exists("mp_dungeon_retries"))
        global.mp_dungeon_retries = ds_map_create();
    var _key = MpDungeonRetryKey(scr_dungeonGetCurrentFloorNumber());
    ds_map_set(global.mp_dungeon_retries, _key, ds_map_find_value_ext(global.mp_dungeon_retries, _key, 0) + 1);
}
