/// @stoneforge return bool
/// @stoneforge param save string
/// @stoneforge param host string
// Client: the save the host sent (its world with our character) kept to load next (MpJoinTakePending, in place of
// scr_slotLoad), tagged as the host's world. false: it didn't decode.
function MpJoinSetPending(save, host)
{
    if (variable_global_exists("mp_join_pending_map") && global.mp_join_pending_map != -1 && ds_exists(global.mp_join_pending_map, ds_type_map))
        ds_map_destroy(global.mp_join_pending_map);
    global.mp_join_pending_map = json_decode(save);
    if (global.mp_join_pending_map == -1)
        return false;
    ds_map_set(global.mp_join_pending_map, "mpHost", host);
    return true;
}
