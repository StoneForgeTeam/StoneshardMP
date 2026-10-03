/// @stoneforge return string
// Our player's look, as JSON ("" before the game has one): the layers o_player composites its sprite from
// (global.playerSpritePartsArray - sprite ids are the same in every game, from the same data.win) and the frame grid,
// ground and body sprites. Each layer: its 13 values, then its sprite's and its mask's origins as they are here -
// equipment origins are set per wearer at run time (scr_itemCharSpritesInit), so another game's copy of the sprite
// may sit differently.
function MpPlayerLook()
{
    if (!variable_global_exists("playerSpritePartsArray") || !variable_global_exists("playerSpritePartsArrayHeight"))
        return "";
    var _count = global.playerSpritePartsArrayHeight;
    var _rows = array_create(_count, 0);
    for (var _i = 0; _i < _count; _i++)
    {
        var _part = global.playerSpritePartsArray[_i];
        var _row = array_create(17, 0);
        for (var _j = 0; _j < 13; _j++)
            _row[_j] = _part[_j];
        var _spr = _part[0];
        var _msk = _part[11];
        if (sprite_exists(_spr))
        {
            _row[13] = sprite_get_xoffset(_spr);
            _row[14] = sprite_get_yoffset(_spr);
        }
        if (sprite_exists(_msk))
        {
            _row[15] = sprite_get_xoffset(_msk);
            _row[16] = sprite_get_yoffset(_msk);
        }
        _rows[_i] = _row;
    }
    var _look = array_create(5, 0);
    _look[0] = global.playerSpriteImageNumberX;
    _look[1] = global.playerSpriteImageNumberY;
    _look[2] = global.playerSpriteGround;
    _look[3] = global.playerSpriteBody;
    _look[4] = _rows;
    return json_stringify(_look);
}
