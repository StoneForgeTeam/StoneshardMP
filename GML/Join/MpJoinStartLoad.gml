/// @stoneforge return bool
// Client: load the host's save (MpJoinSetPending) as the game's save menu loads one - a room change with event 2
// (load), and 14 first (leave the current game) when in game. The local slot is "N/A": a client keeps no saves of the
// host's world. (Legacy: scr_mp_join_start_load.)
function MpJoinStartLoad()
{
    audio_play_sound(snd_ui_menu_start_game_st, 4, 0);
    var _events = (room == global.mainMenuRoom) ? [2] : [14, 2];
    var _changer = scr_smoothRoomChange(-4, _events);
    if (_changer == -4)
        return false;
    global.slotLoaded = false;
    ds_map_set(global.slotsMap, "lastCharacter", "N/A");
    ds_map_set(global.slotsMap, "lastSave", "N/A");
    return true;
}
