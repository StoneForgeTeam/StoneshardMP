/// @stoneforge return int
// The controller's completed-turn count lets C# distinguish an action from an idle world tick.
function MpWorldTurns()
{
    if (!instance_exists(o_player) || !instance_exists(o_controller))
        return -1;
    return o_controller.turns;
}
