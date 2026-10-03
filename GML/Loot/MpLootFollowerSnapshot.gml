/// @stoneforge return string
/// @stoneforge param snapshot string
// Follower: the owner's ground loot (MpLootOwnerSnapshot) - ours made to match it: each item bound to our twin or
// made, and anything it didn't list removed (it isn't in the owner's world). What's here now is the owner's to list,
// so none of it can be a drop of ours. "listed N, removed M", for the log. (Legacy: SYNC_BEGIN/ADD/END.)
function MpLootFollowerSnapshot(snapshot)
{
    MpLootReset();
    var _list = json_parse(snapshot);
    if (!is_array(_list))
        return "unreadable";
    var _here = MpLootAll();
    for (var _i = 0; _i < array_length(_here); _i++)
        MpLootFlagSet(_here[_i], 1);
    for (var _a = 0; _a < array_length(_list); _a++)
        MpLootFollowerAdd(_list[_a]);
    var _removed = 0;
    var _left = MpLootAll();
    for (var _r = 0; _r < array_length(_left); _r++)
    {
        if (!ds_map_exists(global.mp_loot_hid_of, MpLootIkey(_left[_r])))
        {
            MpLootDestroy(_left[_r]);
            _removed++;
        }
    }
    return "listed " + string(array_length(_list)) + ", removed " + string(_removed);
}
