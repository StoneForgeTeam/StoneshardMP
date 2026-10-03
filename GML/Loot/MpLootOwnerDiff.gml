/// @stoneforge return string
// Area owner, every few frames: what changed since the snapshot - new loot (drops, kills, a follower's drop we made),
// at once, in the air or not (a follower flies its copy along the same arc: MpLootFly); and loot that left (picked
// up by anyone). JSON {add: [entries (MpLootEntry)], gone: [u]}, "" if nothing changed. (Legacy: the diff in
// scr_mp_loot_host_step, which waited for loot to land.)
function MpLootOwnerDiff()
{
    if (!variable_global_exists("mp_loot_known"))
        MpLootReset();
    var _add = [];
    var _all = MpLootAll();
    for (var _i = 0; _i < array_length(_all); _i++)
    {
        var _inst = _all[_i];
        var _uid = MpLootUid(_inst);
        if (!ds_map_exists(global.mp_loot_known, _uid))
        {
            ds_map_replace(global.mp_loot_known, _uid, _inst);
            array_push(_add, MpLootEntry(_inst));
        }
    }
    // (Collected first: no deleting while going through the map.)
    var _gone = [];
    var _uids = ds_map_keys_to_array(global.mp_loot_known);
    for (var _g = 0; _g < array_length(_uids); _g++)
    {
        if (MpLootGone(ds_map_find_value(global.mp_loot_known, _uids[_g])))
        {
            ds_map_delete(global.mp_loot_known, _uids[_g]);
            array_push(_gone, _uids[_g]);
        }
    }
    if (array_length(_add) == 0 && array_length(_gone) == 0)
        return "";
    return json_stringify({ add: _add, gone: _gone });
}
