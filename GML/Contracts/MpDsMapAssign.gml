/// @stoneforge return void
/// @stoneforge param dst GmValue
/// @stoneforge param src GmValue
// Make ds_map dst a copy of ds_map src (from json_decode), in place, so everything holding dst (the game keeps
// contracts by their map) sees the new contents. Keys src lacks are removed; nested lists and maps are updated in
// place (MpDsListFill), so references to them (the diary's contract targets) stay good. (Legacy: scr_mp_ds_map_assign.)
function MpDsMapAssign(dst, src)
{
    var _old = ds_map_keys_to_array(dst);
    for (var _i = 0; _i < array_length(_old); _i++)
    {
        if (!ds_map_exists(src, _old[_i]))
            ds_map_delete(dst, _old[_i]);
    }
    var _keys = ds_map_keys_to_array(src);
    for (var _j = 0; _j < array_length(_keys); _j++)
    {
        var _k = _keys[_j];
        var _v = ds_map_find_value(src, _k);
        if (ds_map_is_list(src, _k))
        {
            if (ds_map_exists(dst, _k) && ds_map_is_list(dst, _k))
                MpDsListFill(ds_map_find_value(dst, _k), _v);
            else
            {
                ds_map_delete(dst, _k);
                var _l = ds_list_create();
                MpDsListFill(_l, _v);
                ds_map_add_list(dst, _k, _l);
            }
        }
        else if (ds_map_is_map(src, _k))
        {
            if (ds_map_exists(dst, _k) && ds_map_is_map(dst, _k))
                ds_map_copy(ds_map_find_value(dst, _k), _v);
            else
            {
                ds_map_delete(dst, _k);
                var _m = ds_map_create();
                ds_map_copy(_m, _v);
                ds_map_add_map(dst, _k, _m);
            }
        }
        else
        {
            if (ds_map_exists(dst, _k) && (ds_map_is_list(dst, _k) || ds_map_is_map(dst, _k)))
                ds_map_delete(dst, _k);
            ds_map_set(dst, _k, _v);
        }
    }
}
