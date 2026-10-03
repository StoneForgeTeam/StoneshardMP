/// @stoneforge return string
// The weather as the game keeps it - global.weatherDataMap (rain, thunderstorm, duration, phase) and
// global.smokeDataMap (fog) - as "weatherJSON|fogJSON"; "" with no game loaded. (Legacy: scr_mp_weather_state.)
function MpWeatherState()
{
    if (!variable_global_exists("weatherDataMap") || !variable_global_exists("smokeDataMap"))
        return "";
    if (!ds_exists(global.weatherDataMap, ds_type_map) || !ds_exists(global.smokeDataMap, ds_type_map))
        return "";
    return json_encode(global.weatherDataMap) + "|" + json_encode(global.smokeDataMap);
}
