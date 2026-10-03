/// @stoneforge return double
/// @stoneforge param key string
/// @stoneforge param tileX double
/// @stoneforge param tileY double
/// @stoneforge param respawn bool
// A world-map tile's layout seed of kind key, from the world seed: the same in every game in this world. A respawn
// also mixes in the in-game day (the host keeps everyone's clock), so it gets a new layout - the same in every game
// that respawns it that day. Leaves the random generator randomized after, as the randomize() it replaces.
// (Legacy: scr_mp_tile_seed.)
function MpTileSeed(key, tileX, tileY, respawn)
{
    var _kind = 1;
    if (key == "growSeed")
        _kind = 2;
    else if (key == "mobsSeed")
        _kind = 3;
    else if (key == "presetSeed")
        _kind = 4;
    var _salt = respawn ? floor(scr_timeGetTimestamp() / 1440) + 1 : 0;
    var _mix = global.seed + tileX * 73856093 + tileY * 19349663 + _kind * 83492791 + _salt * 2654435761;
    random_set_seed(abs(_mix) mod 2147483647);
    var _value = irandom_range(1, 2000000000);
    randomize();
    return _value;
}
