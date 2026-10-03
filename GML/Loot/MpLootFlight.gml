/// @stoneforge return GmValue
/// @stoneforge param inst GmValue
// Loot's throw, if it's still in the air (scr_loot_drop's hop onto a neighbouring tile): where it is, the tile it's
// landing on, its speed and gravity, and the spin and slide it's at - all a copy needs to fly the same arc and land
// in the same place (MpLootFly). -1 once it has landed.
function MpLootFlight(inst)
{
    if (inst.speed == 0 && inst.gravity == 0)
        return -1;
    return {
        x: inst.x, y: inst.y, tx: inst.targ_x, ty: inst.targ_y,
        hs: inst.hspeed, vs: inst.vspeed, g: inst.gravity,
        yy: variable_instance_exists(inst, "yy") ? inst.yy : inst.y,
        a: inst.image_angle
    };
}
