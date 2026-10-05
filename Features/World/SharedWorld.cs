using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using StoneForge;

namespace StoneshardMP.Features.World;

// The game side of the shared world (WorldSync): world-map tiles - their areas' seeds and their dungeons - locations'
// saved state, and the weather. (Legacy: scr_mp_tile_*, scr_mp_dungeon_*, scr_mp_location_*, scr_mp_weather_*.)
internal static class SharedWorld
{
    // Floors the game rejected and is building again: (cell, floor, day) -> attempts.
    private static readonly Dictionary<string, int> DungeonRetries = new();

    /// <summary>The seed our world map is made from (every location, village and dungeon); -1 with none.</summary>
    public static double WorldSeed() => Game.Global["seed"] is { Kind: GmKind.Real } seed ? seed.AsReal : -1;

    // (The day of the calendar, from 1: what a respawn and a dungeon reset mix into their seeds.)
    private static double Day => Math.Floor(Time.Timestamp / (double)GameTime.MinutesPerDay) + 1;

    // ---- seeds ----

    /// <summary>In place of scr_globaltile_seed_validate: the game's own version, except that a world-map tile's
    /// layout seeds (first visit, or a respawn) come from the world seed instead of randomize() - so every game in the
    /// same world builds the same area there. The tile, when one of its seeds was set (to tell the others); null if not.</summary>
    public static WorldTile? TileSeedValidate(GmValue key, GmValue tileX, GmValue tileY)
    {
        if (WorldMap.PlayerCell is not { } here)
            return null;
        var tile = new WorldTile(tileX.IsUndefined ? here.X : tileX.AsInt, tileY.IsUndefined ? here.Y : tileY.AsInt);
        string name = key.AsString;
        GmValue Generated() => tile.Get(name, TileLayer.Generated) is { IsUndefined: false } value ? value : -1;
        switch (name)
        {
            case "seed":
            case "growSeed":
            case "mobsSeed":
            case "presetSeed":
            {
                double seed = tile.Seeds[name];
                string location = Locations.TagAt(tile.X, tile.Y);
                if (Locations.Exists(location) && seed == -1)
                {
                    tile[name] = Generated();
                    return tile;
                }
                if (seed == -1 || seed == -2)
                {
                    tile[name] = TileSeed(name, tile, seed == -2);
                    return tile;
                }
                break;
            }
            case "containersSeed":
            case "Trade_Seed":
                if (tile.Seeds[name] == -1)
                {
                    tile[name] = Generated();
                    return tile;
                }
                break;
        }
        return null;
    }

    // A tile's layout seed from the world seed: the same in every game in this world. A respawn mixes in the in-game
    // day (the host keeps everyone's clock), so it gets a new layout - the same in every game that respawns it that
    // day.
    private static double TileSeed(string key, WorldTile tile, bool respawn)
    {
        int kind = key switch { "growSeed" => 2, "mobsSeed" => 3, "presetSeed" => 4, _ => 1 };
        double salt = respawn ? Day : 0;
        double mix = WorldSeed() + tile.X * 73856093.0 + tile.Y * 19349663.0 + kind * 83492791.0 + salt * 2654435761;
        return Game.WithSeed((long)(Math.Abs(mix) % 2147483647), () => (double)Gm.IrandomRange(1, 2000000000));
    }

    /// <summary>A seed for the dungeon at the player's world-map cell - kind 1: the layout of a floor, 2: which floors
    /// are special - from the world seed, so every game in this world builds the same dungeon. Mixed with the in-game day
    /// (a dungeon that resets gets a new layout, the same everywhere that day) and, for a floor the game rejected and is
    /// building again (DungeonRetry), the attempt. -1: a prologue dungeon (as vanilla).</summary>
    public static long DungeonSeed(int kind, int floor)
    {
        if (WorldMap.PlayerCell is not var (x, y))
            return -1;
        int retry = kind == 1 && DungeonRetries.TryGetValue(RetryKey(floor), out int n) ? n : 0;
        double mix = WorldSeed() + x * 73856093.0 + y * 19349663.0 + (10 + kind) * 83492791.0 + floor * 50331653.0
            + Day * 2654435761 + retry * 40503.0;
        return (long)(Math.Abs(mix) % 2147483647);
    }

