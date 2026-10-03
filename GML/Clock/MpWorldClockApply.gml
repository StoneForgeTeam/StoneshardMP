/// @stoneforge return void
/// @stoneforge param seconds int
/// @stoneforge param minutes int
/// @stoneforge param hours int
/// @stoneforge param days int
/// @stoneforge param months int
// Make the client clock match the host, using normal elapsed-time processing for small forward gaps so native
// minute/hour/day effects still fire. Larger or backwards corrections are applied exactly.
function MpWorldClockApply(seconds, minutes, hours, days, months)
{
    if (!variable_global_exists("timeDataMap") || !ds_exists(global.timeDataMap, ds_type_map))
        return;
    var _host = minutes + hours * 60 + days * 1440 + months * 43200;
    var _diff = _host - scr_timeGetTimestamp();
    if (_diff > 0 && _diff <= 1440)
        scr_timePartsUpdate(_diff);
    else if (_diff != 0)
        scr_timeSet(seconds, minutes, hours, days, months);
    ds_map_set(global.timeDataMap, "seconds", seconds);
}
