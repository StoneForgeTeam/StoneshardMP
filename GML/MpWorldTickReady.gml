/// @stoneforge return bool
// Don't inject an idle turn while a scene transition, cutscene, or another local turn is still in progress.
// (Called from C# with no instance: scr_is_cutscene reads object_index, so it runs as the player - without one it
// stopped the game.)
function MpWorldTickReady()
{
    if (!instance_exists(o_player) || !instance_exists(o_controller))
        return false;
    if (instance_exists(o_black_overlay))
        return false;
    var _ready = true;
    with (o_player)
    {
        if (scr_is_cutscene() || lock_movement || max(alarm[4], alarm[1]) > 0)
            _ready = false;
    }
    return _ready;
}