    /// <summary>The floor just built was rejected (o_dungeon_controller's user event 5): the game drops its seed and
    /// restarts the room for a new one. Ours comes from the world seed, so the attempt is counted - the next seed mixes
    /// it in - or the same floor is built, rejected and restarted forever. Every game rejects the same layout, so they
    /// count alike and still build the same floor.</summary>
    public static void DungeonRetry()
    {
        string key = RetryKey(WorldMap.DungeonFloor);
        DungeonRetries[key] = DungeonRetries.GetValueOrDefault(key) + 1;
    }

    // Which floor build a retry count belongs to: the world-map cell, the floor and the in-game day.
    private static string RetryKey(int floor) => $"{WorldMap.PlayerCell}_{floor}_{Day}";

    // ---- locations ----

    /// <summary>A location's saved state as the game keeps it - its tags, preset flags and entities (what's dead, taken,
    /// opened) - as JSON for the others (LocationStore). "" if there's none.</summary>
    public static string LocationExport(string location, GmValue room, GmValue preset)
        => Locations.Get(location)?.Room(room)?.Preset(preset)?.Export() is { EntitiesJson: not null } state ? state.ToJson() : "";

    /// <summary>The location an o_roomEntitySaver has just saved (the end of its user event 2), as LocationExport.</summary>
    public static string LocationExportSaved(Instance saver)
        => saver.Exists ? LocationExport(saver.Get("locationTag").AsString, saver.Get("roomTag"), saver.Get("presetTag")) : "";

    /// <summary>Another game's saved state for a location (LocationExport) into our world data, where the game keeps its
    /// own save of that location, so our next visit there loads theirs. The flags go too: they decide whether its
    /// spawners run on entry, so a location we never visited doesn't spawn fresh mobs on top of theirs. What happened,
    /// for the log ("" for nothing to say).</summary>
    public static string LocationStore(string json)
    {
        if (!Gm.InstanceExists(GameObjectId.o_player) || !Locations.Available)
            return "";
        if (LocationState.FromJson(json) is not { } state)
            return "unreadable location";
        string what = $"{state.Location} / {state.Room} / {state.Preset}";
        // We're standing in it: our live copy is the current one.
        if (Locations.Here is var (location, room) && location == state.Location && room.AsString == state.Room.AsString)
            return "";
        return Locations.Store(state) ? $"location {what}: stored" : $"location {what}: its state didn't read back - kept ours";
    }

    // ---- tiles ----

    /// <summary>A world-map tile as JSON for the others (TileApply): the seeds its areas are built from (-1: not set
    /// here), and its dungeon, if it has one - every value: numbers and text as they are, its maps (the saved floor
    /// graphs, which rooms dropped what, the boss's name) as {m: their JSON}, its lists as {l: their JSON}. (Its
    /// contract_map is an index into the handed-out contracts, the same in every game: ContractSync.) "" outside a
    /// world.</summary>
    public static string TileExport(WorldTile tile)
    {
        if (!WorldMap.Available || tile.X < 0 || tile.Y < 0 || tile.X >= WorldMap.Width || tile.Y >= WorldMap.Height)
            return "";
        var seeds = new JsonArray(TileSeeds.Names.Select(name => (JsonNode)tile.Seeds[name]).ToArray());
        JsonNode dungeon = -1;
        if (tile.Dungeon is { } found)
        {
            var values = new JsonObject();
            foreach (GmValue key in found.Keys)
            {
                if (key.Kind != GmKind.String)
                    continue;
                if (found.GetMap(key.AsString) is { } map)
                    values[key.AsString] = new JsonObject { ["m"] = map.ToJson() };
                else if (found.GetList(key.AsString) is { } list)
                    values[key.AsString] = new JsonObject { ["l"] = list.ToJson() };
                else if (found[key.AsString].ToJsonNode() is { } scalar)
                    values[key.AsString] = scalar;
            }
            dungeon = values;
        }
        return new JsonObject
        {
            ["x"] = tile.X, ["y"] = tile.Y, ["keys"] = new JsonArray(TileSeeds.Names.Select(k => (JsonNode)k).ToArray()),
            ["seeds"] = seeds, ["dungeon"] = dungeon,
        }.ToJsonString();
    }

