using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using StoneForge;
using StoneshardMP.Features.Join;

namespace StoneshardMP.Features.Death;

// Host: each world slot's checkpoint - the character a player comes back as when they die (DeathSync): theirs as it was
// when the host last saved, placed where the host saved. Kept in the host's save data (saved with its world), beside the
// slots' live characters (JoinSave). A slot with none yet - a player who's joined but not been through a host's save -
// gets the character they joined with.
internal static class Checkpoints
{
    private const string Key = "mpCheckpoints";
    // (Where a character is: the room, its position in it and the world-map cell, how the place is named, which way it
    // faces.)
    private static readonly string[] LocationKeys =
    {
        "checkpointLocation", "localX", "localY", "playerGridX", "playerGridY", "locationTitleKey", "isDuplicatedtLocation",
        "image_xscale",
    };

    private static string SlotKey(int slot) => slot.ToString(CultureInfo.InvariantCulture);

    /// <summary>A world slot's checkpoint (a character's JSON), or null if it has none.</summary>
    public static string? Of(int slot)
        => SaveData.Map?.GetMap(Key) is { } map && map[SlotKey(slot)] is { Kind: GmKind.String } json ? json.AsString : null;

    public static void Set(int slot, string character) => SaveData.ModMap(Key)[SlotKey(slot)] = character;

    /// <summary>A slot that hasn't one gets this character as its checkpoint (what a player joined with).</summary>
    public static void EnsureFor(int slot, string character)
    {
        if (Of(slot) == null && character.Length > 0)
            Set(slot, character);
    }

    /// <summary>The host saved: every slot's character as kept now becomes its checkpoint, placed where the host is (its
    /// own character's location, as just saved) - and the host's own (slot 0) as it is. How many.</summary>
    public static int RecordAll()
    {
        if (SaveData.Map is not { } save || SaveData.CharacterJson() is not { Length: > 0 } host
            || JsonNode.Parse(host)?["characterDataMap"] is not JsonObject hostMap)
            return 0;
        Set(0, host);
        int count = 1;
        foreach (var ((slot, _), json) in JoinSave.StoredCharacters(save))
        {
            if (slot is not { } worldSlot || worldSlot == 0 || PlacedAt(json, hostMap) is not { } placed)
                continue;
            Set(worldSlot, placed);
            count++;
        }
        return count;
    }

    // A character put where another is (its location keys copied from that one's characterDataMap).
    private static string? PlacedAt(string character, JsonObject at)
    {
        if (JsonNode.Parse(character) is not JsonObject json || json["characterDataMap"] is not JsonObject map)
            return null;
        foreach (string key in LocationKeys)
            if (at[key] is { } value)
                map[key] = value.DeepClone();
        return json.ToJsonString();
    }

    /// <summary>A character's items - in its bag and worn (the save's inventoryDataList) - as JSON; "[]" if it has none.</summary>
    public static string InventoryOf(string? character)
        => (character == null ? null : JsonNode.Parse(character)?["inventoryDataList"] as JsonArray)?.ToJsonString() ?? "[]";

    /// <summary>Where a checkpoint is, for the log.</summary>
    public static string Where(string? character) => character == null ? "none" : JoinSave.Where(character);

    /// <summary>How many items a checkpoint holds, for the log.</summary>
    public static int ItemCount(string? character)
        => (character == null ? null : JsonNode.Parse(character)?["inventoryDataList"] as JsonArray)?.Count ?? 0;
}
