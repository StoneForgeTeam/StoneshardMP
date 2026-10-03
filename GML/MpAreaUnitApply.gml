/// @stoneforge return void
/// @stoneforge param snapshot string
// Follower reconciliation: bind matching local NPCs to the owner's IDs, move them through the game's grids,
// disable their local AI, and remove local units absent from the owner's roster.
function MpAreaUnitApply(snapshot)
{
    if (!instance_exists(o_player) || !instance_exists(o_controller) || snapshot == "")
        return;
    if (!variable_global_exists("mp_area_units"))
        global.mp_area_units = ds_map_create();
    var _list = json_parse(snapshot);
    var _epoch = (variable_global_exists("mp_area_epoch") ? global.mp_area_epoch + 1 : 1);
    global.mp_area_epoch = _epoch;
    for (var _i = 0; _i < array_length(_list); _i++)
    {
        var _u = _list[_i];
        var _hid = string(_u.id);
        var _inst = ds_map_find_value(global.mp_area_units, _hid);
        if (_inst == undefined || !instance_exists(_inst))
        {
            _inst = noone;
            with (o_enemy)
            {
                if (object_index == _u.obj && (!variable_instance_exists(id, "mp_area_seen")) && xx div 26 == _u.x && yy div 26 == _u.y)
                    _inst = id;
            }
            if (_inst == noone)
                _inst = scr_enemy_create(_u.x * 26 + 13, _u.y * 26 + 13, _u.obj, false, false);
            if (_inst == noone || _inst == -4 || !instance_exists(_inst))
                continue;
            ds_map_replace(global.mp_area_units, _hid, _inst);
        }
        with (_inst)
        {
            mp_area_seen = _epoch;
            ai_is_on = false;
            HP = max(1, _u.hp);
            state = _u.state;
            is_sleeping = _u.sleeping;
            is_neutral = _u.neutral;
            // Copy the source sprites used by native animation selection, not only its current sprite.
            var _vars = ["idle_spr", "default_sprite", "fight_sprite", "corpse_sprite",
                "head_sprite", "ko_sprite", "jailed_spr", "spr_sleep", "spr_work",
                "avatar", "fixed_sprite", "flying_sprite", "npc_sprite"];
            for (var _v = 0; _v < array_length(_vars); _v++)
            {
                var _key = _vars[_v];
                if (!variable_struct_exists(_u.look, _key) || !variable_instance_exists(id, _key))
                    continue;
                var _value = variable_struct_get(_u.look, _key);
                if (_value < 0 || (sprite_exists(_value) && asset_get_index(sprite_get_name(_value)) == _value))
                    variable_instance_set(id, _key, _value);
            }
            name = _u.npcName;
            image_xscale = (_u.flip ? -abs(image_xscale) : abs(image_xscale));
            // The authoritative unit's current animation is part of its state: idle/fight/work/sleep sprites
            // may differ between independent local spawns even when the object and AI state match.
            if (sprite_exists(_u.sprite) && asset_get_index(sprite_get_name(_u.sprite)) == _u.sprite)
            {
                sprite_index = _u.sprite;
                // scr_npc_change_animation also updates the render sprite and leaves normal unit animation.
                // In particular, work/sleep poses use is_life=false; keeping the local value restores idle.
                spr = _u.sprite;
                is_life = _u.lifeAnimation;
            }
            image_index = _u.frame;
            image_speed = _u.speed;
            image_angle = _u.angle;
            image_alpha = _u.alpha;
            scr_set_hl();
        }
        MpGhostUnitMove(_inst, _u.x, _u.y);
    }
    with (o_player)
    {
        if (enemy_iteration == 0)
        {
            for (var _j = ds_list_size(enemylist) - 1; _j >= 0; _j--)
            {
                var _e = ds_list_find_value(enemylist, _j);
                if (instance_exists(_e) && variable_instance_exists(_e, "mp_area_seen") && _e.mp_area_seen == _epoch)
                    ds_list_delete(enemylist, _j);
            }
        }
    }
    // Anything the owner did not send does not exist in this shared area. Remove it without the normal enemy
    // Destroy event (no follower-side loot, corpse, or kill credit), and clear its occupancy first.
    var _gone = [];
    with (o_enemy)
    {
        if (object_index != o_stoneshardmp__ghost && (!variable_instance_exists(id, "mp_area_seen") || mp_area_seen != _epoch))
            array_push(_gone, id);
    }
    for (var _g = 0; _g < array_length(_gone); _g++)
    {
        var _dead = _gone[_g];
        if (!instance_exists(_dead))
            continue;
        with (_dead)
        {
            var _x = xx div 26;
            var _y = yy div 26;
            with (o_controller)
            {
                scr_collision_clear(newgrid, _x, _y, true);
                if (ds_grid_get_ext(posgrid, _x, _y) == other.id)
                    ds_grid_set_ext(posgrid, _x, _y, -4);
            }
            scr_enemy_poly_cell_clear(_x, _y);
            scr_enemy_poly_cell_posgrid_clear(_x, _y);
            with (o_player)
            {
                var _index = ds_list_find_index(enemylist, other.id);
                if (_index >= 0)
                    ds_list_delete(enemylist, _index);
            }
            instance_destroy(id, false);
        }
    }
}
