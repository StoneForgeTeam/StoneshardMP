/// @stoneforge return bool
// The same non-combat idle turn used by a walked tile: global upkeep/time, skill alarms, then the unit turn
// loop. It only runs in a quiet, playable world; combat and cutscenes remain under their own future systems.
function MpWorldTick()
{
    if (!MpWorldTickReady())
        return false;
    with (o_player)
        scr_global_turn();
    with (o_skill)
        alarm[10] = 1;
    with (o_player)
        event_perform(ev_alarm, 4);
    return true;
}
