/// @stoneforge return int
/// @stoneforge param uids string
// Area owner: a follower picked these up (a JSON array of our sync ids) - they're gone from our world too; our next
// diff tells every follower. How many were still here. (Legacy: LOOT_TAKEN in scr_mp_loot_handle.)
function MpLootOwnerTaken(uids)
{
    if (!variable_global_exists("mp_loot_known"))
        return 0;
    var _list = json_parse(uids);
    var _n = 0;
    for (var _i = 0; _i < array_length(_list); _i++)
    {
        var _inst = ds_map_find_value(global.mp_loot_known, _list[_i]);
        if (!is_undefined(_inst) && !MpLootGone(_inst))
        {
            MpLootDestroy(_inst);
            _n++;
        }
    }
    return _n;
}
