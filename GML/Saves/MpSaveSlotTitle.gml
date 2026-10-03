/// @stoneforge return void
/// @stoneforge param header GmValue
// A character folder's header in the save menu (o_saveMenuSlotHeader, after its user event 15 set the title): a
// multiplayer world shows who plays in it (MpSavePlayers) instead of the character's name, numbered and styled as
// the game does its own ("FailMelon, Friend (1)"). Any other folder keeps the game's title. (Legacy: scr_mp_slot_title.)
function MpSaveSlotTitle(header)
{
    if (!instance_exists(header))
        return;
    with (header)
    {
        var _slotMap = scr_slotMapLoad(slotDirName);
        var _players = ds_map_find_value_ext(_slotMap, "mpPlayers", "");
        ds_map_destroy(_slotMap);
        if (!is_string(_players) || _players == "")
            return;
        var _space = scr_actionsLogGetSpace();
        var _number = string_digits(string_replace_all(slotDirName, "_", _space));
        title = scr_stringTransform(_players + _space + scr_actionsLogGetSymbol("openRoundBracket") + _number
            + scr_actionsLogGetSymbol("closeRoundBracket"), true);
    }
}
