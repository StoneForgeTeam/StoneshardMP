using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using StoneForge;
using static StoneshardMP.GmJson;

namespace StoneshardMP.Features.World;

// The game side of the shared world (WorldSync): world-map tiles - their areas' seeds and their dungeons - locations'
// saved state, and the weather. (Legacy: scr_mp_tile_*, scr_mp_dungeon_*, scr_mp_location_*, scr_mp_weather_*.)
internal static class SharedWorld
{
    // The per-tile seeds an area is built from (scr_globaltile_seed_validate).
    private static readonly string[] SeedKeys = { "seed", "growSeed", "mobsSeed", "presetSeed", "containersSeed", "Trade_Seed" };
    // Floors the game rejected and is building again: (cell, floor, day) -> attempts.
    private static readonly Dictionary<string, int> DungeonRetries = new();

    /// <summary>The seed our world map is made from (every location, village and dungeon); -1 with none.</summary>
    public static double WorldSeed() => Game.Global["seed"] is { Kind: GmKind.Real } seed ? seed.AsReal : -1;

    private static double GridX => Game.Global["playerGridX"].AsReal;
    private static double GridY => Game.Global["playerGridY"].AsReal;
    // (-4: the prologue, which has its own map.)
    private static bool OnWorldMap => !Game.Global["playerGridX"].IsUndefined && GridX != -4;

    // ---- seeds ----

    /// <summary>In place of scr_globaltile_seed_validate: the game's own version, except that a world-map tile's
    /// layout seeds (first visit, or a respawn) come from the world seed instead of randomize() - so every game in the
    /// same world builds the same area there. "x_y" when a seed of that tile was set (to tell the others), "" if not.</summary>
    public static string TileSeedValidate(GmValue key, GmValue tileX, GmValue tileY)
    {
        if (!OnWorldMap)
            return "";
        double x = tileX.IsUndefined ? GridX : tileX.AsReal, y = tileY.IsUndefined ? GridY : tileY.AsReal;
        string tile = Text(x) + "_" + Text(y);
        GmValue Seed() => Game.CallScript("scr_globaltile_seed_get", default, key, x, y);
        GmValue Generated() => Game.CallScript("scr_globaltile_get", default, key, x, y, -1, Game.Global["globaltile_lookup_temp"]);
        void SetSeed(GmValue value) => Game.CallScript("scr_globaltile_set", default, key, value, x, y);
        switch (key.AsString)
        {
            case "seed":
            case "growSeed":
            case "mobsSeed":
            case "presetSeed":
            {
                double seed = Seed().AsReal;
                string location = Game.CallScript("scr_locationGenerateTag", default, x, y).AsString;
                if (Game.CallScript("scr_locationExists", default, location).AsBool && seed == -1)
                {
                    SetSeed(Generated());
                    return tile;
                }
                if (seed == -1 || seed == -2)
                {
                    SetSeed(TileSeed(key.AsString, x, y, seed == -2));
                    return tile;
                }
                break;
            }
            case "containersSeed":
            case "Trade_Seed":
                if (Seed().AsReal == -1)
                {
                    SetSeed(Generated());
                    return tile;
                }
                break;
        }
        return "";
    }

    // A tile's layout seed from the world seed: the same in every game in this world. A respawn mixes in the in-game
    // day (the host keeps everyone's clock), so it gets a new layout - the same in every game that respawns it that
    // day. The random generator is randomized after, as the randomize() it replaces leaves it.
    private static double TileSeed(string key, double x, double y, bool respawn)
    {
        int kind = key switch { "growSeed" => 2, "mobsSeed" => 3, "presetSeed" => 4, _ => 1 };
        double salt = respawn ? Math.Floor(Timestamp() / 1440) + 1 : 0;
        double mix = WorldSeed() + x * 73856093 + y * 19349663 + kind * 83492791 + salt * 2654435761;
        Game.CallBuiltin("random_set_seed", Math.Abs(mix) % 2147483647);
        GmValue value = Game.CallBuiltin("irandom_range", 1, 2000000000);
        Game.CallBuiltin("randomize");
        return value.AsReal;
    }

