/// @stoneforge return string
// Client: our character - the player's sections of the save data being saved (MpJoinPlayerSections) - as JSON, for
// the host to keep. "" without save data. (Legacy: scr_mp_on_saved.)
function MpJoinCharacterSections()
{
    if (!variable_global_exists("saveDataMap") || !ds_exists(global.saveDataMap, ds_type_map))
        return "";
    var _full = json_parse(json_encode(global.saveDataMap));
    var _char = {};
    var _keys = ["characterDataMap", "characterStatsDataMap", "skillsDataMap", "inventoryDataList", "scrollsDataList", "locationsFogDataMap"];
    for (var _i = 0; _i < array_length(_keys); _i++)
    {
        if (variable_struct_exists(_full, _keys[_i]))
            variable_struct_set(_char, _keys[_i], variable_struct_get(_full, _keys[_i]));
    }
    return json_stringify(_char);
}
