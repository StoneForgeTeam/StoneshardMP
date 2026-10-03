/// @stoneforge return GmValue
/// @stoneforge param list int
// A contract list - 0: every kind of contract (global.contractsDatabaseList), 1: the ones handed out
// (global.contractsDataList; a contract's index there is its id everywhere: a dungeon's contract_map) - or -1.
// (Legacy: scr_mp_contract_list.)
function MpContractList(list)
{
    var _list = -1;
    if (list == 0 && variable_global_exists("contractsDatabaseList"))
        _list = global.contractsDatabaseList;
    else if (list == 1 && variable_global_exists("contractsDataList"))
        _list = global.contractsDataList;
    if (is_undefined(_list) || !is_real(_list) || _list < 0 || !ds_exists(_list, ds_type_list))
        return -1;
    return _list;
}
