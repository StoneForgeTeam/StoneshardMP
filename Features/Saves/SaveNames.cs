using System;
using StoneForge;
using StoneshardMP.Net;

// The game script saving names into (the patcher makes it hookable).
[assembly: HookScript(nameof(Scripts.scr_slotMapSave))]

namespace StoneshardMP.Features.Saves;

// A multiplayer world's saves are named for who plays in it, not for the host's character: the host's saves write
// the players' names into the character folder's info (MpSavePlayers), and the save menu shows them as that folder's
// header (MpSaveSlotTitle).
public sealed class SaveNames
{
    public SaveNames(ModContext context, Session session, Func<string> playerName)
    {
        // A save's character folder info (scr_slotUpdate: character.map) - ours, or a client's (never written).
        Scripts.scr_slotMapSave.Before(context, call =>
        {
            if (call.Args.Length >= 2 && session.Mode != Session.SessionMode.Client)
                Gml.MpSavePlayers(call.Args[1], playerName(), session.Mode == Session.SessionMode.Host);
            return false;
        });
        context.OnCode("gml_Object_o_saveMenuSlotHeader_Other_25", after: (self, _) => Gml.MpSaveSlotTitle(self));
    }
}
