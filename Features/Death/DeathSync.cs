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
// host; its items sent to us as CheckpointPacket). The world doesn't go back - others are playing in it - so:
// - What you've picked up since - in your bag or worn - drops where you die: every item you have that your checkpoint
//   hasn't (matched by what it is, not its wear or charges), by the game's own drop (an item's user event 15). It's on the
//   ground there for anyone: quest items, keys and finds stay in the world, and nothing you still had is doubled. Sent to
//   the place's other players straight away (LootSync.Flush) - and, running the place yourself, kept in its save (its
//   room entity saver), so it's there for the host when you're gone.
// - The game's death plays (the fall, the death screen), with its choices taken away for two: Respawn - back to your
//   checkpoint now, the host's world loaded in place - and Disconnect - out to the main menu, back as your checkpoint when
//   you next join.
// The host's death is the same, against its own checkpoint (kept by itself): its death screen has Respawn - the world as
// it is now reloaded in place with the host's checkpoint character in it, everyone's characters asked for first so they
// come back as they are (JoinManager.HostRespawn) - Load (the game's save menu: a save loads for everyone) and
// Disconnect (out without saving: the world as the last save left it). Only while hosting: alone, death is the game's.
public sealed class DeathSync
{
    // (What an item is - not its wear, charges, or whether it's been looked at.)
    private static readonly HashSet<string> Changing = new()
    {
        "Duration", "charge", "Effects_Duration", "identified", "i_index", "is_execute", "is_fire",
        // (A bag's: what's in it, and how full - its contents are counted one by one.)
        "lootList", "Stack",
    };

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly JoinManager _join;
    private readonly AreaOwnership _ownership;
    private readonly LootSync _loot;
    private readonly DeathScreen _screen;
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

