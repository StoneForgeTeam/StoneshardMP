using System;
using System.Linq;
using StoneForge;
using StoneshardMP.Net;

// The game script saving names into (the patcher makes it hookable).
[assembly: HookScript(nameof(Scripts.scr_slotMapSave))]

namespace StoneshardMP.Features.Saves;

// A multiplayer world's saves are named for who plays in it, not for the host's character: the host's saves write
// the players' names into the character folder's info (SavePlayers), and the save menu shows them as that folder's
// header (SlotTitle). (Legacy: scr_mp_slot_players_set, scr_mp_slot_title.)
public sealed class SaveNames
{
    public SaveNames(ModContext context, Session session, Func<string> playerName)
    {
        // A save's character folder info (scr_slotUpdate: character.map) - ours, or a client's (never written).
        Scripts.scr_slotMapSave.Before(context, call =>
        {
            if (call.Args.Length >= 2 && session.Mode != Session.SessionMode.Client)
                SavePlayers(call.Args[1], playerName(), session.Mode == Session.SessionMode.Host);
            return false;
        });
        context.OnCode("gml_Object_o_saveMenuSlotHeader_Other_25", after: (self, _) => SlotTitle(self));
    }

    // Host: the character folder's info being saved (scr_slotMapSave's map, its character.map) gets who plays in this
    // world - us, then every player whose character we keep (mpPlayersDataMap), in name order - as "A, B, C" under
    // "mpPlayers". A world becomes a multiplayer one (gameDataMap's "mpWorld", saved with it) the first time it's saved
    // while hosting, and stays one.
    private static void SavePlayers(GmValue slotMap, string hostName, bool hosting)
    {
        GmValue game = Game.Global["gameDataMap"], save = Game.Global["saveDataMap"];
        if (!InGame.Exists(GameObjectId.o_player) || !Ds.IsMap(save) || !Ds.IsMap(game) || !Ds.IsMap(slotMap))
            return;
        if (hosting)
            Ds.Set(game, "mpWorld", true);
        if (!Ds.Get(game, "mpWorld", false).AsBool)
            return;
        GmValue players = Ds.Get(save, "mpPlayersDataMap");
        var names = new[] { hostName }.Concat((Ds.IsMap(players) ? Ds.Keys(players) : Array.Empty<GmValue>())
                .Select(key => key.AsString).Where(name => name.Length > 0 && name != hostName).OrderBy(name => name, StringComparer.Ordinal))
            .Where(name => name.Length > 0);
        string text = string.Join(", ", names);
        if (text.Length > 0)
            Ds.Set(slotMap, "mpPlayers", text);
    }

    // A character folder's header in the save menu (o_saveMenuSlotHeader, after its user event 15 set the title): a
    // multiplayer world shows who plays in it instead of the character's name, numbered and styled as the game does its
    // own ("FailMelon, Friend (1)"). Any other folder keeps the game's title.
    private static void SlotTitle(Instance header)
    {
        if (!header.Exists)
            return;
        GmValue folder = header.Get("slotDirName");
        GmValue slot = Game.CallScript("scr_slotMapLoad", header, folder);
        GmValue players = Ds.Get(slot, "mpPlayers", "");
        Ds.Destroy(slot);
        if (players.Kind != GmKind.String || players.AsString.Length == 0)
            return;
        string space = Game.CallScript("scr_actionsLogGetSpace", header).AsString;
        string number = Game.CallBuiltin("string_digits", Game.CallBuiltin("string_replace_all", folder, "_", space)).AsString;
        header["title"] = Game.CallScript("scr_stringTransform", header, players.AsString + space
            + Game.CallScript("scr_actionsLogGetSymbol", header, "openRoundBracket").AsString + number
            + Game.CallScript("scr_actionsLogGetSymbol", header, "closeRoundBracket").AsString, true);
    }
}
