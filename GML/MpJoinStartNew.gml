/// @stoneforge return void
// Client, from the main menu: start a new character as New Game -> Adventure does (the class picked at Verren); its
// world is the host's (MpJoinApplySeed). (Legacy: scr_mp_join_start_new.)
function MpJoinStartNew()
{
    global.permadeathMode = false;
    global.globalMapInit = false;
    audio_play_sound(snd_ui_menu_start_game_st, 4, 0);
    scr_smoothRoomChange(-4, [1]);
    scr_loadingCreate(4019, ds_list_find_value(global.other_hover, 22));
}
