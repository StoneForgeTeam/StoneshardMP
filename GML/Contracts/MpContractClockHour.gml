/// @stoneforge return string
/// @stoneforge param occupied string
// The host's hourly contract clock, in place of the game's (o_time_controller user event 3) while playing together:
// every taken contract's deadline goes down an hour - by the contract list, not the world map's dungeon list as the
// game does - unless a player of this world is on its dungeon's world-map cell (the game's own pause, for anyone:
// us, or a tile in occupied, "x_y," each), and it fails at 0 as the game's does. Clients count none (theirs come with
// the host's contracts). What happened, for the log ("" if nothing). (Legacy: scr_mp_contract_clock_hour.)
function MpContractClockHour(occupied)
{
    var _list = MpContractList(1);
    if (_list == -1)
        return "";
    var _line = "";
    for (var _i = 0; _i < ds_list_size(_list); _i++)
    {
        var _map = ds_list_find_value(_list, _i);
        if (!ds_exists(_map, ds_type_map) || !ds_map_find_value(_map, "isTaken") || ds_map_find_value(_map, "isComplete"))
            continue;
        var _deadline = ds_map_find_value(_map, "Contract_Deadline");
        if (!is_numeric(_deadline))
            continue;
        var _at = false;
        var _dxy = ds_map_find_value(_map, "Dungeon_Coordinate");
        if (is_string(_dxy) && string_pos("/", _dxy) > 0)
        {
            var _dx = real(string_extract(_dxy, "/", 0));
            var _dy = real(string_extract(_dxy, "/", 1));
            _at = (global.playerGridX == _dx && global.playerGridY == _dy)
                || string_pos("," + string(_dx) + "_" + string(_dy) + ",", "," + occupied) > 0;
        }
        if (_at)
        {
            _line += " #" + string(_i) + " " + string(_deadline) + "h (paused: a player is at its dungeon)";
            continue;
        }
        _deadline -= 1;
        ds_map_replace(_map, "Contract_Deadline", _deadline);
        _line += " #" + string(_i) + " " + string(_deadline) + "h";
        if (_deadline <= 0)
        {
            _line += " FAILED";
            with (o_time_controller)
                scr_contract_delete(_map, false);
        }
    }
    return _line;
}
