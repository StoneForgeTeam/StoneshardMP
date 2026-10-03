/// @stoneforge return void
/// @stoneforge param slotMap GmValue
/// @stoneforge param hostName string
/// @stoneforge param hosting bool
// Host: the character folder's info being saved (scr_slotMapSave's map, its character.map) gets who plays in this
// world - us (hostName), then every player whose character we keep (mpPlayersDataMap) - as "A, B, C" under
// "mpPlayers", for the save menu's header (MpSaveSlotTitle). A world becomes a multiplayer one (gameDataMap's
// "mpWorld", saved with it) the first time it's saved while hosting, and stays one. (Legacy: scr_mp_slot_players_set.)
function MpSavePlayers(slotMap, hostName, hosting)
{
    if (!MpHostInWorld() || !ds_exists(slotMap, ds_type_map))
        return;
    if (hosting)
        ds_map_set(global.gameDataMap, "mpWorld", true);
    if (!ds_map_find_value_ext(global.gameDataMap, "mpWorld", false))
        return;
    var _names = hostName;
    var _players = ds_map_find_value(global.saveDataMap, "mpPlayersDataMap");
    if (!is_undefined(_players) && ds_exists(_players, ds_type_map))
    {
        var _keys = ds_map_keys_to_array(_players);
        array_sort(_keys, true);
        for (var _i = 0; _i < array_length(_keys); _i++)
        {
            var _name = string(_keys[_i]);
            if (_name != "" && _name != hostName)
                _names += (_names == "" ? "" : ", ") + _name;
        }
    }
    if (_names != "")
        ds_map_set(slotMap, "mpPlayers", _names);
}
