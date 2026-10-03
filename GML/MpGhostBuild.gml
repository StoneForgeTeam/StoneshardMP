/// @stoneforge return string
/// @stoneforge param look string
/// @stoneforge param old0 double
/// @stoneforge param old1 double
/// @stoneforge param old2 double
/// @stoneforge param old3 double
/// @stoneforge param old4 double
// A ghost's sprites (its 5 animation rows, "s0,s1,s2,s3,s4") built from another player's look (MpPlayerLook's JSON)
// by the game's own compositor: scr_playerSpriteUpdate run against their layers, our player's globals put back after.
// It frees what's in playerSpriteArray, so it's handed the ghost's previous sprites (old0-4, -4 for none) rather than
// ours. Each layer's sprite and mask sit at their origins in the other game while it composites, then back as they
// were. Call it in a Draw event (it draws to surfaces). "" if the look can't be built.
function MpGhostBuild(look, old0, old1, old2, old3, old4)
{
    var _look = json_parse(look);
    if (!is_array(_look) || array_length(_look) < 5)
        return "";
    var _rows = _look[4];
    var _n = array_length(_rows);
    if (_look[0] < 1 || _look[1] < 1 || _n < 1 || !sprite_exists(_look[3]))
        return "";
    // The layers as scr_playerSpriteInit makes them: 13 values, then two flags.
    var _parts = array_create(_n, 0);
    for (var _i = 0; _i < _n; _i++)
    {
        var _part = array_create(15, 0);
        for (var _j = 0; _j < 13; _j++)
            _part[_j] = _rows[_i][_j];
        _part[13] = false;
        _part[14] = false;
        _parts[_i] = _part;
    }

    var _svParts = global.playerSpritePartsArray;
    var _svHeight = global.playerSpritePartsArrayHeight;
    var _svNX = global.playerSpriteImageNumberX;
    var _svNY = global.playerSpriteImageNumberY;
    var _svGround = global.playerSpriteGround;
    var _svBody = global.playerSpriteBody;
    var _svArray = global.playerSpriteArray;
    var _svUpdate = global.playerSpriteUpdate;
    var _svSpeed = global.playerSpriteSpeed;
    var _svIndex = global.playerSpriteIndex;

    var _arr = array_create(5, -4);
    _arr[0] = old0;
    _arr[1] = old1;
    _arr[2] = old2;
    _arr[3] = old3;
    _arr[4] = old4;
    global.playerSpriteArray = _arr;
    global.playerSpritePartsArray = _parts;
    global.playerSpritePartsArrayHeight = _n;
    global.playerSpriteImageNumberX = _look[0];
    global.playerSpriteImageNumberY = _look[1];
    global.playerSpriteGround = _look[2];
    global.playerSpriteBody = _look[3];
    global.playerSpriteUpdate = true;

    // Their origins on, ours saved first and put back in reverse (a sprite used by several layers ends as it was).
    var _saved = array_create(_n * 2, -1);
    for (var _s = 0; _s < _n; _s++)
    {
        var _spr = _parts[_s][0];
        var _msk = _parts[_s][11];
        if (sprite_exists(_spr))
            _saved[_s * 2] = [_spr, sprite_get_xoffset(_spr), sprite_get_yoffset(_spr)];
        if (sprite_exists(_msk))
            _saved[_s * 2 + 1] = [_msk, sprite_get_xoffset(_msk), sprite_get_yoffset(_msk)];
    }
    for (var _a = 0; _a < _n; _a++)
    {
        if (sprite_exists(_parts[_a][0]))
            sprite_set_offset(_parts[_a][0], _rows[_a][13], _rows[_a][14]);
        if (sprite_exists(_parts[_a][11]))
            sprite_set_offset(_parts[_a][11], _rows[_a][15], _rows[_a][16]);
    }

    scr_playerSpriteUpdate();

    for (var _r = array_length(_saved) - 1; _r >= 0; _r--)
    {
        var _entry = _saved[_r];
        if (is_array(_entry))
            sprite_set_offset(_entry[0], _entry[1], _entry[2]);
    }
    var _built = global.playerSpriteArray;
    var _result = string(_built[0]) + "," + string(_built[1]) + "," + string(_built[2]) + "," + string(_built[3]) + "," + string(_built[4]);

    global.playerSpritePartsArray = _svParts;
    global.playerSpritePartsArrayHeight = _svHeight;
    global.playerSpriteImageNumberX = _svNX;
    global.playerSpriteImageNumberY = _svNY;
    global.playerSpriteGround = _svGround;
    global.playerSpriteBody = _svBody;
    global.playerSpriteArray = _svArray;
    global.playerSpriteUpdate = _svUpdate;
    global.playerSpriteSpeed = _svSpeed;
    global.playerSpriteIndex = _svIndex;
    return _result;
}
