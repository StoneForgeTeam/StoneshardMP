/// @stoneforge return bool
// Host: whether we're in a world that can take players - a game loaded or begun, with its save data.
function MpHostInWorld()
{
    return instance_exists(o_player) && variable_global_exists("saveDataMap") && ds_exists(global.saveDataMap, ds_type_map);
}
