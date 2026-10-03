/// @stoneforge return bool
/// @stoneforge param name string
/// @stoneforge param character string
// Host: keep player name's character (JSON of their save sections) in our own save data - saved with our world, as
// mpPlayersDataMap. false: we're not in a world to keep it in. (Legacy: scr_mp_join_store_char.)
function MpJoinStoreCharacter(name, character)
{
    if (!MpHostInWorld())
        return false;
    var _players = ds_map_find_value(global.saveDataMap, "mpPlayersDataMap");
    if (is_undefined(_players) || !ds_exists(_players, ds_type_map))
    {
        _players = ds_map_create();
        ds_map_add_map(global.saveDataMap, "mpPlayersDataMap", _players);
    }
    ds_map_set(_players, name, character);
    return true;
}
