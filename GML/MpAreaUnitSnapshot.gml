/// @stoneforge return string
// The owning game exports every real NPC/animal/enemy in its current room. Remote player ghosts are excluded.
function MpAreaUnitSnapshot()
{
    var _units = [];
    with (o_enemy)
    {
        if (object_index == o_stoneshardmp__ghost)
            continue;
        var _look = {};
        var _vars = ["idle_spr", "default_sprite", "fight_sprite", "corpse_sprite",
            "head_sprite", "ko_sprite", "jailed_spr", "spr_sleep", "spr_work",
            "avatar", "fixed_sprite", "flying_sprite", "npc_sprite"];
        for (var _v = 0; _v < array_length(_vars); _v++)
        {
            if (!variable_instance_exists(id, _vars[_v]))
                continue;
            var _value = variable_instance_get(id, _vars[_v]);
            // Runtime-generated sprites have process-local IDs and cannot be copied between games.
            if (is_numeric(_value) && (_value < 0 ||
                (sprite_exists(_value) && asset_get_index(sprite_get_name(_value)) == _value)))
                variable_struct_set(_look, _vars[_v], _value);
        }
        array_push(_units, {
            id: id, obj: object_index, x: xx div 26, y: yy div 26, hp: HP,
            state: string(state), sleeping: is_sleeping, neutral: is_neutral,
            flip: (image_xscale < 0), sprite: sprite_index, frame: image_index,
            speed: image_speed, angle: image_angle, alpha: image_alpha,
            look: _look, npcName: name,
            lifeAnimation: is_life
        });
    }
    return json_stringify(_units);
}
