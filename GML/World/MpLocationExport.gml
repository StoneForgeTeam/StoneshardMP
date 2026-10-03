/// @stoneforge return string
/// @stoneforge param locationTag GmValue
/// @stoneforge param roomTag GmValue
/// @stoneforge param presetTag GmValue
// A location's saved state as the game keeps it in global.locationsRoomsDataMap - its tags, preset flags and
// entities (what's dead, taken, opened) - as JSON for the others (MpLocationStore). The tags keep their types (a
// room or preset tag can be a number: a ds_map key 3 isn't "3"). "" if there's none. (Legacy: scr_mp_send_location.)
function MpLocationExport(locationTag, roomTag, presetTag)
{
    var _preset = scr_locationRoomPresetGet(locationTag, roomTag, presetTag, false);
    if (_preset == -1)
        return "";
    var _entities = ds_map_find_value_ext(_preset, "entitiesDataMapString", "N/A");
    if (!is_string(_entities) || _entities == "N/A")
        return "";
    return json_stringify({
        tags: [locationTag, roomTag, presetTag],
        flags: ds_map_find_value_ext(_preset, "flags", 0),
        entities: _entities
    });
}
