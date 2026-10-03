using System.Linq;
using System.Text.Json.Nodes;
using StoneForge;

namespace StoneshardMP.Features.Join;

// The game side of joining (JoinManager): the host's save data, a client's character in it, loading the host's world
// or starting a character for it. (Legacy: scr_mp_join_*.)
internal static class JoinSave
{
    // The player's sections of the save data: what a client's character is. The rest - the world, its journal - is
    // the host's.
    private static readonly string[] CharacterSections =
    {
        "characterDataMap", "characterStatsDataMap", "skillsDataMap", "inventoryDataList", "scrollsDataList", "locationsFogDataMap",
    };

    // Client: the host's save we were sent, kept to load next (a ds_map; -1: none).
    private static GmValue _pending = -1;

    private static GmValue SaveData => Game.Global["saveDataMap"];

    /// <summary>Host: whether we're in a world that can take players - a game loaded or begun, with its save data.</summary>
    public static bool HostInWorld() => InGame.Exists(GameObjectId.o_player) && Ds.IsMap(SaveData);

    /// <summary>Client making a character for the host's world: the world map is made from the host's seed
    /// (o_globalmap_controller seeds its generation with global.seed), so it's the host's map.</summary>
    public static void ApplySeed(double seed)
    {
        Game.Global["seed"] = seed;
        if (Game.Global["gameDataMap"] is var game && Ds.IsMap(game))
            Ds.Set(game, "seed", seed);
    }

    /// <summary>Host: the save a player loads to join - our world (the whole save data) with their character in
    /// place of ours, as JSON; "" if we have no character for them yet. Their dialogue flags get ours added (story
    /// progress: NPCs don't repeat what's done), and the other players' characters stay here.</summary>
    public static string BuildSave(string name)
    {
        if (!HostInWorld())
            return "";
        GmValue players = Ds.Get(SaveData, "mpPlayersDataMap");
        if (!Ds.IsMap(players) || !Ds.Has(players, name))
            return "";
        if (JsonNode.Parse(Ds.ToJson(SaveData)) is not JsonObject world
            || JsonNode.Parse(Ds.Get(players, name).AsString) is not JsonObject character)
            return "";
        var hostDialogue = (world["characterDataMap"] as JsonObject)?["Dialogue_Complete"] as JsonObject;
        foreach (string section in CharacterSections)
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
        world.Remove("mpPlayersDataMap");
        return world.ToJsonString();
    }

    /// <summary>Client: our character - the player's sections of the save data being saved - as JSON, for the host to
    /// keep. "" without save data.</summary>
    public static string CharacterJson()
    {
        if (!Ds.IsMap(SaveData) || JsonNode.Parse(Ds.ToJson(SaveData)) is not JsonObject full)
            return "";
        var character = new JsonObject();
        foreach (string section in CharacterSections)
            if (full[section] is { } value)
                character[section] = value.DeepClone();
        return character.ToJsonString();
    }

    /// <summary>Host: keep a player's character (the JSON of their sections) in our own save data - saved with our
    /// world, as mpPlayersDataMap. false: we're not in a world to keep it in.</summary>
    public static bool StoreCharacter(string name, string character)
    {
        if (!HostInWorld())
            return false;
        GmValue players = Ds.Get(SaveData, "mpPlayersDataMap");
        if (!Ds.IsMap(players))
        {
            players = Game.CallBuiltin("ds_map_create");
            Game.CallBuiltin("ds_map_add_map", SaveData, "mpPlayersDataMap", players);
        }
        Ds.Set(players, name, character);
        return true;
    }

    /// <summary>Client: a calm moment to load the host's world - the main menu, or in game with nothing mid-way (no room
    /// change, fade, cutscene or conversation).</summary>
    public static bool Calm()
    {
        if (InGame.Exists(GameObjectId.o_smoothRoomChanger))
            return false;
        if (InMainMenuRoom)
            return true;
        if (!InGame.Exists(GameObjectId.o_player) || InGame.Exists(GameObjectId.o_black_overlay) || InGame.Exists(GameObjectId.o_dialogue))
            return false;
        // (As the player: it reads object_index.)
        return !Game.CallScript("scr_is_cutscene", InGame.Player).AsBool;
    }

    /// <summary>Client: the save the host sent (its world with our character), kept to load next (TakePending, in place
    /// of scr_slotLoad), tagged as the host's world. false: it didn't decode.</summary>
    public static bool SetPending(string save, string host)
    {
        if (Ds.IsMap(_pending))
            Ds.Destroy(_pending);
        _pending = Ds.FromJson(save);
        if (!Ds.IsMap(_pending))
            return false;
        Ds.Set(_pending, "mpHost", host);
        return true;
    }

    /// <summary>Client, in place of scr_slotLoad: the host's save we were sent becomes the save data being loaded.
    /// false: none pending (a save of our own loads as usual).</summary>
    public static bool TakePending()
    {
        if (!Ds.IsMap(_pending))
            return false;
        Game.Global["saveDataMap"] = _pending;
        _pending = -1;
        return true;
    }

    /// <summary>Client: load the host's save (SetPending) as the game's save menu loads one - a room change with event 2
    /// (load), and 14 first (leave the current game) when in game. The local slot is "N/A": a client keeps no saves of
    /// the host's world.</summary>
    public static bool StartLoad()
    {
        PlayStartSound();
        if (!RoomChange(InMainMenuRoom ? new GmValue[] { 2 } : new GmValue[] { 14, 2 }))
            return false;
        Game.Global["slotLoaded"] = false;
        Ds.Set(Game.Global["slotsMap"], "lastCharacter", "N/A");
        Ds.Set(Game.Global["slotsMap"], "lastSave", "N/A");
        return true;
    }

    /// <summary>Client, from the main menu: start a new character as New Game -> Adventure does (the class picked at
    /// Verren); its world is the host's (ApplySeed).</summary>
    public static void StartNew()
    {
        Game.Global["permadeathMode"] = false;
        Game.Global["globalMapInit"] = false;
        PlayStartSound();
        RoomChange(1);
        // (The loading screen, as the game's Adventure button shows it: its picture and its tip.)
        Game.CallScript("scr_loadingCreate", default, 4019, Ds.At(Game.Global["other_hover"], 22));
    }

    /// <summary>Client: back to the main menu from the host's world (the host left it) - leave the game (room changer
    /// event 14) and go to the menu (3), without saving: the host has our character. false: a room change is already
    /// under way.</summary>
    public static bool LeaveToMenu() => InMainMenuRoom || RoomChange(14, 3);

    private static bool InMainMenuRoom => Gm.Room == Game.Global["mainMenuRoom"].AsInt;

    private static void PlayStartSound()
        => Game.CallBuiltin("audio_play_sound", InGame.Asset("snd_ui_menu_start_game_st"), 4, 0);

    // The game's room change, with its events in order; false if one is already under way (-4).
    private static bool RoomChange(params GmValue[] events)
    {
        using GmArray list = InGame.Array(events);
        GmValue changer = Game.CallScript("scr_smoothRoomChange", default, -4, list);
        return !(changer.Kind == GmKind.Real && changer.AsReal == -4);
    }
}