    /// <summary>A seed for the dungeon at the player's world-map cell - kind 1: the layout of a floor, 2: which floors
    /// are special - from the world seed, so every game in this world builds the same dungeon. Mixed with the in-game day
    /// (a dungeon that resets gets a new layout, the same everywhere that day) and, for a floor the game rejected and is
    /// building again (DungeonRetry), the attempt. -1: a prologue dungeon (as vanilla).</summary>
    public static double DungeonSeed(int kind, int floor)
    {
        if (!OnWorldMap)
            return -1;
        double day = Math.Floor(Timestamp() / 1440) + 1;
        int retry = kind == 1 && DungeonRetries.TryGetValue(RetryKey(floor), out int n) ? n : 0;
        double mix = WorldSeed() + GridX * 73856093 + GridY * 19349663 + (10 + kind) * 83492791 + floor * 50331653
            + day * 2654435761 + retry * 40503;
        return Math.Abs(mix) % 2147483647;
    }

    /// <summary>The floor just built was rejected (o_dungeon_controller's user event 5): the game drops its seed and
    /// restarts the room for a new one. Ours comes from the world seed, so the attempt is counted - the next seed mixes
    /// it in - or the same floor is built, rejected and restarted forever. Every game rejects the same layout, so they
    /// count alike and still build the same floor.</summary>
    public static void DungeonRetry()
    {
        string key = RetryKey(Game.CallScript("scr_dungeonGetCurrentFloorNumber", default).AsInt);
        DungeonRetries[key] = DungeonRetries.GetValueOrDefault(key) + 1;
    }

    // Which floor build a retry count belongs to: the world-map cell, the floor and the in-game day.
    private static string RetryKey(int floor) => $"{Text(GridX)}_{Text(GridY)}_{floor}_{Math.Floor(Timestamp() / 1440)}";

    private static double Timestamp() => Game.CallScript("scr_timeGetTimestamp", default).AsReal;

    // ---- locations ----

    /// <summary>A location's saved state as the game keeps it in global.locationsRoomsDataMap - its tags, preset flags
    /// and entities (what's dead, taken, opened) - as JSON for the others (LocationStore). The tags keep their types (a
    /// room or preset tag can be a number: a ds_map key 3 isn't "3"). "" if there's none.</summary>
    public static string LocationExport(GmValue location, GmValue room, GmValue preset)
    {
        GmValue map = Game.CallScript("scr_locationRoomPresetGet", default, location, room, preset, false);
        if (!Ds.IsMap(map))
            return "";
        GmValue entities = Ds.Get(map, "entitiesDataMapString", "N/A");
        if (entities.Kind != GmKind.String || entities.AsString == "N/A")
            return "";
        return new JsonObject
        {
            ["tags"] = new JsonArray(ToJson(location), ToJson(room), ToJson(preset)),
            ["flags"] = ToJson(Ds.Get(map, "flags", 0)),
            ["entities"] = entities.AsString,
        }.ToJsonString();
    }

    /// <summary>The location an o_roomEntitySaver has just saved (the end of its user event 2), as LocationExport.</summary>
    public static string LocationExportSaved(Instance saver)
        => saver.Exists ? LocationExport(saver.Get("locationTag"), saver.Get("roomTag"), saver.Get("presetTag")) : "";

