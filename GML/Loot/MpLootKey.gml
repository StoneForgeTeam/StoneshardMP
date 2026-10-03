/// @stoneforge return string
/// @stoneforge param inst GmValue
// Loot's matching key: what the game hashes it by (scr_locationRoomEntityLootInstanceGetHash) - a weapon's idName or
// the object's name, and its floored position - so twins from the same save match. Dot access, not with(), so it
// works on culled instances too. (Legacy: scr_mp_loot_key.)
function MpLootKey(inst)
{
    var _name = object_get_name(inst.object_index);
    if (inst.object_index == o_weapon_loot)
        _name = ds_map_find_value_ext(inst.data, "idName", "N/A");
    return _name + "_" + string(floor(inst.x)) + "_" + string(floor(inst.y));
}
