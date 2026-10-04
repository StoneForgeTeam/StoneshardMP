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
        // Which of the 5 animation rows it's drawn from.
        int row = 0;
        GmValue sprite = player.Get("sprite_index");
        using (GmArray? rows = Game.Global["playerSpriteArray"].AsArray)
            for (int i = 0; rows != null && i < 5; i++)
                if (rows[i].AsReal == sprite.AsReal)
                {
                    row = i;
                    break;
                }
        double shadowAlpha = 0;
        if (V("isGround") == 1 && !player.Get("hasFootwave").AsBool && !player.Get("is_sleeping").AsBool
            && (player.Get("is_life").AsBool || V("image_speed") == 0))
            shadowAlpha = V("shadow_spr_alpha") * V("shadow_spr_alpha_multiplier");
        double shiftX = 0, shiftY = 0;
        using (GmArray? shift = player.Get("shadow_spr_shift").AsArray)
            if (shift != null)
            {
                shiftX = shift[0].AsReal;
                shiftY = shift[1].AsReal;
            }
        double scaleX = V("image_xscale"), scaleY = V("image_yscale"), shadowScale = V("shadow_scale");
        return new PlayerState(place, (float)V("stX"), (float)V("stY"), (float)V("stScaleX"), (float)V("stScaleY"),
            (float)V("image_index"), (byte)row, (int)V("depth"), player.Get("visible").AsBool, (float)V("image_angle"),
            (float)V("diss"), (float)V("image_alpha"), (int)V("shadow_spr"),
            (float)(V("draw_x") + shiftX * scaleX * shadowScale), (float)(V("draw_y") + (5.5 + shiftY) * shadowScale),
            (float)(scaleX * shadowScale), (float)(scaleY * shadowScale), (float)shadowAlpha,
            (short)Math.Truncate(V("xx") / 26), (short)Math.Truncate(V("yy") / 26),
            (float)V("HP"), (float)V("max_hp"), (float)V("MP"), (float)V("max_mp"));
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