    /// <summary>Another game's saved state for a location (LocationExport) into our world data, where the game keeps its
    /// own save of that location, so our next visit there loads theirs. The flags go too: they decide whether its
    /// spawners run on entry, so a location we never visited doesn't spawn fresh mobs on top of theirs. What happened,
    /// for the log ("" for nothing to say).</summary>
    public static string LocationStore(string state)
    {
        if (!InGame.Exists(GameObjectId.o_player))
            return "";
        if (JsonNode.Parse(state) is not JsonObject s || s["tags"] is not JsonArray tags || tags.Count < 3)
            return "unreadable location";
        GmValue location = FromJson(tags[0]), room = FromJson(tags[1]), preset = FromJson(tags[2]);
        string what = $"{location} / {room} / {preset}";
        // We're standing in it: our live copy is the current one.
        if (Game.CallScript("scr_locationGenerateTag", default).AsString == location.AsString
            && Game.CallScript("scr_locationRoomGenerateTag", default).AsString == room.AsString)
            return "";
        string entities = s["entities"]?.GetValue<string>() ?? "";
        // Only a state that reads back: a broken one would make the game rebuild the location from scratch.
        GmValue check = Ds.FromJson(entities);
        if (!Ds.IsMap(check))
            return $"location {what}: its state didn't read back - kept ours";
        Ds.Destroy(check);
        GmValue map = Game.CallScript("scr_locationRoomPresetGet", default, location, room, preset, true);
        if (!Ds.IsMap(map))
            return $"location {what}: no place for it here";
        Ds.Set(map, "flags", FromJson(s["flags"]));
        Ds.Set(map, "entitiesDataMapString", entities);
        return $"location {what}: stored";
    }

    // ---- tiles ----

    /// <summary>A world-map tile as JSON for the others (TileApply): the seeds its areas are built from (-1: not set
    /// here), and its dungeon, if it has one - every value: numbers and text as they are, its maps (the saved floor
    /// graphs, which rooms dropped what, the boss's name) as {m: their JSON}, its level list as {l: [...]}. (Its
    /// contract_map is an index into the handed-out contracts, the same in every game: ContractSync.) "" outside a
    /// world.</summary>
    public static string TileExport(double x, double y)
    {
        if (!Ds.IsMap(Game.Global["globalmapLocationsDataMap"]) || !OnWorldMap)
            return "";
        GmValue lookup = Game.Global["globaltile_lookup_save"];
        var seeds = new JsonArray(SeedKeys.Select(key => ToJson(Game.CallScript("scr_globaltile_get", default, key, x, y, -1, lookup))).ToArray());
        JsonNode dungeon = -1;
        GmValue map = Game.CallScript("scr_globaltile_get", default, "dungeon", x, y, -1, lookup);
        if (Ds.IsMap(map))
        {
            var values = new JsonObject();
            foreach (GmValue key in Ds.Keys(map))
            {
                if (key.Kind != GmKind.String)
                    continue;
                GmValue value = Ds.Get(map, key);
                if (Ds.KeyIsMap(map, key))
                    values[key.AsString] = new JsonObject { ["m"] = Ds.ToJson(value) };
                else if (Ds.KeyIsList(map, key))
                    values[key.AsString] = new JsonObject
                    {
                        ["l"] = new JsonArray(Enumerable.Range(0, Ds.Count(value)).Select(i => ToJson(Ds.At(value, i))).ToArray()),
                    };
                else if (ToJson(value) is { } scalar)
                    values[key.AsString] = scalar;
            }
            dungeon = values;
        }
        return new JsonObject
        {
            ["x"] = x, ["y"] = y, ["keys"] = new JsonArray(SeedKeys.Select(k => (JsonNode)k).ToArray()),
            ["seeds"] = seeds, ["dungeon"] = dungeon,
        }.ToJsonString();
    }

