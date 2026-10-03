/// @stoneforge return bool
// Client: back to the main menu from the host's world (the host left it) - leave the game (room changer event 14) and
// go to the menu (3), without saving: the host has our character, sent by the save it asked for. false: a room change
// is already under way. (Legacy: scr_mp_leave_world.)
function MpLeaveToMenu()
{
    if (room == global.mainMenuRoom)
        return true;
    return scr_smoothRoomChange(-4, [14, 3]) != -4;
}
