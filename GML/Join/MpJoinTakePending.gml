/// @stoneforge return bool
// Client, in place of scr_slotLoad: the host's save we were sent becomes the save data being loaded. false: none
// pending (a save of our own loads as usual).
function MpJoinTakePending()
{
    if (!variable_global_exists("mp_join_pending_map") || global.mp_join_pending_map == -1 || !ds_exists(global.mp_join_pending_map, ds_type_map))
        return false;
    global.saveDataMap = global.mp_join_pending_map;
    global.mp_join_pending_map = -1;
    return true;
}
