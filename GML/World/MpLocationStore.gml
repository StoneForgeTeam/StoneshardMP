/// @stoneforge return string
/// @stoneforge param state string
// Another game's saved state for a location (MpLocationExport) into our world data, where the game keeps its own
// save of that location, so our next visit there loads theirs. The flags go too: they decide whether its spawners
// run on entry, so a location we never visited doesn't spawn fresh mobs on top of theirs. What happened, for the
// log. (Legacy: scr_mp_location_store.)
function MpLocationStore(state)
{
    if (!instance_exists(o_player))
        return "";
    var _s = json_parse(state);
    if (!is_struct(_s) || !is_array(_s.tags) || array_length(_s.tags) < 3)
        return "unreadable location";
    var _location = _s.tags[0];
    var _room = _s.tags[1];
    var _preset = _s.tags[2];
    var _what = string(_location) + " / " + string(_room) + " / " + string(_preset);
    // We're standing in it: our live copy is the current one.
    if (scr_locationGenerateTag() == _location && string(scr_locationRoomGenerateTag()) == string(_room))
        return "";
    // Only a state that reads back: a broken one would make the game rebuild the location from scratch.
    var _check = json_decode(_s.entities);
    if (_check == -1)
        return "location " + _what + ": its state didn't read back - kept ours";
    ds_map_destroy(_check);
    var _map = scr_locationRoomPresetGet(_location, _room, _preset, true);
    if (_map == -1)
        return "location " + _what + ": no place for it here";
    ds_map_set(_map, "flags", _s.flags);
    ds_map_set(_map, "entitiesDataMapString", _s.entities);
    return "location " + _what + ": stored";
}
