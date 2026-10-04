using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using StoneForge;

namespace StoneshardMP.Features.Join;

// The game side of joining (JoinManager): the host's save data, a client's character in it, loading the host's world
// or starting a character for it. (Legacy: scr_mp_join_*.)
internal static class JoinSave
{
    // Where the host keeps the other players' characters, in its save data (saved with its world).
    private const string PlayersKey = "mpPlayersDataMap";

    // Client: the host's save we were sent, kept to load next.
    private static DsMap? _pending;

    /// <summary>Host: whether we're in a world that can take players - a game loaded or begun, with its save data.</summary>
    public static bool HostInWorld() => Gm.InstanceExists(GameObjectId.o_player) && SaveData.Available;

    /// <summary>Client making a character for the host's world: the world map is made from the host's seed
    /// (o_globalmap_controller seeds its generation with global.seed), so it's the host's map.</summary>
    public static void ApplySeed(double seed)
    {
        Game.Global["seed"] = seed;
        if (Game.Global["gameDataMap"].AsDsMap is { } game)
            game["seed"] = seed;
    }

    /// <summary>Host: the save a player loads to join - our world (the whole save data) with their character in
    /// place of ours, as JSON; "" if we have no character for them yet. Their dialogue flags get ours added (story
    /// progress: NPCs don't repeat what's done), and the other players' characters stay here.</summary>
    public static string BuildSave(string name)
    {
        if (!HostInWorld() || SaveData.Map?.GetMap(PlayersKey) is not { } players || !players.Has(name))
            return "";
        if (JsonNode.Parse(SaveData.ToJson()!) is not JsonObject world || JsonNode.Parse(players[name].AsString) is not JsonObject character)
            return "";
        var hostDialogue = (world["characterDataMap"] as JsonObject)?["Dialogue_Complete"] as JsonObject;
        foreach (string section in SaveData.CharacterSections)
            if (character[section] is { } value)
                world[section] = value.DeepClone();
        if (hostDialogue != null && world["characterDataMap"] is JsonObject characterMap)
        {
            if (characterMap["Dialogue_Complete"] is not JsonObject dialogue)
                characterMap["Dialogue_Complete"] = dialogue = new JsonObject();
            foreach (var (key, said) in hostDialogue.ToList())
                if (!dialogue.ContainsKey(key))
                    dialogue[key] = said?.DeepClone();
        }
        world.Remove(PlayersKey);
        return world.ToJsonString(GameJson);
    }

    // JSON as the game's json_decode reads it: text as it is. System.Text.Json's default escapes characters that matter
    // in HTML (', <, >, &, +) as \uXXXX, and the game's decoder fails on some of them - a dungeon named
    // "Bernarhof's Cenotaph" made the whole world unreadable on the client.
    private static readonly System.Text.Json.JsonSerializerOptions GameJson =
        new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Client: our character - the player's sections of the save data being saved - as JSON, for the host to
    /// keep. "" without save data.</summary>
    public static string CharacterJson() => SaveData.CharacterJson() ?? "";

    /// <summary>Host: keep a player's character (the JSON of their sections) in our own save data - saved with our
    /// world. false: we're not in a world to keep it in.</summary>
    public static bool StoreCharacter(string name, string character)
    {
        if (!HostInWorld())
            return false;
        SaveData.ModMap(PlayersKey)[name] = character;
        return true;
    }

    // The character's sections the game keeps a live global of (what scr_savegame writes to), as list or map.
    private static readonly (string Section, bool List)[] LiveSections =
    {
        ("characterDataMap", false), ("characterStatsDataMap", false), ("inventoryDataList", true),
        ("scrollsDataList", true), ("locationsFogDataMap", false),
    };

    /// <summary>Client, before reading its character: each section of the save data that isn't the game's live one
    /// (global.&lt;section&gt; - what the game plays with and scr_savegame writes) is pointed at the live one, so what we
    /// send is where we are. What was relinked, for the log ("" when nothing was).</summary>
    public static string LinkLive()
    {
        if (SaveData.Map is not { } save)
            return "";
        var fixedUp = new List<string>();
        foreach (var (section, list) in LiveSections)
        {
            GmValue live = Game.Global[section];
            if (live.Kind != GmKind.Real || live.AsReal < 0
                || !Game.CallBuiltin("ds_exists", live, list ? 2 : 1).AsBool
                || save.Has(section) && save[section].AsReal == live.AsReal)
                continue;
            fixedUp.Add($"{section} ({(save.Has(section) ? save[section].AsInt.ToString() : "none")} -> {live.AsInt})");
            Game.CallBuiltin(list ? "ds_map_replace_list" : "ds_map_replace_map", save.Id, section, live);
        }
        return string.Join(", ", fixedUp);
    }