    /// <summary>Another game's world-map tile (TileExport): its seeds (-1: not set there, ours kept) and its dungeon's
    /// values, so the areas and dungeon there are built and behave as theirs - a floor they've built is rebuilt from their
    /// saved graph. Not the tile we're standing on: it's built here already, and we'd take it on our next visit.</summary>
    public static void TileApply(string state)
    {
        if (!InGame.Exists(GameObjectId.o_player) || !OnWorldMap)
            return;
        if (JsonNode.Parse(state) is not JsonObject s || s["seeds"] is not JsonArray seeds || s["keys"] is not JsonArray keys)
            return;
        double x = s["x"]!.GetValue<double>(), y = s["y"]!.GetValue<double>();
        if (x == GridX && y == GridY)
            return;
        for (int i = 0; i < keys.Count && i < seeds.Count; i++)
            if (FromJson(seeds[i]) is { Kind: GmKind.Real } seed && seed.AsReal != -1)
                Game.CallScript("scr_globaltile_set", default, keys[i]!.GetValue<string>(), seed, x, y);
        if (s["dungeon"] is not JsonObject dungeon)
            return;
        foreach (var (key, value) in dungeon.ToList())
        {
            if (value is JsonObject { } nested && nested["m"] is { } m)
            {
                GmValue map = Ds.FromJson(m.GetValue<string>());
                if (Ds.IsMap(map))
                    Game.CallScript("scr_globaltile_dungeon_set_map", default, key, map, x, y);
            }
            else if (value is JsonObject { } listed && listed["l"] is JsonArray items)
            {
                GmValue list = Game.CallBuiltin("ds_list_create");
                foreach (var item in items)
                    Game.CallBuiltin("ds_list_add", list, FromJson(item));
                Game.CallScript("scr_globaltile_dungeon_set_list", default, key, list, x, y);
            }
            else
                Game.CallScript("scr_globaltile_dungeon_set", default, key, FromJson(value), x, y);
        }
    }

    // ---- the host's copy for a newcomer ----

    /// <summary>Every location we have a stored state for (its tags), and every world-map tile ("x_y" keyed): what a
    /// player coming into our world is copied.</summary>
    public static (List<(GmValue Location, GmValue Room, GmValue Preset)> Locations, List<(double X, double Y)> Tiles) CopyList()
    {
        var locations = new List<(GmValue, GmValue, GmValue)>();
        var tiles = new List<(double, double)>();
        GmValue all = Game.Global["locationsRoomsDataMap"];
        if (Ds.IsMap(all))
            foreach (GmValue location in Ds.Keys(all))
            {
                GmValue rooms = Ds.Get(all, location);
                if (!Ds.IsMap(rooms))
                    continue;
                foreach (GmValue room in Ds.Keys(rooms))
                {
                    GmValue presets = Ds.Get(rooms, room);
                    if (Ds.IsMap(presets))
                        locations.AddRange(Ds.Keys(presets).Select(preset => (location, room, preset)));
                }
            }
        GmValue map = Game.Global["globalmapLocationsDataMap"];
        if (Ds.IsMap(map))
            foreach (GmValue tag in Ds.Keys(map))
            {
                string text = tag.AsString;
                int sep = text.IndexOf('_');
                if (sep > 0 && double.TryParse(text[..sep], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                    && double.TryParse(text[(sep + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                    tiles.Add((x, y));
            }
        return (locations, tiles);
    }

    // ---- weather ----

    /// <summary>The weather as the game keeps it - global.weatherDataMap (rain, thunderstorm, duration, phase) and
    /// global.smokeDataMap (fog) - as "weatherJSON|fogJSON"; "" with no game loaded.</summary>
    public static string WeatherState()
    {
        GmValue weather = Game.Global["weatherDataMap"], smoke = Game.Global["smokeDataMap"];
        return Ds.IsMap(weather) && Ds.IsMap(smoke) ? Ds.ToJson(weather) + "|" + Ds.ToJson(smoke) : "";
    }

    /// <summary>The host's weather and fog (WeatherState), value by value into our own maps - the game's rain and fog
    /// objects read them from there.</summary>
    public static void WeatherApply(string state)
    {
        GmValue weather = Game.Global["weatherDataMap"], smoke = Game.Global["smokeDataMap"];
        int sep = state.IndexOf('|');
        if (!Ds.IsMap(weather) || !Ds.IsMap(smoke) || sep < 1)
            return;
        foreach (var (target, json) in new[] { (weather, state[..sep]), (smoke, state[(sep + 1)..]) })
        {
            GmValue source = Ds.FromJson(json);
            if (!Ds.IsMap(source))
                continue;
            foreach (GmValue key in Ds.Keys(source))
                Ds.Set(target, key, Ds.Get(source, key));
            Ds.Destroy(source);
        }
    }
}
