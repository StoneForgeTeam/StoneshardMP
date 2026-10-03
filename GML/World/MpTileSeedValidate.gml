/// @stoneforge return string
/// @stoneforge param key GmValue
/// @stoneforge param tileX GmValue
/// @stoneforge param tileY GmValue
// In place of scr_globaltile_seed_validate: the game's own version, except that a world-map tile's layout seeds
// (first visit, or a respawn) come from the world seed instead of randomize() - so every game in the same world
// builds the same area there (MpTileSeed). "x_y" when a seed of that tile was set (to tell the others), "" if not.
// (Legacy: scr_globaltile_seed_validate_replacement.)
function MpTileSeedValidate(key, tileX, tileY)
{
    if (tileX == undefined)
        tileX = global.playerGridX;
    if (tileY == undefined)
        tileY = global.playerGridY;
    if (global.playerGridX == -4)
        return "";
    var _locationTag = scr_locationGenerateTag(tileX, tileY);
    var _seed;
    switch (key)
    {
        case "seed":
        case "growSeed":
        case "mobsSeed":
        case "presetSeed":
            _seed = scr_globaltile_seed_get(key, tileX, tileY);
            if (scr_locationExists(_locationTag) && _seed == -1)
            {
                scr_globaltile_set(key, scr_globaltile_get(key, tileX, tileY, -1, global.globaltile_lookup_temp), tileX, tileY);
                return string(tileX) + "_" + string(tileY);
            }
            if (_seed == -1 || _seed == -2)
            {
                scr_globaltile_set(key, MpTileSeed(key, tileX, tileY, _seed == -2), tileX, tileY);
                return string(tileX) + "_" + string(tileY);
            }
            break;
        case "containersSeed":
        case "Trade_Seed":
            _seed = scr_globaltile_seed_get(key, tileX, tileY);
            if (_seed == -1)
            {
                scr_globaltile_set(key, scr_globaltile_get(key, tileX, tileY, -1, global.globaltile_lookup_temp), tileX, tileY);
                return string(tileX) + "_" + string(tileY);
            }
            break;
    }
    return "";
}
