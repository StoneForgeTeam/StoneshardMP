using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using StoneForge;
using static StoneshardMP.GmJson;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Players;

// Our player as the others see it - where and how it's drawn, its look and resistances - and another player's look
// built into sprites by the game's own compositor. (Legacy: scr_mp_send_state, scr_mp_send_look, scr_mp_ghost_build.)
internal static class OurPlayer
{
    private static readonly string[] Resistances = PlayerProfile.ResistanceNames;
    // (Many features ask where we are each frame: worked out once a frame.)
    private static long _stateFrame = -1;
    private static PlayerState? _state;
    private static long _frame;

    /// <summary>A new frame (the mod's Tick): what we worked out last frame is old now.</summary>
    public static void NewFrame() => _frame++;

    /// <summary>Where we are: the room, "#f&lt;floor&gt;" in a dungeon (every floor of one is the same room), and
    /// "@x_y", the world-map cell (neighbouring areas of the world map are built in the same room). Null with no game
    /// world - or no player.</summary>
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

    private static PlayerState? ReadState()
    {
        Instance player = InGame.Player;
        if (player.IsNone)
            return null;
        string place = Game.CallBuiltin("room_get_name", Gm.Room).AsString;
        if (Game.Global["floor_counter"] is { Kind: GmKind.Real } floor && floor.AsReal > 0)
            place += "#f" + Text(floor);
        if (Game.Global["playerGridX"] is { IsUndefined: false } gridX)
            place += "@" + Text(gridX) + "_" + Text(Game.Global["playerGridY"]);

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
        Instance player = InGame.Player;
        if (player.IsNone)
            return "";
        return string.Join("|", Resistances.Select(name => player.Get(name) is { IsUndefined: false } value ? Text(value) : "0"));
    }

    /// <summary>Our player's look as JSON ("" before the game has one): the layers o_player composites its sprite from
    /// (global.playerSpritePartsArray - sprite ids are the same in every game, from the same data.win) and the frame
    /// grid, ground and body sprites. Each layer: its 13 values, then its sprite's and its mask's origins as they are
    /// here - equipment origins are set per wearer at run time (scr_itemCharSpritesInit), so another game's copy of a
    /// sprite may sit differently.</summary>
    public static string Look()
    {
        using GmArray? parts = Game.Global["playerSpritePartsArray"].AsArray;
        GmValue height = Game.Global["playerSpritePartsArrayHeight"];
        if (parts == null || height.IsUndefined)
            return "";
        var rows = new JsonArray();
        for (int i = 0; i < height.AsInt; i++)
        {
            using GmArray? part = parts[i].AsArray;
            if (part == null)
                continue;
            var row = new JsonArray();
            for (int j = 0; j < 13; j++)
                row.Add(ToJson(part[j]));
            AddOrigin(row, part[0]);
            AddOrigin(row, part[11]);
            rows.Add(row);
        }
        return new JsonArray(
            ToJson(Game.Global["playerSpriteImageNumberX"]), ToJson(Game.Global["playerSpriteImageNumberY"]),
            ToJson(Game.Global["playerSpriteGround"]), ToJson(Game.Global["playerSpriteBody"]), rows).ToJsonString();
    }

    // A sprite's origin (0, 0 for none).
    private static void AddOrigin(JsonArray row, GmValue sprite)
    {
        bool exists = Game.CallBuiltin("sprite_exists", sprite).AsBool;
        row.Add(exists ? Game.CallBuiltin("sprite_get_xoffset", sprite).AsReal : 0);
        row.Add(exists ? Game.CallBuiltin("sprite_get_yoffset", sprite).AsReal : 0);
    }