    /// <summary>Where the player really is (o_player, the room) - to set against what a character says, in the log.</summary>
    public static string PlayerWhere()
    {
        Instance player = Instances.All(GameObjectId.o_player).FirstOrDefault();
        return player.IsNone ? "no player" : $"{Rooms.CurrentName} at {player.Get("x").AsReal},{player.Get("y").AsReal}";
    }

    /// <summary>Where a character (CharacterJson) is, for the log: its room, position and world-map cell.</summary>
    public static string Where(string character)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(character);
            var map = doc.RootElement.GetProperty("characterDataMap");
            string V(string key) => map.TryGetProperty(key, out var v) ? v.ToString() : "?";
            return $"{V("checkpointLocation")} at {V("localX")},{V("localY")} (cell {V("playerGridX")}_{V("playerGridY")})";
        }
        catch (System.Exception e) when (e is System.Text.Json.JsonException or System.InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        {
            return "unreadable";
        }
    }

    /// <summary>The players whose characters we keep in our world (none outside one).</summary>
    public static string[] StoredPlayers()
        => SaveData.Map?.GetMap(PlayersKey) is { } players ? players.Keys.Select(key => key.AsString).ToArray() : System.Array.Empty<string>();

    /// <summary>Client: a calm moment to load the host's world - the main menu, or in game with nothing mid-way (no room
    /// change, fade, cutscene or conversation).</summary>
    public static bool Calm()
    {
        if (Rooms.IsChanging)
            return false;
        if (Gm.InMainMenu)
            return true;
        return Gm.InstanceExists(GameObjectId.o_player) && !Game.IsBusy;
    }

    /// <summary>Client: the save the host sent (its world with our character), kept to load next (TakePending, in place
    /// of scr_slotLoad), tagged as the host's world. false: it didn't decode.</summary>
    public static bool SetPending(string save, string host)
    {
        _pending?.Destroy();
        _pending = DsMap.FromJson(save);
        if (_pending is not { } pending)
            return false;
        pending["mpHost"] = host;
        return true;
    }

    /// <summary>Why a save didn't read as JSON (System.Text.Json's error, with where), for the log.</summary>
    public static string WhyUnreadable(string save)
    {
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(save);
            return "it's JSON, but the game didn't make a map of it";
        }
        catch (System.Text.Json.JsonException e)
        {
            return e.Message;
        }
    }

    /// <summary>A save that didn't read, kept to look at: its file.</summary>
    public static string KeepUnreadable(string save)
    {
        string folder = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "StoneShard");
        System.IO.Directory.CreateDirectory(folder);
        string file = System.IO.Path.Combine(folder, "stoneshardmp-unreadable-world.json");
        System.IO.File.WriteAllText(file, save);
        return file;
    }

    /// <summary>Client, in place of scr_slotLoad: the host's save we were sent becomes the save data being loaded.
    /// false: none pending (a save of our own loads as usual).</summary>
    public static bool TakePending()
    {
        if (_pending is not { } pending || !pending.Exists)
            return false;
        Game.Global["saveDataMap"] = pending;
        _pending = null;
        return true;
    }

    /// <summary>Client: load the host's save (SetPending) as the game's save menu loads one - a room change with event 2
    /// (load), and 14 first (leave the current game) when in game. The local slot is "N/A": a client keeps no saves of
    /// the host's world. (Rooms.LoadSave loads a save on disk; this one's only in memory.)</summary>
    public static bool StartLoad()
    {
        bool fromMenu = Gm.InMainMenu;
        if (Rooms.IsChanging)
            return false;
        if (fromMenu)
            Game.CallBuiltin("audio_play_sound", Gm.AssetGetIndex("snd_ui_menu_start_game_st"), 4, 0);
        using GmArray events = GmArray.From(fromMenu ? new GmValue[] { 2 } : new GmValue[] { 14, 2 });
        GmValue changer = Game.CallScript("scr_smoothRoomChange", default, -4, events);
        if (changer.Kind == GmKind.Real && changer.AsReal == -4)
            return false;
        Game.Global["slotLoaded"] = false;
        if (Game.Global["slotsMap"].AsDsMap is { } slots)
        {
            slots["lastCharacter"] = "N/A";
            slots["lastSave"] = "N/A";
        }
        return true;
    }

    /// <summary>Client, from the main menu: start a new character as New Game -> Adventure does (the class picked at
    /// Verren); its world is the host's (ApplySeed).</summary>
    public static void StartNew() => Rooms.StartNew();

    /// <summary>Client: back to the main menu from the host's world (the host left it), without saving: the host has our
    /// character. false: a room change is already under way.</summary>
    public static bool LeaveToMenu() => Rooms.ToMainMenu();
}
