/// @stoneforge return string
/// @stoneforge param inst GmValue
// Loot's key in the loot tables: its id as a number, so an id from with() and one from the culling list match.
// (Legacy: scr_mp_loot_ikey.)
function MpLootIkey(inst)
{
    return string(real(inst));
}
