/// @stoneforge return string
/// @stoneforge param dungeonFloor int
// Which floor build a retry count belongs to: the world-map cell, the floor and the in-game day (the seed's own
// inputs). (Legacy: scr_mp_dungeon_retry_key.)
function MpDungeonRetryKey(dungeonFloor)
{
    return string(global.playerGridX) + "_" + string(global.playerGridY) + "_" + string(dungeonFloor) + "_"
        + string(floor(scr_timeGetTimestamp() / 1440));
}
