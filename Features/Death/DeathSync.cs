using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Text.Json.Nodes;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Join;
using StoneshardMP.Features.Loot;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Death;

// A client's death in the host's world - as the game's own death is going back to your last save, here it's going back
// to your checkpoint: your character as it was at the host's last save, where the host saved (Checkpoints, kept by the
// host), with nothing on you. The world doesn't go back - others are playing in it - so:
// - Everything you carried - in your bag and worn - drops where you die, by the game's own drop (an item's user event
//   15): yours or anyone's to go back for. Nothing comes back twice: before, only what was new since the checkpoint
//   dropped and the checkpoint's gear came back with you - gear left in a chest before dying was had twice (#25). Sent to
//   the place's other players straight away (LootSync.Flush) - and, running the place yourself, kept in its save (its
//   room entity saver), so it's there for the host when you're gone.
// - The game's death plays (the fall, the death screen), with its choices taken away for two: Respawn - back to your
//   checkpoint now, the host's world loaded in place - and Disconnect - out to the main menu, back as your checkpoint when
//   you next join.
// The host's death is the same, against its own checkpoint (kept by itself): its death screen has Respawn - the world as
// it is now reloaded in place with the host's checkpoint character in it, for the host alone: the players carry on where
// they are (JoinManager.HostRespawn) - Load (the game's save menu: a save loads for everyone) and
// Disconnect (out without saving: the world as the last save left it). Only while hosting: alone, death is the game's.
public sealed class DeathSync
{
    private readonly ModContext _context;
    private readonly Session _session;
    private readonly JoinManager _join;
    private readonly AreaOwnership _ownership;
    private readonly LootSync _loot;
    private readonly DeathScreen _screen;
    private readonly DeathCalm _calm;
    private string? _checkpointItems;
    private string _checkpointWhere = "";

    public DeathSync(ModContext context, Session session, JoinManager join, AreaOwnership ownership, LootSync loot)
    {
        _context = context;
        _session = session;
        _join = join;
        _ownership = ownership;
        _loot = loot;
        session.On<CheckpointPacket>(ReceiveCheckpoint);
        _calm = new DeathCalm(context, session, ownership);
        Player.OnDying(context, () =>
        {
            Died();
            return false;
        });
        _screen = context.UI.Always.Add(new DeathScreen(() => HostName, Choose));
        // The death screen: its own choices (Load, the log, Exit) gone - ours in their place.
        context.OnCode("gml_Object_o_dead_panel_Create_0", after: (panel, _) =>
        {
            if (!Dead)
                return;
            foreach (Instance button in Instances.All(GameObjectId.o_ingame_menu_button))
                if (Instance.Of(button.Get("guiParent")).Persist().Equals(panel.Persist()))
                    button.Destroy();
            _screen.Show(IsHost, !IsHost || Checkpoints.Of(0) != null);
        });
    }

    /// <summary>Whether we died in the host's world and haven't come back yet.</summary>
    public bool Dead { get; private set; }

    private bool IsHost => _session.Mode == Session.SessionMode.Host;

    private bool Ours => IsHost || (_session.Mode == Session.SessionMode.Client && _join.InSharedWorld);

    // The checkpoint's items: the host's own (kept here), a client's (sent by the host).
    private string? CheckpointItems => IsHost ? (Checkpoints.Of(0) is { } ours ? Checkpoints.InventoryOf(ours) : null) : _checkpointItems;

    private string HostName => _session.Players.FirstOrDefault(p => p.Slot == 0)?.Name ?? "the host";

    public void Clear()
    {
        Dead = false;
        _checkpointItems = null;
        _screen.Hide();
    }

    public void Tick()
    {
        // (Back in the world - respawned, or anything else that brought a player back.)
        if (Dead && Gm.InGame)
        {
            Dead = false;
            _screen.Hide();
        }
        if (!_session.Connected)
            _checkpointItems = null;
    }

