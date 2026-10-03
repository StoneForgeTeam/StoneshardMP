/// @stoneforge return void
// Start the loot tables over (a new area, or a new role in it). Kept in globals, keyed by instance (MpLootIkey):
// a culled instance doesn't report its own variables, so tags stored on it were lost.
// - mp_loot_uid_of: owner - our loot -> its sync id; mp_loot_known: sync id -> our loot, as last sent;
//   mp_loot_token_of: our loot -> the drop token of the follower it came from.
// - mp_loot_hid_of: follower - our loot -> the owner's sync id; mp_loot_map: the owner's id -> our loot;
//   mp_loot_pending: our drops sent to the owner, token -> our loot; mp_loot_sent_at: our loot -> when.
// - mp_loot_flags_of: our loot -> flags (MpLootFlag).
function MpLootReset()
{
    var _names = ["mp_loot_uid_of", "mp_loot_known", "mp_loot_token_of", "mp_loot_hid_of", "mp_loot_map",
        "mp_loot_pending", "mp_loot_sent_at", "mp_loot_flags_of"];
    for (var _i = 0; _i < array_length(_names); _i++)
    {
        if (variable_global_exists(_names[_i]) && ds_exists(variable_global_get(_names[_i]), ds_type_map))
            ds_map_clear(variable_global_get(_names[_i]));
        else
            variable_global_set(_names[_i], ds_map_create());
    }
    if (!variable_global_exists("mp_loot_next_uid"))
        global.mp_loot_next_uid = 0;
    if (!variable_global_exists("mp_loot_next_token"))
        global.mp_loot_next_token = 0;
}
