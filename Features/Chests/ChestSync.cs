using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Chests;

// Chests, barrels, tombs and the rest (the game's containers in the world) where players are together: one set of
// contents for everyone there. Each game keeps its containers' items in them (StoneForge's Containers), so:
// - A container someone opens is theirs while it's open (ChestKind.Opened): another player trying to open it is told
//   who's looking in it, in the game's log, and it stays shut for them.
// - Closing it sends what's in it now (ChestKind.Closed), and the others' becomes that (Containers.SetContents) - opened,
//   for one they'd never opened: its loot is what was rolled, not rolled again. (A container's first roll is seeded by
//   its place in the world, so it's the same loot in every game anyway.)
// - When players come together, the place's owner (AreaOwnership) sends each container it has opened as it first sees
//   it (ChestKind.Contents): what the others have there may be older.
// Containers are matched by object and position (every game builds a place alike). One off screen when its contents
// come gets them when it's next on. Two opening the same one at once: the last to close it decides what's in it.
// Where nobody else is, nothing's shared: leaving, the place's save is (WorldSync).
public sealed class ChestSync
{
    // (Every this many frames.)
    private const int Interval = 6;

    // The containers whose opening is their own user event 3: the two kinds (c_container - barrels, tombs, corpses...;
    // c_abstract_chest - chests), and those of theirs with their own that doesn't call their kind's (their own loot).
    private static readonly string[] OpenEvents =
    {
        "c_container", "c_abstract_chest",
        "o_ProselyteContractChest01", "o_ProselyteContractChest02", "o_ProselyteContractChest03", "o_cryptContractChest01",
        "o_cryptContractChestBlacktablet", "o_tomb_special", "o_carvancoop", "o_CaravanPigeonCage", "o_CraftingTable_hl",
        "o_prisoncart_container", "o_tcorpse", "o_tcorpse1", "o_tutorstand", "o_lootcrate1", "o_prolog_archives",
        "o_prolog_archives02", "o_prolog_archives03", "o_prolog_archives05", "o_prologuewfallcorpse", "o_abbeybarrels01",
        "o_whitchousecontainer02", "o_whitchousecontainer03", "o_whitchousecontainer04", "o_chickencoop01", "o_barrels02",
    };

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly AreaOwnership _ownership;
    private readonly Func<bool> _inSharedWorld;
    private int _frame;
    // The place we're keeping, and who else was there; containers the owner has sent since (owner) - every one it has
    // seen; contents come for containers that weren't on screen (or were open here); who has which open (by key: slot).
    private string? _place;
    private string _others = "";
    private readonly HashSet<string> _sent = new();
    private readonly Dictionary<string, string> _pending = new();
    private readonly Dictionary<string, int> _inUse = new();
    // Containers we've closed whose contents haven't gone yet (sent once they can be read: the window's gone).
    private readonly List<Instance> _closed = new();
    private bool _applying;

    public ChestSync(ModContext context, Session session, AreaOwnership ownership, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _ownership = ownership;
        _inSharedWorld = inSharedWorld;
        session.On<ChestPacket>(Receive);
        session.PlayerLeft += player =>
        {
            foreach (var (key, slot) in _inUse.ToList())
                if (slot == player.Slot)
                    _inUse.Remove(key);
        };
        Containers.OnOpened(context, Opened);
        Containers.OnClosed(context, Closed);
        // Opening a container (its user event 3: its loot rolled the first time, its window made): not one another
        // player has open.
        foreach (string obj in OpenEvents)
            context.OnCode($"gml_Object_{obj}_Other_13", before: (container, _) => InUseByAnother(container));
    }

    public void Clear()
    {
        _place = null;
        _others = "";
        _sent.Clear();
        _pending.Clear();
        _inUse.Clear();
        _closed.Clear();
    }

    public void Tick()
    {
        string? place = OurPlayer.State()?.Place;
        var here = place == null
            ? new List<int>()
            : _session.Players.Where(p => p.State?.Place == place).Select(p => p.Slot).OrderBy(s => s).ToList();
        if (!_session.Connected || !_inSharedWorld() || !Gm.InGame || place == null || here.Count == 0 || Rooms.IsChanging)
        {
            if (_place != null)
                Clear();
            return;
        }
        // (A new place, or someone's come or gone: the owner sends its containers afresh; a lock of someone who's left
        // goes.)
        SendClosed();
        string others = string.Join(",", here);
        bool fresh = place != _place || others != _others;
        if (fresh)
        {
            if (place != _place)
                _pending.Clear();
            _place = place;
            _others = others;
            _sent.Clear();
            foreach (var (key, slot) in _inUse.ToList())
                if (!here.Contains(slot))
                    _inUse.Remove(key);
        }
        else if (++_frame % Interval != 0)
            return;
        bool owner = _ownership.Place == place && _ownership.Role == AreaRole.Owner;
        int sent = 0, applied = 0;
        foreach (Instance container in All())
        {
            if (Containers.IsOpen(container))
                continue;
            string key = KeyOf(container);
            if (_pending.Remove(key, out string? json))
            {
                Apply(container, json);
                applied++;
                continue;
            }
            // (The owner's: each container it has opened, as it first sees it - one it hasn't is as every game rolls it.)
            if (owner && _sent.Add(key) && Containers.HasBeenOpened(container) && Containers.ContentsJson(container) is { } contents)
            {
                Send(ChestKind.Contents, key, contents);
                sent++;
            }
        }
        if (fresh && sent > 0)
            _context.Log($"Containers here: sent {sent} opened one(s) to the others");
        if (applied > 0)
            _context.Log($"Containers here: {applied} made as the others have them");
    }

