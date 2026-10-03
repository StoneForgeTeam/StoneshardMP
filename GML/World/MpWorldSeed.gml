/// @stoneforge return double
// The seed our world map is made from (every location, village and dungeon): a new character made for this world
// takes it.
function MpWorldSeed()
{
    return variable_global_exists("seed") ? global.seed : -1;
}
