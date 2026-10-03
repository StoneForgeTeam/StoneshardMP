/// @stoneforge return void
/// @stoneforge param entry GmValue
// Follower: the owner has this loot (an entry: MpLootEntry). Bound to ours if we have it - our own drop coming back
// (its token), or our twin from the same save (same key) - else made from its JSON, and flown along the owner's arc
// if it's in the air. (Legacy: LOOT_ADD in scr_mp_loot_handle.)
function MpLootFollowerAdd(entry)
{
    var _have = ds_map_find_value(global.mp_loot_map, entry.u);
    if (!is_undefined(_have) && !MpLootGone(_have))
        return;
    var _inst = noone;
    if (entry.t != "")
    {
        var _mine = ds_map_find_value(global.mp_loot_pending, entry.t);
        if (!is_undefined(_mine))
        {
            ds_map_delete(global.mp_loot_pending, entry.t);
            if (!MpLootGone(_mine))
                _inst = _mine;
        }
    }
    if (_inst == noone)
    {
        var _all = MpLootAll();
        for (var _i = 0; _i < array_length(_all); _i++)
        {
            if (!ds_map_exists(global.mp_loot_hid_of, MpLootIkey(_all[_i])) && !MpLootFlag(_all[_i], 4)
                && MpLootKey(_all[_i]) == entry.k)
            {
                _inst = _all[_i];
                break;
            }
        }
    }
    if (_inst == noone)
    {
        _inst = MpLootCreate(entry.j);
        MpLootFly(_inst, entry.f);
    }
    if (_inst == noone)
        return;
    MpLootFlagSet(_inst, 1);
    ds_map_set(global.mp_loot_hid_of, MpLootIkey(_inst), entry.u);
    ds_map_replace(global.mp_loot_map, entry.u, _inst);
}
