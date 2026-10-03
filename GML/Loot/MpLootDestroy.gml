/// @stoneforge return void
/// @stoneforge param inst GmValue
// Remove loot from the world, culled or not: a culled one leaves the culling controller's list (and its cached size)
// first, or the controller would read a destroyed instance. Never an item in flight: the throw reads it when it
// lands (removing it first crashed the game). (Legacy: scr_mp_loot_destroy.)
function MpLootDestroy(inst)
{
    with (o_physical_shell)
    {
        if (variable_instance_exists(id, "loot_object") && loot_object == inst)
            return;
    }
    with (o_cullingController)
    {
        var _i = ds_list_find_index(deactivatedInstancesList, inst);
        if (_i >= 0)
        {
            ds_list_delete(deactivatedInstancesList, _i);
            deactivatedInstancesListSize = ds_list_size(deactivatedInstancesList);
            instance_activate_object(inst);
        }
    }
    if (instance_exists(inst))
    {
        with (inst)
            instance_destroy();
    }
}
