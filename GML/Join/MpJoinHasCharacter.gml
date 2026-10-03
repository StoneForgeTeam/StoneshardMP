/// @stoneforge return bool
/// @stoneforge param name string
// Host: whether our world has a character for player name.
function MpJoinHasCharacter(name)
{
    if (!MpHostInWorld())
        return false;
    var _players = ds_map_find_value(global.saveDataMap, "mpPlayersDataMap");
    return !is_undefined(_players) && ds_exists(_players, ds_type_map) && ds_map_exists(_players, name);
}
