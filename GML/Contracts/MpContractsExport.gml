/// @stoneforge return string
/// @stoneforge param list int
// Every contract in a list (MpContractList) as it is now, each its own JSON, in a JSON array by index ("" for a slot
// that isn't a contract) - to compare with what was last sent (ContractSync). "" with no such list.
function MpContractsExport(list)
{
    var _list = MpContractList(list);
    if (_list == -1)
        return "";
    var _out = [];
    for (var _i = 0; _i < ds_list_size(_list); _i++)
    {
        var _map = ds_list_find_value(_list, _i);
        array_push(_out, ds_exists(_map, ds_type_map) ? json_encode(_map) : "");
    }
    return json_stringify(_out);
}
