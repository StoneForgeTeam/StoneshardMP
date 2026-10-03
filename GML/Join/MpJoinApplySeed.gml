/// @stoneforge return void
/// @stoneforge param seed double
// Client making a character for the host's world: the world map is made from the host's seed (o_globalmap_controller
// seeds its generation with global.seed), so it's the host's map. (Legacy: scr_mp_new_world_seed.)
function MpJoinApplySeed(seed)
{
    global.seed = seed;
    if (variable_global_exists("gameDataMap") && ds_exists(global.gameDataMap, ds_type_map))
        ds_map_set(global.gameDataMap, "seed", seed);
}
