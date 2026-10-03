/// @stoneforge return void
/// @stoneforge param inst GmValue
/// @stoneforge param flight GmValue
// Put loot made from its saved state (landed where it was) back in the air as another game's throw has it
// (MpLootFlight): the game's own Step then flies it along the same arc and lands it on the same tile, with its sound
// and dust. Nothing for -1 (it had landed).
function MpLootFly(inst, flight)
{
    if (!is_struct(flight) || !instance_exists(inst))
        return;
    with (inst)
    {
        x = flight.x;
        y = flight.y;
        targ_x = flight.tx;
        targ_y = flight.ty;
        hspeed = flight.hs;
        vspeed = flight.vs;
        gravity = flight.g;
        if (variable_instance_exists(id, "yy"))
            yy = flight.yy;
        image_angle = flight.a;
    }
}