    private void ReceiveCheckpoint(RemotePlayer from, CheckpointPacket packet)
    {
        if (from.Slot != 0)
            return;
        _checkpointItems = JoinCompression.Decompress(packet.Items);
        _checkpointWhere = packet.Where;
        _context.Log($"Checkpoint: {(JsonNode.Parse(_checkpointItems) as JsonArray)?.Count ?? 0} items, at {packet.Where}");
    }

    // Our player is dying (o_player's death event, before it runs): everything we carry - in the bag and worn - dropped
    // here, a bag with what's in it. We come back as our checkpoint with nothing on us (Checkpoints.WithoutItems), so
    // nothing comes back twice: before, only what was new since the checkpoint dropped and the checkpoint's own gear came
    // back - including what we'd left in a chest before dying.
    private void Died()
    {
        if (!Ours)
            return;
        Dead = true;
        if (CheckpointItems is null)
        {
            _context.Log("Died with no checkpoint: nothing dropped");
            return;
        }
        Instance inventory = Instances.All(GameObjectId.o_inventory).FirstOrDefault();
        var drops = new List<(Instance Item, string What)>();
        foreach (Instance item in Instances.All(GameObjectId.o_inv_slot))
        {
            if (inventory.IsNone || !Instance.Of(item.Get("owner")).Persist().Equals(inventory.Persist()) || item.Get("data").AsDsMap is not { } data)
                continue;
            string name = item.Get("object_index").AsInt == (int)GameObjectId.o_inv_slot
                ? data.ToJsonNode()["idName"]?.ToString() ?? "" : Gm.ObjectGetName(item.Get("object_index").AsInt);
            int stack = item.Get("stack") is { Kind: GmKind.Real } s && s.AsReal > 0 ? (int)s.AsReal : 0;
            drops.Add((item.Persist(), stack > 1 ? $"{name} x{stack}" : name));
        }
        foreach (var (item, _) in drops)
        {
            if (!item.Exists)
                continue;
            // (The game won't drop a worn cursed item - it's stuck on: taken off first.)
            if (item.Get("equipped").AsBool && item.Get("data").AsDsMap is { } data && data["is_cursed"].AsBool)
                item.Set("equipped", false);
            item.Set("forced_drop", true);
            Game.CallBuiltinAs("event_user", item, item, 15);
        }
        _context.Log($"Died: dropped everything we carried, {drops.Count} item(s)"
            + (drops.Count > 0 ? ": " + string.Join(", ", drops.Select(d => d.What)) : ""));
        // The hostility we leave behind ended, as a reload would: our place calmed down, the faction's crime record back
        // to the host's last save (DeathCalm) - before the place is saved, below.
        _calm.Died();
        // (To the place's other players now - and, running it ourselves, into its save, for the host.)
        _loot.Flush();
        if (_ownership.Role != AreaRole.Follower)
            foreach (Instance saver in Instances.All(GameObjectId.o_roomEntitySaver))
                Game.CallBuiltinAs("event_user", saver, saver, 2);
    }

    // The death screen's choices.
    private void Choose(DeathScreen.Choice choice)
    {
        switch (choice)
        {
            case DeathScreen.Choice.Respawn when IsHost:
                if (_join.HostRespawn())
                    _screen.Hide();
                break;
            case DeathScreen.Choice.Respawn:
                _screen.Hide();
                _context.Log($"Respawning at {_checkpointWhere}");
                _join.Respawn(rejoin: true);
                break;
            case DeathScreen.Choice.Load:
                JoinManager.OpenLoadMenu();
                break;
            case DeathScreen.Choice.Disconnect when IsHost:
                _screen.Hide();
                _join.HostDisconnect();
                break;
            case DeathScreen.Choice.Disconnect:
                _screen.Hide();
                _context.Log("Disconnecting after death");
                _join.Respawn(rejoin: false);
                break;
        }
    }
}
