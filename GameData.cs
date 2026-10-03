using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using StoneForge;

namespace StoneshardMP;

// The game's own data from C#: its ds_maps and ds_lists (numbers, as GameMaker hands them out), and its instances by
// object. Game thread only, as any game access.
internal static class Ds
{
    // (GameMaker's ds_type_map / ds_type_list.)
    private const int MapType = 1, ListType = 2;

    public static bool IsMap(GmValue value) => value.Kind == GmKind.Real && Game.CallBuiltin("ds_exists", value, MapType).AsBool;
    public static bool IsList(GmValue value) => value.Kind == GmKind.Real && Game.CallBuiltin("ds_exists", value, ListType).AsBool;

    public static bool Has(GmValue map, GmValue key) => Game.CallBuiltin("ds_map_exists", map, key).AsBool;
    public static GmValue Get(GmValue map, GmValue key) => Game.CallBuiltin("ds_map_find_value", map, key);
    // (As the game's ds_map_find_value_ext.)
    public static GmValue Get(GmValue map, GmValue key, GmValue fallback) => Has(map, key) ? Get(map, key) : fallback;
    public static void Set(GmValue map, GmValue key, GmValue value) => Game.CallBuiltin("ds_map_set", map, key, value);
    public static void Replace(GmValue map, GmValue key, GmValue value) => Game.CallBuiltin("ds_map_replace", map, key, value);
    public static void Delete(GmValue map, GmValue key) => Game.CallBuiltin("ds_map_delete", map, key);
    public static bool KeyIsMap(GmValue map, GmValue key) => Game.CallBuiltin("ds_map_is_map", map, key).AsBool;
    public static bool KeyIsList(GmValue map, GmValue key) => Game.CallBuiltin("ds_map_is_list", map, key).AsBool;

    // A map's keys, as they are (a number stays a number).
    public static GmValue[] Keys(GmValue map)
    {
        using GmArray? keys = Game.CallBuiltin("ds_map_keys_to_array", map).AsArray;
        return keys?.ToArray() ?? System.Array.Empty<GmValue>();
    }

    public static int Count(GmValue list) => Game.CallBuiltin("ds_list_size", list).AsInt;
    public static GmValue At(GmValue list, int index) => Game.CallBuiltin("ds_list_find_value", list, index);

    // Its JSON (json_encode: nested maps and lists marked as such go in as JSON objects and arrays), and a map from one
    // (json_decode: -1 if it isn't one).
    public static string ToJson(GmValue map) => Game.CallBuiltin("json_encode", map).AsString;
    public static GmValue FromJson(string json) => Game.CallBuiltin("json_decode", json);
    public static void Destroy(GmValue map) => Game.CallBuiltin("ds_map_destroy", map);
}

internal static class InGame
{
    public static bool Exists(GameObjectId obj) => Gm.InstanceExists(obj);

    // The first instance of an object (none if there isn't one).
    public static Instance First(GameObjectId obj)
        => Gm.InstanceExists(obj) ? Game.CallBuiltin("instance_find", GmValue.From(obj), 0).AsInstance : default;

    public static Instance Player => First(GameObjectId.o_player);

    // A game asset's index by name (-1 if there's none).
    public static int Asset(string name) => Gm.AssetGetIndex(name);

    // A new game array of these values (what the game's scripts take as a list: scr_smoothRoomChange's events...).
    public static GmArray Array(params GmValue[] values) => GmArray.From(values);

    // Every instance of an object, as found now.
    public static List<Instance> All(GameObjectId obj)
    {
        int count = Gm.InstanceNumber(obj);
        var all = new List<Instance>(count);
        for (int i = 0; i < count; i++)
            if (Game.CallBuiltin("instance_find", GmValue.From(obj), i).AsInstance is { IsNone: false } found)
                all.Add(found);
        return all;
    }
}

// Game values as JSON and back - numbers, text, true/false (anything else as null) - and as GameMaker's string() writes
// a number.
internal static class GmJson
{
    public static JsonNode? ToJson(GmValue value) => value.Kind switch
    {
        GmKind.Real => JsonValue.Create(value.AsReal),
        GmKind.Bool => JsonValue.Create(value.AsBool),
        GmKind.String => JsonValue.Create(value.AsString),
        _ => null,
    };

    public static GmValue FromJson(JsonNode? node) => node?.GetValueKind() switch
    {
        JsonValueKind.Number => node.GetValue<double>(),
        JsonValueKind.String => node.GetValue<string>(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => GmValue.Undefined,
    };

    public static string Text(GmValue value) => value.Kind == GmKind.Real ? Text(value.AsReal) : value.AsString;
    public static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
}
