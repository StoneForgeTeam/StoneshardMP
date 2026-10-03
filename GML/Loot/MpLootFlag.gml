/// @stoneforge return bool
/// @stoneforge param inst GmValue
/// @stoneforge param flag int
// Whether loot has a flag: 1 came from the other game (never a drop of ours), 2 a drop or not is decided, 4 it is our
// drop. (Legacy: scr_mp_loot_flag.)
function MpLootFlag(inst, flag)
{
    if (!variable_global_exists("mp_loot_flags_of"))
        MpLootReset();
    var _f = ds_map_find_value(global.mp_loot_flags_of, MpLootIkey(inst));
    return !is_undefined(_f) && (_f & flag) != 0;
}
