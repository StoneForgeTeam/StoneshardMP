using System;
using System.Linq;
using StoneForge;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Players;

// Our player as the others see it - where and how it's drawn, its look and resistances. (Legacy: scr_mp_send_state,
// scr_mp_send_look.)
internal static class OurPlayer
{
    private static readonly string[] Resistances = PlayerProfile.ResistanceNames;
    // (Many features ask where we are each frame: worked out once a frame.)
    private static long _stateFrame = -1;
    private static PlayerState? _state;
    private static long _frame;
    // The animation row and shadow shift, read from the game's arrays only when the sprite drawn changes (or every
    // ArraysEvery frames), and kept: handing a game array back in is what failed - a reference StoneForge no longer held,
    // which (before StoneForge 0.9.6) raised an error inside the call and left the game's error handling broken, so the
    // next array access anywhere crashed it: the second player out of the inn, every time. Kept as last read if a read
    // fails.
    private const int ArraysEvery = 60;
    private static double _arraysSprite = double.NaN;
    private static long _arraysFrame = long.MinValue;
    private static int _row;
    private static double _shiftX, _shiftY;

    /// <summary>A new frame (the mod's Tick): what we worked out last frame is old now.</summary>
    public static void NewFrame() => _frame++;

    /// <summary>Where we are (WorldMap.Place): the room, "#f&lt;floor&gt;" in a dungeon (every floor of one is the same
    /// room), and "@x_y", the world-map cell (neighbouring areas of the world map are built in the same room). Null with
    /// no game world - or no player.</summary>
    public static string? Place => State()?.Place;

    /// <summary>Where and how our player is drawn this frame (null with no player): the place, what o_player draws
    /// with - stX / stY / stScale* (scr_spriteTransformUpdate: the bob and lean), frame, animation row, depth, visible,
    /// tilt, hit flash (diss), alpha - its shadow as scr_draw_self_shadow places it, its cell, its health and energy.</summary>
    public static PlayerState? State()
    {
        if (_stateFrame == _frame)
            return _state;
        _stateFrame = _frame;
        _state = ReadState();
        return _state;
    }

    /// <summary>Our player's instance (none with no player).</summary>
    public static Instance Instance => Instances.All(GameObjectId.o_player).FirstOrDefault();

    private static PlayerState? ReadState()
    {
        Instance player = Instance;
        if (player.IsNone || WorldMap.Place is not { } place)
            return null;

        double V(string name) => player.Get(name).AsReal;
        double sprite = V("sprite_index");
        if (sprite != _arraysSprite || _frame - _arraysFrame >= ArraysEvery)
        {
            _arraysSprite = sprite;
            _arraysFrame = _frame;
            ReadArrays(player, sprite);
        }
        int row = _row;
        double shiftX = _shiftX, shiftY = _shiftY;
        double shadowAlpha = 0;
        if (V("isGround") == 1 && !player.Get("hasFootwave").AsBool && !player.Get("is_sleeping").AsBool
            && (player.Get("is_life").AsBool || V("image_speed") == 0))
            shadowAlpha = V("shadow_spr_alpha") * V("shadow_spr_alpha_multiplier");
        double scaleX = V("image_xscale"), scaleY = V("image_yscale"), shadowScale = V("shadow_scale");
        return new PlayerState(place, (float)V("stX"), (float)V("stY"), (float)V("stScaleX"), (float)V("stScaleY"),
            (float)V("image_index"), (byte)row, (int)V("depth"), player.Get("visible").AsBool, (float)V("image_angle"),
            (float)V("diss"), (float)V("image_alpha"), (int)V("shadow_spr"),
            (float)(V("draw_x") + shiftX * scaleX * shadowScale), (float)(V("draw_y") + (5.5 + shiftY) * shadowScale),
            (float)(scaleX * shadowScale), (float)(scaleY * shadowScale), (float)shadowAlpha,
            (short)Math.Truncate(V("xx") / 26), (short)Math.Truncate(V("yy") / 26),
            (float)V("HP"), (float)V("max_hp"), (float)V("MP"), (float)V("max_mp"));
    }

    // Which of the 5 animation rows the sprite is (global.playerSpriteArray), and its shadow's shift (shadow_spr_shift) -
    // every read within the array's length: one past its end is an error in the game. A read that fails keeps the last.
    private static void ReadArrays(Instance player, double sprite)
    {
        try
        {
            using (GmArray? rows = Game.Global["playerSpriteArray"].AsArray)
            {
                int length = rows == null ? 0 : Math.Min(rows.Length, 5), row = 0;
                for (int i = 0; i < length; i++)
                    if (rows![i].AsReal == sprite)
                    {
                        row = i;
                        break;
                    }
                _row = row;
            }
            using (GmArray? shift = player.Get("shadow_spr_shift").AsArray)
                if (shift != null && shift.Length >= 2)
                {
                    _shiftX = shift[0].AsReal;
                    _shiftY = shift[1].AsReal;
                }
        }
        catch (GameCallException) { }
    }

    /// <summary>The values the game's enemy-style inspection card reads from a unit's resistances, "|" between (""
    /// with no player): sent apart from the state - equipment and buffs change them, rarely.</summary>
    public static string Profile()
    {
        Instance player = Instance;
        if (player.IsNone)
            return "";
        return string.Join("|", Resistances.Select(name => player.Get(name) is { IsUndefined: false } value ? value.AsString : "0"));
    }

    /// <summary>Our player's look as JSON ("" before the game has one): the layers o_player composites its sprite from,
    /// its frame grid, ground and body sprites, and each layer's origins here (CharacterLook) - another game builds our
    /// sprites from it.</summary>
    public static string Look() => CharacterLook.OfPlayer()?.ToJson() ?? "";
}
