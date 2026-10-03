/// @stoneforge return string
/// @stoneforge param name string
// Host: the save player name loads to join - our world (the whole save data) with their character in place of ours,
// as JSON; "" if we have no character for them yet. Their dialogue flags get ours added (story progress: NPCs don't
// repeat what's done), and the other players' characters stay here. (Legacy: scr_mp_join_build_save.)
function MpJoinBuildSave(name)
{
    if (!MpHostInWorld())
        return "";
    var _players = ds_map_find_value(global.saveDataMap, "mpPlayersDataMap");
    if (is_undefined(_players) || !ds_exists(_players, ds_type_map) || !ds_map_exists(_players, name))
        return "";
    var _world = json_parse(json_encode(global.saveDataMap));
    var _char = json_parse(ds_map_find_value(_players, name));
    var _hostDialogue = undefined;
    var _hostChar = variable_struct_get(_world, "characterDataMap");
    if (is_struct(_hostChar))
        _hostDialogue = variable_struct_get(_hostChar, "Dialogue_Complete");
    var _keys = ["characterDataMap", "characterStatsDataMap", "skillsDataMap", "inventoryDataList", "scrollsDataList", "locationsFogDataMap"];
    for (var _i = 0; _i < array_length(_keys); _i++)
    {
        if (variable_struct_exists(_char, _keys[_i]))
            variable_struct_set(_world, _keys[_i], variable_struct_get(_char, _keys[_i]));
    }
    var _charMap = variable_struct_get(_world, "characterDataMap");
    if (is_struct(_hostDialogue) && is_struct(_charMap))
    {
        var _charDialogue = variable_struct_get(_charMap, "Dialogue_Complete");
        if (!is_struct(_charDialogue))
        {
            _charDialogue = {};
            variable_struct_set(_charMap, "Dialogue_Complete", _charDialogue);
        }
        var _names = variable_struct_get_names(_hostDialogue);
        for (var _d = 0; _d < array_length(_names); _d++)
        {
            if (!variable_struct_exists(_charDialogue, _names[_d]))
                variable_struct_set(_charDialogue, _names[_d], variable_struct_get(_hostDialogue, _names[_d]));
        }
    }
    if (variable_struct_exists(_world, "mpPlayersDataMap"))
        variable_struct_remove(_world, "mpPlayersDataMap");
    return json_stringify(_world);
}
