/// @stoneforge return string
// Area owner: all our ground loot, for followers starting over (one arrived, or asked) - a JSON array of entries
// (MpLootEntry). Everything in it is now known (MpLootOwnerDiff).
// (Legacy: the snapshot in scr_mp_loot_host_step.)
function MpLootOwnerSnapshot()
{
    if (!variable_global_exists("mp_loot_known"))
        MpLootReset();
    ds_map_clear(global.mp_loot_known);
    var _all = MpLootAll();
    var _out = [];
    for (var _i = 0; _i < array_length(_all); _i++)
    {
        var _entry = MpLootEntry(_all[_i]);
        ds_map_replace(global.mp_loot_known, _entry.u, _all[_i]);
        array_push(_out, _entry);
    }
    return json_stringify(_out);
}
