using System;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Join;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Saves;

// A multiplayer world's saves are named for who plays in it, not for the host's character: the host's saves write
// the players' names into the character folder's info (SavePlayers), and the save menu shows them as that folder's
// header. (Legacy: scr_mp_slot_players_set, scr_mp_slot_title.)
public sealed class SaveNames
{
    // (Who plays in a world, in its folder's info.)
    private const string PlayersKey = "mpPlayers";

    public SaveNames(ModContext context, Session session, Func<string> playerName)
    {
        // A save's character folder info - ours, or a client's (never written).
        SaveSlots.OnInfoSaving(context, (_, info) =>
        {
            if (session.Mode != Session.SessionMode.Client)
                SavePlayers(info, playerName(), session.Mode == Session.SessionMode.Host);
        });
        // A multiplayer world shows who plays in it instead of the character's name; any other folder keeps the game's.
        SaveSlots.SetTitle(context, slot => slot.Info?[PlayersKey] is { Kind: GmKind.String } players ? players.AsString : null);
    }

    // Host: the character folder's info being saved gets who plays in this world - us, then every player whose
    // character we keep, in name order - as "A, B, C". A world becomes a multiplayer one (gameDataMap's "mpWorld",
    // saved with it) the first time it's saved while hosting, and stays one.
    private static void SavePlayers(DsMap info, string hostName, bool hosting)
    {
        if (!Gm.InstanceExists(GameObjectId.o_player) || SaveData.Section("gameDataMap") is not { } game)
            return;
        if (hosting)
            game["mpWorld"] = true;
        if (!game.Get("mpWorld", false).AsBool)
            return;
        var names = new[] { hostName }.Concat(JoinSave.StoredPlayers()
                .Where(name => name.Length > 0 && name != hostName).OrderBy(name => name, StringComparer.Ordinal))
            .Where(name => name.Length > 0);
        string text = string.Join(", ", names);
        if (text.Length > 0)
            info[PlayersKey] = text;
    }
}
