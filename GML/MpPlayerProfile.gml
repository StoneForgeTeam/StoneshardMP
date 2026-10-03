/// @stoneforge return string
// Values the enemy-style inspection panel reads from a unit's resistance list. This is separate from player state:
// equipment and buffs can change it, but it does not need to travel every movement update.
function MpPlayerProfile()
{
    if (!instance_exists(o_player))
        return "";
    var _keys = ["Fortitude", "Physical_Resistance", "Nature_Resistance", "Magic_Resistance", "Slashing_Resistance", "Piercing_Resistance", "Blunt_Resistance", "Rending_Resistance", "Fire_Resistance", "Shock_Resistance", "Poison_Resistance", "Caustic_Resistance", "Frost_Resistance", "Arcane_Resistance", "Unholy_Resistance", "Sacred_Resistance", "Psionic_Resistance", "Stun_Resistance", "Knockback_Resistance", "Bleeding_Resistance", "Pain_Resistance"];
    var _text = "";
    with (o_player)
    {
        for (var _i = 0; _i < array_length(_keys); _i++)
        {
            var _key = _keys[_i];
            if (_i > 0)
                _text += "|";
            _text += string(variable_instance_exists(id, _key) ? variable_instance_get(id, _key) : 0);
        }
    }
    return _text;
}