    /// <summary>Another game's world-map tile (TileExport): its seeds (-1: not set there, ours kept) and its dungeon's
    /// values, so the areas and dungeon there are built and behave as theirs - a floor they've built is rebuilt from their
    /// saved graph. Not the tile we're standing on: it's built here already, and we'd take it on our next visit.</summary>
    public static void TileApply(string json)
    {
        if (!Gm.InstanceExists(GameObjectId.o_player) || WorldMap.PlayerCell is not { } here)
            return;
        if (JsonNode.Parse(json) is not JsonObject s || s["seeds"] is not JsonArray seeds || s["keys"] is not JsonArray keys)
            return;
        var tile = new WorldTile(s["x"]!.GetValue<int>(), s["y"]!.GetValue<int>());
        if (tile == here || tile.X < 0 || tile.Y < 0 || tile.X >= WorldMap.Width || tile.Y >= WorldMap.Height)
            return;
        for (int i = 0; i < keys.Count && i < seeds.Count; i++)
            if (GmValue.FromJsonNode(seeds[i]) is { Kind: GmKind.Real } seed && seed.AsReal != -1)
                tile[keys[i]!.GetValue<string>()] = seed;
        if (s["dungeon"] is not JsonObject dungeon)
            return;
        // (Through the game's own setters - they make the dungeon if the tile has none yet.)
        foreach (var (key, value) in dungeon.ToList())
        {
            if (value is JsonObject { } nested && nested["m"] is { } m)
            {
                if (DsMap.FromJson(m.GetValue<string>()) is { } map)
                    tile.SetDungeonMap(key, map);
            }
            else if (value is JsonObject { } listed && listed["l"] is { } l)
            {
                if (DsList.FromJson(l.GetValue<string>()) is { } list)
                    tile.SetDungeonList(key, list);
            }
            else
                tile.SetDungeonValue(key, GmValue.FromJsonNode(value));
        }
    }

    // ---- the host's copy for a newcomer ----

    /// <summary>Every location we have a stored state for (its tags), and every world-map tile the save keeps: what a
    /// player coming into our world is copied.</summary>
    public static (List<(string Location, GmValue Room, GmValue Preset)> Locations, List<WorldTile> Tiles) CopyList()
    {
        var locations = new List<(string, GmValue, GmValue)>();
        foreach (string tag in StoneForge.Locations.Tags)
            if (StoneForge.Locations.Get(tag) is { } location)
                foreach (GmValue room in location.Rooms)
                    if (location.Room(room) is { } r)
                        locations.AddRange(r.Presets.Select(preset => (tag, room, preset)));
        var tiles = new List<WorldTile>();
        if (Game.Global["globalmapLocationsDataMap"].AsDsMap is { } map)
            foreach (GmValue tag in map.Keys)
            {
                string text = tag.AsString;
                int sep = text.IndexOf('_');
                if (sep > 0 && int.TryParse(text[..sep], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                    && int.TryParse(text[(sep + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
                    tiles.Add(new WorldTile(x, y));
            }
        return (locations, tiles);
    }

    // ---- weather ----

    // The weather as the game reads it - global.weatherDataMap (rain, thunderstorm, duration, phase) and
    // global.smokeDataMap (fog): the save data's sections, or maps of their own when the save had none.
    private static DsMap? Weather => Game.Global["weatherDataMap"].AsDsMap;
    private static DsMap? Smoke => Game.Global["smokeDataMap"].AsDsMap;

    /// <summary>The weather and fog as "weatherJSON|fogJSON"; "" with no game loaded.</summary>
    public static string WeatherState()
        => Weather is { } weather && Smoke is { } smoke ? weather.ToJson() + "|" + smoke.ToJson() : "";

    /// <summary>The host's weather and fog (WeatherState), value by value into our own maps - the game's rain and fog
    /// objects read them from there.</summary>
    public static void WeatherApply(string state)
    {
        int sep = state.IndexOf('|');
        if (Weather is not { } weather || Smoke is not { } smoke || sep < 1)
            return;
        foreach (var (target, json) in new[] { (weather, state[..sep]), (smoke, state[(sep + 1)..]) })
        {
            if (DsMap.FromJson(json) is not { } source)
                continue;
            foreach (GmValue key in source.Keys)
                target[key] = source[key];
            source.Destroy();
        }
    }
}
