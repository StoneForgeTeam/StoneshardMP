/// @stoneforge return void
/// @stoneforge param state string
// The host's weather and fog (MpWeatherState), value by value into our own maps - the game's rain and fog objects
// read them from there. (Legacy: scr_mp_weather_apply.)
function MpWeatherApply(state)
{
    if (MpWeatherState() == "")
        return;
    var _sep = string_pos("|", state);
    if (_sep < 2)
        return;
    var _parts = [string_copy(state, 1, _sep - 1), string_delete(state, 1, _sep)];
    var _targets = [global.weatherDataMap, global.smokeDataMap];
    for (var _i = 0; _i < 2; _i++)
    {
        var _src = json_decode(_parts[_i]);
        if (_src == -1)
            continue;
        var _keys = ds_map_keys_to_array(_src);
        for (var _k = 0; _k < array_length(_keys); _k++)
            ds_map_set(_targets[_i], _keys[_k], ds_map_find_value(_src, _keys[_k]));
        ds_map_destroy(_src);
    }
}