    /// <summary>Another player's sprites (its 5 animation rows, "s0,s1,s2,s3,s4") built from their look (Look's JSON)
    /// by the game's own compositor: scr_playerSpriteUpdate run against their layers, our player's globals put back
    /// after. It frees what's in playerSpriteArray, so it's handed that player's previous sprites (old, -4 for none)
    /// rather than ours. Each layer's sprite and mask sit at their origins in the other game while it composites, then
    /// back as they were. Call it in a Draw event (it draws to surfaces). "" if the look can't be built.</summary>
    public static string Build(string look, int[] old)
    {
        if (JsonNode.Parse(look) is not JsonArray l || l.Count < 5 || l[4] is not JsonArray rows || rows.Count < 1)
            return "";
        double framesX = l[0]!.GetValue<double>(), framesY = l[1]!.GetValue<double>();
        GmValue body = FromJson(l[3]);
        if (framesX < 1 || framesY < 1 || !Game.CallBuiltin("sprite_exists", body).AsBool)
            return "";

        // The layers as scr_playerSpriteInit makes them: 13 values, then two flags.
        var parts = new List<GmArray>();
        foreach (var row in rows.OfType<JsonArray>())
        {
            var part = GmArray.Create(15, 0);
            for (int j = 0; j < 13; j++)
                part[j] = FromJson(row[j]);
            part[13] = false;
            part[14] = false;
            parts.Add(part);
        }
        string[] globals =
        {
            "playerSpritePartsArray", "playerSpritePartsArrayHeight", "playerSpriteImageNumberX", "playerSpriteImageNumberY",
            "playerSpriteGround", "playerSpriteBody", "playerSpriteArray", "playerSpriteUpdate", "playerSpriteSpeed",
            "playerSpriteIndex",
        };
        var saved = globals.ToDictionary(name => name, name => Game.Global[name]);
        // Their origins on, ours saved first and put back in reverse (a sprite used by several layers ends as it was).
        var origins = new List<(GmValue Sprite, double X, double Y)>();
        using var partsArray = GmArray.From(parts.Select(p => (GmValue)p));
        using var spriteArray = GmArray.From(old.Select(s => (GmValue)s));
        try
        {
            Game.Global["playerSpriteArray"] = spriteArray;
            Game.Global["playerSpritePartsArray"] = partsArray;
            Game.Global["playerSpritePartsArrayHeight"] = parts.Count;
            Game.Global["playerSpriteImageNumberX"] = framesX;
            Game.Global["playerSpriteImageNumberY"] = framesY;
            Game.Global["playerSpriteGround"] = FromJson(l[2]);
            Game.Global["playerSpriteBody"] = body;
            Game.Global["playerSpriteUpdate"] = true;
            foreach (var row in rows.OfType<JsonArray>())
            {
                SetOrigin(origins, FromJson(row[0]), row[13], row[14]);
                SetOrigin(origins, FromJson(row[11]), row[15], row[16]);
            }
            Game.CallScript("scr_playerSpriteUpdate", default);
            using GmArray? built = Game.Global["playerSpriteArray"].AsArray;
            return built == null ? "" : string.Join(",", Enumerable.Range(0, 5).Select(i => Text(built[i])));
        }
        finally
        {
            for (int i = origins.Count - 1; i >= 0; i--)
                Game.CallBuiltin("sprite_set_offset", origins[i].Sprite, origins[i].X, origins[i].Y);
            foreach (var (name, value) in saved)
                Game.Global[name] = value;
            foreach (var part in parts)
                part.Dispose();
            foreach (var value in saved.Values)
                (value.AsArray as GmRef ?? value.AsStruct)?.Dispose();
        }
    }

    // A layer's sprite at the other game's origin, ours noted first to put back.
    private static void SetOrigin(List<(GmValue, double, double)> origins, GmValue sprite, JsonNode? x, JsonNode? y)
    {
        if (!Game.CallBuiltin("sprite_exists", sprite).AsBool)
            return;
        origins.Add((sprite, Game.CallBuiltin("sprite_get_xoffset", sprite).AsReal, Game.CallBuiltin("sprite_get_yoffset", sprite).AsReal));
        Game.CallBuiltin("sprite_set_offset", sprite, x?.GetValue<double>() ?? 0, y?.GetValue<double>() ?? 0);
    }
}
