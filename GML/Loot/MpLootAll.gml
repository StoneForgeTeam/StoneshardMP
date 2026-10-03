/// @stoneforge return GmValue
// Every ground loot instance in the room - the culling controller's deactivated ones (off screen) too, which with
// (o_loot) skips - but not an item in flight: a thrown item waits, hidden, where it will land, and is loot once it
// has. (Legacy: scr_mp_loot_all.)
function MpLootAll()
{
    // (A lookup table, not array_contains: the game's runtime predates it.)
    var _flying = ds_map_create();
    with (o_physical_shell)
    {
        if (variable_instance_exists(id, "loot_object") && instance_exists(loot_object))
            ds_map_set(_flying, string(real(loot_object)), true);
    }
    var _all = [];
    with (o_loot)
    {
        if (!persistent && !ds_map_exists(_flying, string(real(id))))
            array_push(_all, id);
    }
    with (o_cullingController)
    {
        for (var _i = 0; _i < ds_list_size(deactivatedInstancesList); _i++)
        {
            var _inst = ds_list_find_value(deactivatedInstancesList, _i);
            if ((_inst.object_index == o_loot || object_is_ancestor(_inst.object_index, o_loot)) && !_inst.persistent
                && !ds_map_exists(_flying, string(real(_inst))))
                array_push(_all, _inst);
        }
    }
    ds_map_destroy(_flying);
    return _all;
}
