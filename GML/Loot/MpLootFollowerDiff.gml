/// @stoneforge return void
/// @stoneforge param diff string
// Follower: what changed on the owner (MpLootOwnerDiff) - loot gone from its world removed here, new loot bound or
// made. (Legacy: LOOT_REMOVE and LOOT_ADD in scr_mp_loot_handle.)
function MpLootFollowerDiff(diff)
{
    var _d = json_parse(diff);
    if (!is_struct(_d))
        return;
    for (var _g = 0; _g < array_length(_d.gone); _g++)
    {
        var _inst = ds_map_find_value(global.mp_loot_map, _d.gone[_g]);
        ds_map_delete(global.mp_loot_map, _d.gone[_g]);
        if (!is_undefined(_inst))
            MpLootDestroy(_inst);
    }
    for (var _a = 0; _a < array_length(_d.add); _a++)
        MpLootFollowerAdd(_d.add[_a]);
}
