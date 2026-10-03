/// @stoneforge return string
/// @stoneforge param list int
/// @stoneforge param index int
/// @stoneforge param json string
/// @stoneforge param full bool
/// @stoneforge param host bool
// A contract from another game (its list, index and JSON; full: the host's full copy for a client just in its world)
// copied into ours in place (MpDsMapAssign), so the game's own references to it - the journal, the diary, a dungeon -
// see it. Deadlines (Contract_Deadline) are counted by the host's clock only (MpContractClockHour): a client takes the
// host's; the host keeps its own for a contract it already had taken, and takes the taker's for a newly taken one.
// An offer's expiry (Contract_Expiration) ticks in every game, so ours is kept, except in the full copy. A handed-out
// contract also goes into our journal and diary (MpContractJournal). Returns the contract's JSON as it now is here
// (what we have counts as synced: not sent back), "" if it couldn't be applied. (Legacy: scr_mp_contract_apply.)
function MpContractApply(list, index, json, full, host)
{
    var _list = MpContractList(list);
    if (_list == -1 || index < 0)
        return "";
    var _src = json_decode(json);
    if (_src == -1)
        return "";
    while (ds_list_size(_list) <= index)
    {
        ds_list_add(_list, ds_map_create());
        ds_list_mark_as_map(_list, ds_list_size(_list) - 1);
    }
    var _dst = ds_list_find_value(_list, index);
    var _wasTaken = ds_map_find_value(_dst, "isTaken") == true;
    if (!full)
    {
        if (host && _wasTaken && ds_map_exists(_dst, "Contract_Deadline"))
            ds_map_set(_src, "Contract_Deadline", ds_map_find_value(_dst, "Contract_Deadline"));
        if (ds_map_exists(_dst, "Contract_Expiration"))
            ds_map_set(_src, "Contract_Expiration", ds_map_find_value(_dst, "Contract_Expiration"));
    }
    MpDsMapAssign(_dst, _src);
    ds_map_destroy(_src);
    if (list == 1)
        MpContractJournal(index, _dst, _wasTaken);
    return json_encode(_dst);
}
