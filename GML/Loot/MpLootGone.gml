/// @stoneforge return bool
/// @stoneforge param inst GmValue
// Whether loot really left the world (picked up, destroyed). Culled loot is deactivated, which makes instance_exists
// false too, so the culling controller's list is checked first. (Legacy: scr_mp_loot_gone.)
function MpLootGone(inst)
{
    if (instance_exists(inst))
        return false;
    var _gone = true;
    with (o_cullingController)
    {
        if (ds_list_find_index(deactivatedInstancesList, inst) >= 0)
            _gone = false;
    }
    return _gone;
}
