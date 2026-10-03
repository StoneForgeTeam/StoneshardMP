/// @stoneforge return string
/// @stoneforge param inst GmValue
// Loot as JSON in the game's own save format (scr_locationRoomEntityLootSaveDataGet: object or weapon, position,
// stack, charge, its item data). "" for static loot, which the game doesn't save either - it's part of the location,
// so the other game has it already. (Legacy: scr_mp_loot_json.)
function MpLootJson(inst)
{
    // (The save helper runs in with(), which skips a culled instance: woken up for it.)
    var _culled = !instance_exists(inst);
    if (_culled)
        instance_activate_object(inst);
    var _json = "";
    if (instance_exists(inst) && inst.roomEntityType != "static")
    {
        var _map = scr_locationRoomEntityLootSaveDataGet(inst);
        _json = json_encode(_map);
        ds_map_destroy(_map);
    }
    if (_culled)
        instance_deactivate_object(inst);
    return _json;
}