    // Our player is dying (o_player's death event, before it runs): what we've picked up since the checkpoint dropped here.
    // Each item in the checkpoint - in the bag, worn, or inside a bag (its lootList: a moneybag's gold, a backpack's
    // contents) - is kept once, by amount (a stack's count: arrows, coins): whatever we have beyond that drops. A stack
    // drops only what's more than the checkpoint had; a bag drops with only what's new in it (a bag we had at the
    // checkpoint drops too, then, as the box for it). What stays behind doesn't matter: the checkpoint replaces it.
    private void Died()
    {
        if (!Ours)
            return;
        Dead = true;
        if (CheckpointItems is not { } checkpointItems)
        {
            _context.Log("Died with no checkpoint: nothing dropped");
            return;
        }
        var quota = new Dictionary<string, int>();
        foreach (JsonNode? entry in JsonNode.Parse(checkpointItems) as JsonArray ?? new JsonArray())
            CountCheckpoint(entry, quota);
        Instance inventory = Instances.All(GameObjectId.o_inventory).FirstOrDefault();
        var drops = new List<(Instance Item, string What)>();
        foreach (Instance item in Instances.All(GameObjectId.o_inv_slot))
        {
            if (inventory.IsNone || !Instance.Of(item.Get("owner")).Persist().Equals(inventory.Persist()) || item.Get("data").AsDsMap is not { } data)
                continue;
            JsonObject json = data.ToJsonNode();
            string name = item.Get("object_index").AsInt == (int)GameObjectId.o_inv_slot
                ? json["idName"]?.ToString() ?? "" : Gm.ObjectGetName(item.Get("object_index").AsInt);
            int stack = item.Get("stack") is { Kind: GmKind.Real } s && s.AsReal > 0 ? (int)s.AsReal : 0;
            int surplus = Math.Max(stack, 1) - Take(quota, Identity(name, json), Math.Max(stack, 1));
            bool newInside = data["lootList"].AsDsList is { } contents && Trim(contents, quota);
            if (surplus <= 0 && !newInside)
                continue;
            if (stack > 0 && surplus > 0 && surplus < stack)
                item.Set("stack", surplus);
            drops.Add((item.Persist(), stack > 0 ? $"{name} x{Math.Max(surplus, 0)}" : newInside && surplus <= 0 ? name + " (what's new in it)" : name));
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
        _context.Log($"Died: dropped {drops.Count} item(s) picked up since the checkpoint"
            + (drops.Count > 0 ? ": " + string.Join(", ", drops.Select(d => d.What)) : ""));
        // (To the place's other players now - and, running it ourselves, into its save, for the host.)
        _loot.Flush();
        if (_ownership.Role != AreaRole.Follower)
            foreach (Instance saver in Instances.All(GameObjectId.o_roomEntitySaver))
                Game.CallBuiltinAs("event_user", saver, saver, 2);
    }

    // A checkpoint item (a save entry: [object, data, container, cell, index, charge, stack, ...]) counted - and what's in
    // it, if it's a bag.
    private static void CountCheckpoint(JsonNode? entry, Dictionary<string, int> quota)
    {
        if (entry is not JsonArray item || item.Count < 2)
            return;
        string key = Identity(item[0]?.ToString() ?? "", item[1]);
        quota[key] = quota.GetValueOrDefault(key) + Amount(item.Count > 6 ? item[6] : null);
        if (item[1]?["lootList"] is JsonArray contents)
            foreach (JsonNode? inside in contents)
                CountCheckpoint(inside, quota);
    }

    private static int Amount(JsonNode? stack)
        => stack is JsonValue v && v.TryGetValue(out double d) && d > 0 ? (int)d : 1;

    // As much of this as the checkpoint still has to give: what of it we keep.
    private static int Take(Dictionary<string, int> quota, string key, int amount)
    {
        int have = quota.GetValueOrDefault(key), kept = Math.Min(have, amount);
        quota[key] = have - kept;
        return kept;
    }

    // A bag's contents (its lootList: entries as the save's, [object, data, ..., stack at 6]) trimmed to what's new: kept
    // ones taken out, a stack cut to its surplus, a bag inside trimmed the same way. Whether anything new is left.
    private static bool Trim(DsList contents, Dictionary<string, int> quota)
    {
        for (int i = contents.Count - 1; i >= 0; i--)
        {
            if (DsList.From(contents[i]) is not { } entry || DsMap.From(entry[1]) is not { } data)
                continue;
            int stack = entry[6] is { Kind: GmKind.Real } s && s.AsReal > 0 ? (int)s.AsReal : 0;
            int surplus = Math.Max(stack, 1) - Take(quota, Identity(entry[0].AsString, data.ToJsonNode()), Math.Max(stack, 1));
            bool newInside = data["lootList"].AsDsList is { } inner && Trim(inner, quota);
            if (surplus <= 0 && !newInside)
                contents.RemoveAt(i);
            else if (stack > 0 && surplus > 0 && surplus < stack)
                entry[6] = surplus;
        }
        return contents.Count > 0;
    }

    // What an item is: its object (or its id name) and its data, less what changes as it's used - as one string.
    private static string Identity(string name, JsonNode? data)
    {
        var text = new StringBuilder(name).Append('|');
        if (data is JsonObject map)
            foreach (var (key, value) in map.OrderBy(p => p.Key, StringComparer.Ordinal))
                if (!Changing.Contains(key))
                    text.Append(key).Append('=').Append(Canonical(value)).Append(';');
        return text.ToString();
    }

    // A value written the same way however it was encoded (the save's numbers "1.0", json_encode's "1").
    private static string Canonical(JsonNode? value) => value switch
    {
        null => "null",
        JsonArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
        JsonObject map => "{" + string.Join(",", map.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + Canonical(p.Value))) + "}",
        JsonValue v when v.TryGetValue(out double d) => d.ToString("R", CultureInfo.InvariantCulture),
        JsonValue v when v.TryGetValue(out bool b) => b ? "1" : "0",
        _ => value.ToString(),
    };

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
