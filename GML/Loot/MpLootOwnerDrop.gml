/// @stoneforge return bool
/// @stoneforge param drop string
// Area owner: a follower dropped something - {j: its JSON, f: its throw (MpLootFlight), t: the follower's token for
// it}. Made here and thrown along the same arc; our next diff adds it, token and all, to every follower - the one
// whose drop it is then keeps its own copy as the synced item (MpLootFollowerAdd). (Legacy: LOOT_DROP in
// scr_mp_loot_handle.)
function MpLootOwnerDrop(drop)
{
    if (!instance_exists(o_player))
        return false;
    var _d = json_parse(drop);
    if (!is_struct(_d))
        return false;
    var _inst = MpLootCreate(_d.j);
    if (_inst == noone)
        return false;
    MpLootFly(_inst, _d.f);
    ds_map_set(global.mp_loot_token_of, MpLootIkey(_inst), _d.t);
    return true;
}
