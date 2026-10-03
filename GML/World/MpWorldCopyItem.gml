/// @stoneforge return string
/// @stoneforge param tiles bool
/// @stoneforge param index int
// One thing MpWorldCopyBegin listed, as it is now: location number index (MpLocationExport) or, with tiles, tile
// number index (MpTileExport). "" if there's nothing to send for it.
function MpWorldCopyItem(tiles, index)
{
    if (tiles)
    {
        if (!variable_global_exists("mp_copy_tiles") || index >= array_length(global.mp_copy_tiles))
            return "";
        var _t = global.mp_copy_tiles[index];
        return MpTileExport(_t[0], _t[1]);
    }
    if (!variable_global_exists("mp_copy_locations") || index >= array_length(global.mp_copy_locations))
        return "";
    var _l = global.mp_copy_locations[index];
    return MpLocationExport(_l[0], _l[1], _l[2]);
}
