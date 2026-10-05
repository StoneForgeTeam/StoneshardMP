using System;
using StoneForge;
using StoneshardMP.Net;

// The game script that starts a room change - a save's load among them (the patcher makes it hookable).
[assembly: HookScript(nameof(Scripts.scr_smoothRoomChange))]

namespace StoneshardMP.Features.Join;

// Host, on the main menu: a save is picked, not gone into - Continue picks the last one played, Load Game's list the one
// clicked (its load held: the save menu shut, its loading screen gone) - and the players choose their slots
// (WorldSlots, PlayersPanel: who's who from the picked save, SlotCharacters) until the host presses Play, which loads it.
// New Game still starts straight away: a new world has no characters to choose from.
public sealed class HostLobby
{
    private readonly Session _session;
    // Play pressed: its load is let through.
    private bool _playing;

    public HostLobby(ModContext context, Session session)
    {
        _session = session;
        session.Changed += () =>
        {
            if (session.Mode != Session.SessionMode.Host)
                Pick(null);
        };
        // A save clicked in Load Game's list (its slot's user event 0 starts the load: a room change with event 2, then
        // the save to load set - if there's a changer): picked instead, while we host on the main menu.
        Scripts.scr_smoothRoomChange.Before(context, call =>
        {
            if (_playing || _session.Mode != Session.SessionMode.Host || !Gm.InMainMenu)
                return false;
            if (!IsLoad(call.Args) || call.Self.IsNone || Gm.ObjectGetName(call.Self.Get("object_index").AsInt) != "o_saveMenuSlotSave"
                || call.Self.Get("slotDirName") is not { Kind: GmKind.String } slot || call.Self.Get("saveDirName") is not { Kind: GmKind.String } save)
                return false;
            Pick(new SaveFile(new SaveSlot(slot.AsString), save.AsString));
            // (No changer: the save menu doesn't set the save to load. Its loading screen, and the menu itself, gone - back
            // to the main menu.)
            call.Result = -4;
            Game.CallBuiltin("instance_destroy", (int)GameObjectId.o_loading);
            Game.CallBuiltin("instance_destroy", (int)GameObjectId.o_saveMenu);
            return true;
        });
    }

    /// <summary>The save picked to play (null: none yet).</summary>
    public SaveFile? Picked { get; private set; }

    /// <summary>The save picked changed.</summary>
    public event Action? Changed;

    /// <summary>Continue: the last save played, picked.</summary>
    public void PickLast()
    {
        Pick(SaveSlots.CurrentSave);
    }

    /// <summary>Play: the picked save loaded (as Load Game does). False if there's none, or it couldn't be.</summary>
    public bool Play()
    {
        if (Picked is not { } save)
            return false;
        _playing = true;
        try
        {
            if (!Rooms.LoadSave(save))
                return false;
        }
        finally { _playing = false; }
        Pick(null);
        return true;
    }

    private void Pick(SaveFile? save)
    {
        if (Equals(save, Picked))
            return;
        Picked = save;
        Changed?.Invoke();
    }

    // A room change that loads a save: its events are [2].
    private static bool IsLoad(GmValue[] args)
    {
        if (args.Length < 2 || args[1].AsArray is not { } events)
            return false;
        using (events)
            return events.Length == 1 && events[0].AsReal == 2;
    }
}
