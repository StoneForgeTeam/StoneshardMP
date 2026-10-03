/// @stoneforge return string
// A portable host clock snapshot: seconds, minutes, hours, days, months.
function MpWorldClock()
{
    if (!variable_global_exists("timeDataMap") || !ds_exists(global.timeDataMap, ds_type_map))
        return "";
    return string(ds_map_find_value(global.timeDataMap, "seconds")) + "|"
        + string(ds_map_find_value(global.timeDataMap, "minutes")) + "|"
        + string(ds_map_find_value(global.timeDataMap, "hours")) + "|"
        + string(ds_map_find_value(global.timeDataMap, "days")) + "|"
        + string(ds_map_find_value(global.timeDataMap, "months"));
}
