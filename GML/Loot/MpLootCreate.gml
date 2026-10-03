/// @stoneforge return GmValue
/// @stoneforge param json string
// Loot made from its JSON (MpLootJson) the way loading a location makes it: created, then given its saved state -
// which also lands it where it lay (no throw to a neighbouring tile). noone if it can't be. (Legacy:
// scr_mp_loot_create.)
function MpLootCreate(json)
{
    if (json == "")
        return noone;
    var _map = json_decode(json);
    if (_map == -1)
        return noone;
    var _inst = scr_locationRoomEntityLootInstanceCreate(_map);
    if (_inst != -4 && instance_exists(_inst))
    {
        scr_locationRoomEntityLootSaveDataSet(_inst, _map);
        // (Came from the other game: never a drop of ours.)
        MpLootFlagSet(_inst, 1);
    }
    else
        _inst = noone;
    ds_map_destroy(_map);
    return _inst;
}
