/// @stoneforge return void
/// @stoneforge param dst GmValue
/// @stoneforge param src GmValue
// Make ds_list dst a copy of ds_list src (from json_decode), in place - its nested lists and maps too, element by
// element, so whatever holds one of them (the diary shows a contract's targets straight from its inner lists) still
// holds the same, now updated, one. New nested ones are made and marked; extra elements dropped from the end. One
// level down (a contract's Targets is a list of lists of plain values); deeper is copied as is. (Legacy:
// scr_mp_ds_list_fill.)
function MpDsListFill(dst, src)
{
    var _n = ds_list_size(src);
    for (var _i = 0; _i < _n; _i++)
    {
        var _v = ds_list_find_value(src, _i);
        var _have = _i < ds_list_size(dst);
        if (ds_list_is_list(src, _i))
        {
            if (_have && ds_list_is_list(dst, _i))
                ds_list_copy(ds_list_find_value(dst, _i), _v);
            else
            {
                var _l = ds_list_create();
                ds_list_copy(_l, _v);
                if (_have)
                    ds_list_replace(dst, _i, _l);
                else
                    ds_list_add(dst, _l);
                ds_list_mark_as_list(dst, _i);
            }
        }
        else if (ds_list_is_map(src, _i))
        {
            if (_have && ds_list_is_map(dst, _i))
                ds_map_copy(ds_list_find_value(dst, _i), _v);
            else
            {
                var _m = ds_map_create();
                ds_map_copy(_m, _v);
                if (_have)
                    ds_list_replace(dst, _i, _m);
                else
                    ds_list_add(dst, _m);
                ds_list_mark_as_map(dst, _i);
            }
        }
        else if (_have)
            ds_list_replace(dst, _i, _v);
        else
            ds_list_add(dst, _v);
    }
    while (ds_list_size(dst) > _n)
        ds_list_delete(dst, ds_list_size(dst) - 1);
}
