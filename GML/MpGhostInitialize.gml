/// @stoneforge return void
/// @stoneforge param unit GmValue
// o_enemy's Create schedules Alarm 2 for scr_atr_calc. Ghosts deliberately have no character stat template and do
// not participate in combat yet, so stop that inherited setup before it runs.
function MpGhostInitialize(unit)
{
    if (!instance_exists(unit))
        return;
    with (unit)
        alarm[2] = -1;
}
