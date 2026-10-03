/// @stoneforge return string
/// @stoneforge param dropWindow bool
/// @stoneforge param settling bool
/// @stoneforge param pendingMs int
/// @stoneforge param spots string
// Follower, every few frames once we have the owner's snapshot: what to tell the owner - JSON {taken: [the owner's
// ids of loot we picked up], drops: [{j: JSON, f: throw, t: token}]} - "" if nothing. (Legacy:
// scr_mp_loot_client_step.)
// - Picked up: bound loot that left our world.
// - Dropped: sent to the owner as soon as we see it, with its throw (MpLootFlight) and a token, and ours kept, in the
//   air. The owner makes it and throws it along the same arc; its diff brings it back with the token and ours becomes
//   the synced item (MpLootFollowerAdd). One the owner doesn't bring back within a few seconds (it didn't take it) is
//   removed: the owner's list decides what lies here.
// A drop is decided once, when we first see the loot: it must turn up next to our player while dropWindow is open
// (our player just dropped something: the game's "dropped" log line), or by where one of our arrows just landed
// (spots: "x,y;x,y", MpLootShotSpot - an arrow drops its ammo by its target), or have flown there in one of our own
// throws (an o_physical_shell of ours carrying it: marked our drop while it's still in the air). Any other unbound loot isn't ours to add - the
// game swapping an item for a new one (food changing), loot made here that the owner makes too - and is removed: the
// owner's list decides what lies here (reporting those made the owner create copies endlessly). Except while settling
// (the first seconds after the snapshot: loot still turning up is the area finishing loading here).
function MpLootFollowerStep(dropWindow, settling, pendingMs, spots)
{
    if (!instance_exists(o_player) || !variable_global_exists("mp_loot_map"))
        return "";
    var _taken = [];
    var _hids = ds_map_keys_to_array(global.mp_loot_map);
    for (var _i = 0; _i < array_length(_hids); _i++)
    {
        if (MpLootGone(ds_map_find_value(global.mp_loot_map, _hids[_i])))
        {
            ds_map_delete(global.mp_loot_map, _hids[_i]);
            array_push(_taken, _hids[_i]);
        }
    }
    // Items in our throws: ours.
    with (o_physical_shell)
    {
        if (variable_instance_exists(id, "loot_object") && instance_exists(loot_object) && variable_instance_exists(id, "owner")
            && is_player(owner) && !MpLootFlag(loot_object, 2))
            MpLootFlagSet(loot_object, 4);
    }
    // Where loot can be our drop: by our player while the drop window is open, by our arrows' landing spots.
    var _near = [];
    if (dropWindow)
        array_push(_near, [o_player.x, o_player.y]);
    if (spots != "")
    {
        var _parts = string_split(spots, ";");
        for (var _s = 0; _s < array_length(_parts); _s++)
        {
            var _xy = string_split(_parts[_s], ",");
            if (array_length(_xy) == 2)
                array_push(_near, [real(_xy[0]), real(_xy[1])]);
        }
    }
    var _drops = [];
    var _all = MpLootAll();
    for (var _j = 0; _j < array_length(_all); _j++)
    {
        var _inst = _all[_j];
        if (ds_map_exists(global.mp_loot_hid_of, MpLootIkey(_inst)) || _inst.roomEntityType == "static")
            continue;
        if (!MpLootFlag(_inst, 2))
        {
            MpLootFlagSet(_inst, 2);
            if (!MpLootFlag(_inst, 1))
            {
                for (var _n = 0; _n < array_length(_near); _n++)
                {
                    if (point_distance(_inst.x, _inst.y, _near[_n][0], _near[_n][1]) <= 80)
                    {
                        MpLootFlagSet(_inst, 4);
                        break;
                    }
                }
            }
        }
        if (!MpLootFlag(_inst, 4))
        {
            if (!settling)
                MpLootDestroy(_inst);
            continue;
        }
        // (8: sent, waiting for the owner's copy.)
        var _k = MpLootIkey(_inst);
        if (MpLootFlag(_inst, 8))
        {
            if (current_time - ds_map_find_value_ext(global.mp_loot_sent_at, _k, current_time) > pendingMs)
                MpLootDestroy(_inst);
            continue;
        }
        var _json = MpLootJson(_inst);
        if (_json == "")
            continue;
        global.mp_loot_next_token += 1;
        var _token = string(global.mp_loot_next_token) + "_" + string(irandom(999999));
        array_push(_drops, { j: _json, f: MpLootFlight(_inst), t: _token });
        MpLootFlagSet(_inst, 8);
        ds_map_set(global.mp_loot_pending, _token, _inst);
        ds_map_set(global.mp_loot_sent_at, _k, current_time);
    }
    if (array_length(_taken) == 0 && array_length(_drops) == 0)
        return "";
    return json_stringify({ taken: _taken, drops: _drops });
}
