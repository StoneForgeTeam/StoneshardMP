/// @stoneforge return bool
// Client: a calm moment to load the host's world - the main menu, or in game with nothing mid-way (no room change,
// fade, cutscene or conversation). (Legacy: scr_mp_join_step.)
function MpJoinCalm()
{
    if (instance_exists(o_smoothRoomChanger))
        return false;
    if (room == global.mainMenuRoom)
        return true;
    if (!instance_exists(o_player) || instance_exists(o_black_overlay) || instance_exists(o_dialogue))
        return false;
    var _calm = true;
    with (o_player)
    {
        if (scr_is_cutscene())
            _calm = false;
    }
    return _calm;
}
