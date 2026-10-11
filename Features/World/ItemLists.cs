using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using StoneForge;

// A container's items loaded into its window (the patcher makes it hookable).
[assembly: HookScript(nameof(Scripts.scr_loadContainerContent))]

namespace StoneshardMP.Features.World;

// The items a saved container or corpse holds, made fit to go to another game and back.
// The game saves a container's items (its loot_list - a fire's cooking pot too; a corpse's loot_list_meat and
// loot_list_add) by copying the list of them, and each item is a list of its own held there by its number (not nested:
// scr_save_item_single's list, never destroyed). In one game those numbers are its live item lists; written to JSON they
// stay numbers - in another game, nothing, or some other list. A location's state or a corpse sent as it was put
// numbers into the other game that its cooking window then read as items: "object_get_name argument 1 incorrect type
// (undefined)" opening the pot of a fire another player had used, and the game crashed.
// - Portable (to send): each number becomes that item's own list, as JSON - one gone already is left out.
// - Local (as it comes): each item made again as a list of this game's, on its own as the game's own are, and its
//   number put in its place. A bare number (another game's) is never one of ours: left out.
// - And as a container's items are loaded into its window (scr_loadContainerContent), any that isn't an item of this
//   game's is taken out first: what was saved with another game's numbers before this - a host's world keeps the
//   places others last had - loses those, rather than the game crashing on them.
internal static class ItemLists
{
    private static readonly string[] Keys = { "loot_list", "loot_list_meat", "loot_list_add" };

    // (JSON as the game's json_decode reads it: its text as it is. .NET's own escapes what matters in HTML - ' < > & + -
    // as backslash-u codes, and the game gives up on some: a place's entities written so didn't load, and the game
    // crashed entering it - "Data structure with index does not exist" in o_roomEntityLoader.)
    private static readonly JsonSerializerOptions ForGame = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Hooks the game's loading of a container's items, to take out any that isn't one of ours.</summary>
    public static void GuardLoading(ModContext context)
        => Scripts.scr_loadContainerContent.Before(context, call =>
        {
            if (call.Args.Length > 0)
                CleanList(call.Args[0]);
            return false;
        });

    /// <summary>A container's or corpse's live list of items (its loot_list...) with what isn't one of this game's items
    /// taken out; how many were.</summary>
    public static int CleanList(GmValue list)
    {
        if (DsList.From(list) is not { Exists: true } items)
            return 0;
        int removed = 0;
        for (int i = items.Count - 1; i >= 0; i--)
            if (!IsItem(items[i]))
            {
                items.RemoveAt(i);
                removed++;
            }
        return removed;
    }

    /// <summary>A saved state's JSON (a location's entities, a corpse) with the numbers in its item lists that aren't one
    /// of this game's items taken out; how many were (0: the JSON as it was).</summary>
    public static (string Json, int Removed) Clean(string json)
    {
        if (!Keys.Any(key => json.Contains('"' + key + '"')) || JsonNode.Parse(json) is not { } root)
            return (json, 0);
        int removed = Clean(root);
        return removed > 0 ? (root.ToJsonString(ForGame), removed) : (json, 0);
    }

    /// <summary>Saved JSON the game may not read - written by .NET's escaping (backslash-u codes) - written again as the
    /// game reads it; whether it changed. (What 0.4.1 to 0.4.4 wrote into some places' saved state, and their saves.)</summary>
    public static (string Json, bool Changed) Readable(string json)
    {
        if (!json.Contains(@"\u") || JsonNode.Parse(json) is not { } root)
            return (json, false);
        string written = root.ToJsonString(ForGame);
        return (written, written != json);
    }

    private static int Clean(JsonNode? node)
    {
        int removed = 0;
        if (node is JsonObject map)
        {
            foreach (var (key, value) in map.ToList())
            {
                if (Keys.Contains(key) && value is JsonArray items)
                {
                    for (int i = items.Count - 1; i >= 0; i--)
                        if (items[i] is JsonValue number && number.TryGetValue(out double id) && !IsItem(id))
                        {
                            items.RemoveAt(i);
                            removed++;
                        }
                }
                else
                    removed += Clean(value);
            }
        }
        else if (node is JsonArray array)
            foreach (JsonNode? item in array)
                removed += Clean(item);
        return removed;
    }

    // (An item as the game keeps one: a list of its own, first its object's or weapon's name - or its object's number,
    // which the loader takes too.)
    private static bool IsItem(GmValue entry)
    {
        if (DsList.From(entry) is not { Exists: true } item || item.Count < 2)
            return false;
        GmValue what = item[0];
        return what.Kind == GmKind.String
            || what.Kind == GmKind.Real && what.AsReal >= 0 && Game.CallBuiltin("object_exists", what).AsBool;
    }

    public static string Portable(string json) => Walk(json, portable: true);

    public static string Local(string json) => Walk(json, portable: false);

    private static string Walk(string json, bool portable)
    {
        if (!Keys.Any(key => json.Contains('"' + key + '"')) || JsonNode.Parse(json) is not { } root)
            return json;
        Visit(root, portable);
        return root.ToJsonString(ForGame);
    }

    private static void Visit(JsonNode? node, bool portable)
    {
        if (node is JsonObject map)
        {
            foreach (var (key, value) in map.ToList())
            {
                if (Keys.Contains(key) && value is JsonArray items)
                    map[key] = portable ? ToPortable(items) : ToLocal(items);
                else
                    Visit(value, portable);
            }
        }
        else if (node is JsonArray array)
            foreach (JsonNode? item in array)
                Visit(item, portable);
    }

    private static JsonArray ToPortable(JsonArray ids)
    {
        var items = new JsonArray();
        foreach (JsonNode? entry in ids)
        {
            if (entry is JsonArray already)
                items.Add(already.DeepClone());
            else if (entry is JsonValue number && number.TryGetValue(out double id)
                && DsList.From(id) is { Exists: true } item)
                items.Add(item.ToJsonNode());
        }
        return items;
    }

    private static JsonArray ToLocal(JsonArray items)
    {
        var ids = new JsonArray();
        foreach (JsonNode? entry in items)
            if (entry is JsonArray item && DsList.FromJson(item.ToJsonString()) is { } made)
                ids.Add(made.Id);
        return ids;
    }
}