    // ---- ours ----

    // Before a container opens: if another player here has it open, it stays shut (the game's own code skipped), and
    // we're told who.
    private bool InUseByAnother(Instance container)
    {
        if (_place == null || container.IsNone || !Shared(container) || !_inUse.TryGetValue(KeyOf(container), out int slot))
            return false;
        string who = _session.Players.FirstOrDefault(p => p.Slot == slot)?.Name ?? "Someone";
        Game.CallScript("scr_actionsLogAddMessage", default, $"{who} is looking in it.");
        return true;
    }

    private void Opened(OpenContainer open)
    {
        Instance container = open.Container;
        if (_place == null || container.IsNone || !Shared(container))
            return;
        _session.Send(new ChestPacket(_place, KeyOf(container), ChestKind.Opened, Array.Empty<byte>()));
    }

    // Closed: what's in it now to the others (ours, as the last to close it) - unless it's another game's contents
    // we're putting in.
    private void Closed(Instance container)
    {
        if (_applying || _place == null || OurPlayer.State()?.Place != _place || !Shared(container))
            return;
        _pending.Remove(KeyOf(container));
        _closed.Add(container);
        SendClosed();
    }

    // Our closed containers' contents, to the others - each once it can be read (never sent empty for not knowing).
    private void SendClosed()
    {
        for (int i = _closed.Count - 1; i >= 0; i--)
        {
            Instance container = _closed[i];
            if (!container.Exists)
                _closed.RemoveAt(i);
            else if (Containers.ContentsJson(container) is { } contents)
            {
                _closed.RemoveAt(i);
                Send(ChestKind.Closed, KeyOf(container), contents);
            }
        }
    }

    private void Apply(Instance container, string json)
    {
        _applying = true;
        try
        {
            if (!Containers.SetContents(container, json))
                _context.Log($"Containers here: couldn't make {KeyOf(container)} as the others have it");
        }
        finally { _applying = false; }
    }

    // ---- network ----

    private void Send(ChestKind kind, string key, string json)
        => _session.Send(new ChestPacket(_place!, key, kind, JoinCompression.Compress(json)));

    private void Receive(RemotePlayer from, ChestPacket packet)
    {
        if (!Gm.InGame || OurPlayer.State()?.Place != packet.Place)
            return;
        if (packet.Kind == ChestKind.Opened)
        {
            _inUse[packet.Key] = from.Slot;
            return;
        }
        // Closed (theirs no longer), or the owner's as it is: ours made the same - now, if it's on screen and shut here;
        // else when it is.
        if (packet.Kind == ChestKind.Closed && _inUse.TryGetValue(packet.Key, out int slot) && slot == from.Slot)
            _inUse.Remove(packet.Key);
        string json = JoinCompression.Decompress(packet.Data);
        Instance found = All().FirstOrDefault(c => KeyOf(c) == packet.Key);
        if (found.IsNone || Containers.IsOpen(found))
            _pending[packet.Key] = json;
        else
            Apply(found, json);
    }

    // The shared containers on screen here: both kinds.
    private static IEnumerable<Instance> All()
        => Instances.All(GameObjectId.c_container).Concat(Instances.All(GameObjectId.c_abstract_chest)).Where(Shared);

    // A container in the world (its own loot list) - not a bag the player carries, nor a player's own stash (each
    // player's: o_player_chest).
    private static bool Shared(Instance container)
        => container.Get("loot_list").Kind == GmKind.Real && Gm.ObjectGetName(container.Get("object_index").AsInt) != "o_player_chest";

    // A container's key: its object and position - the same container in every game, which build the place alike.
    private static string KeyOf(Instance container)
        => $"{Gm.ObjectGetName(container.Get("object_index").AsInt)}_{Math.Floor(container.Get("x").AsReal)}_{Math.Floor(container.Get("y").AsReal)}";
}
