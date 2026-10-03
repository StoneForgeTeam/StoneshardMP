/// @stoneforge return void
/// @stoneforge param inst GmValue
/// @stoneforge param flag int
// Give loot a flag (MpLootFlag). (Legacy: scr_mp_loot_set_flag.)
function MpLootFlagSet(inst, flag)
{
    if (!variable_global_exists("mp_loot_flags_of"))
        MpLootReset();
    var _k = MpLootIkey(inst);
    var _f = ds_map_find_value(global.mp_loot_flags_of, _k);
    ds_map_set(global.mp_loot_flags_of, _k, (is_undefined(_f) ? 0 : _f) | flag);
}
