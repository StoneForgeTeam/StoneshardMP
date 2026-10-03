/// @stoneforge return double
/// @stoneforge param inst GmValue
// Owner: loot's sync id, given on first use - loot goes by this on the wire, not its instance id (one read back from
// the network is a plain number, which needn't match the id used as a map key or in the culling list). (Legacy:
// scr_mp_loot_uid.)
function MpLootUid(inst)
{
    var _k = MpLootIkey(inst);
    var _uid = ds_map_find_value(global.mp_loot_uid_of, _k);
    if (is_undefined(_uid))
    {
        global.mp_loot_next_uid += 1;
        _uid = global.mp_loot_next_uid;
        ds_map_set(global.mp_loot_uid_of, _k, _uid);
    }
    return _uid;
}
