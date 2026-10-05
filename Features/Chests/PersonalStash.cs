using System;
using System.Collections.Generic;
using StoneForge;
using StoneshardMP.Features.Join;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Chests;

// The player's own chest (o_player_chest, by the bed): each player's, in a multiplayer world. The game keeps a chest's
// items in it, saved with its place - one copy for everyone, which the place's saves going between games (WorldSync)
// made whoever left last's. So each player's stash in it is kept apart, in the host's save data (by world slot - the
// player slot they play, WorldSlots -, place and chest), and put in the chest just before that player opens it; closing it, what's in it is kept as theirs. A client's
// goes to the host to keep (StashPacket), and comes back with the host's world when it joins. What the chest holds in
// the place's own save doesn't matter any more. Not shared live (ChestSync leaves it out): two players at one chest see
// their own.
public sealed class PersonalStash
{
    // Where the stashes are kept in the save data: "<world slot>|<place>|<chest>" -> its items (Containers.ContentsJson).
    internal const string StashesKey = "mpStashes";

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Func<int> _worldSlot;
    private readonly WorldSlots _slots;
    private readonly Func<bool> _inSharedWorld;
    // Stashes we've closed, and their ids, whose items haven't been kept yet (once they can be read: the window's gone).
    private readonly List<(Instance Chest, string Id)> _closed = new();

    public PersonalStash(ModContext context, Session session, Func<int> worldSlot, WorldSlots slots, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _worldSlot = worldSlot;
        _slots = slots;
        _inSharedWorld = inSharedWorld;
        session.On<StashPacket>(Receive);
        // Opening a chest (c_abstract_chest's user event 3): ours, if it's a stash.
        context.OnCode("gml_Object_c_abstract_chest_Other_13", before: (chest, _) =>
        {
            if (IsStash(chest) && Active)
                Fill(chest);
            return false;
        });
        Containers.OnClosed(context, chest =>
        {
            if (IsStash(chest) && Active && Id(chest) is { } id)
            {
                _closed.Add((chest, id));
                Keep();
            }
        });
    }

    public void Clear() => _closed.Clear();

    public void Tick()
    {
        if (_closed.Count > 0)
            Keep();
    }

    // In a multiplayer world: playing one with others, or one whose stashes are kept apart already (the host on its own).
    private bool Active => SaveData.Available
        && (_session.Connected && _inSharedWorld() || SaveData.Map?.GetMap(StashesKey) != null);

    // Our stash in this chest, put in it. None kept yet: the host's (or ours alone) is what's in it - a stash from before
    // they were kept apart is the host's; a client's starts empty.
    private void Fill(Instance chest)
    {
        if (Id(chest) is not { } id)
            return;
        string key = Key(_worldSlot(), id);
        GmValue kept = SaveData.ModMap(StashesKey)[key];
        string? items = kept.Kind == GmKind.String ? kept.AsString
            : _session.Connected && _session.Mode == Session.SessionMode.Client ? "[]" : null;
        if (items != null && !Containers.SetContents(chest, items))
            _context.Log($"Stash {key}: couldn't put it in the chest");
    }

    // Our closed stashes' items, kept as ours - and a client's sent to the host to keep in its world.
    private void Keep()
    {
        for (int i = _closed.Count - 1; i >= 0; i--)
        {
            var (chest, id) = _closed[i];
            if (!chest.Exists || !SaveData.Available)
            {
                _closed.RemoveAt(i);
                continue;
            }
            if (Containers.ContentsJson(chest) is not { } items)
                continue;
            _closed.RemoveAt(i);
            string key = Key(_worldSlot(), id);
            SaveData.ModMap(StashesKey)[key] = items;
            if (_session.Connected && _session.Mode == Session.SessionMode.Client)
                _session.Send(new StashPacket(id, JoinCompression.Compress(items)), to: 0);
        }
    }

    // Host: a client's stash, kept as theirs (by the world slot they play) in our world's save data.
    private void Receive(RemotePlayer from, StashPacket packet)
    {
        if (_session.Mode != Session.SessionMode.Host || !SaveData.Available)
            return;
        SaveData.ModMap(StashesKey)[Key(_slots.Of(from.Slot), packet.Chest)] = JoinCompression.Decompress(packet.Data);
    }

    private static string Key(int worldSlot, string id) => worldSlot.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + id;

    private static bool IsStash(Instance chest)
        => !chest.IsNone && Gm.ObjectGetName(chest.Get("object_index").AsInt) == "o_player_chest";

    // A stash's id: its place and position (the same chest in every game) - null with no player.
    private static string? Id(Instance chest)
        => WorldMap.Place is { } place ? $"{place}|{Math.Floor(chest.Get("x").AsReal)}_{Math.Floor(chest.Get("y").AsReal)}" : null;
}
