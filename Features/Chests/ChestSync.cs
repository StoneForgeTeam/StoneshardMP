using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
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
// Fires to cook at - campfires, firepits, hearths, the caravan's pot (o_campfire_parent) - are kept the same way: a fire
// keeps what's in its cooking pot in itself as a container does (its loot_list, saved from the cooking window as it
// closes), so one cook at a time, and what they left goes to the others - with whether the pot's still there (taken
// out, the fire's drawn without it) and whether it's lit (lighting or putting it out goes at once).
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
        // A fire's cooking window: opening it (the fire's user event 0, lit) not while another player's cooking at it;
        // its pot's contents loaded into it (ours open); saved back as it closes (ours closed). Lit or put out: at once.
        context.OnCode("gml_Object_o_campfire_parent_Other_10",
            before: (fire, _) => !fire.IsNone && fire.Get("is_fire").AsBool && InUseByAnother(fire));
        context.OnCode("gml_Object_o_craftingFoodMenu_Other_11", after: (menu, _) =>
        {
            if (!menu.IsNone && Instance.Of(menu.Get("parent")) is { IsNone: false } fire && IsFire(fire))
                Opened(fire);
        });
        var closing = new Stack<Instance>();
        context.OnCode("gml_Object_o_craftingMenu_Other_25",
            before: (menu, _) =>
            {
                closing.Push(menu.IsNone ? default : Instance.Of(menu.Get("parent")));
                return false;
            },
            after: (_, _) =>
            {
                if (closing.Count > 0 && closing.Pop() is { IsNone: false } fire && fire.Exists && IsFire(fire))
                    Closed(fire);
            });
        context.OnCode("gml_Object_o_campfire_parent_Other_12", after: (fire, _) => Lit(fire));
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
            if (IsOpen(container))
                continue;
            string key = KeyOf(container);
            if (_pending.Remove(key, out string? json))
            {
                Apply(container, json);
                applied++;
                continue;
            }
            // (The owner's: each container it has opened, as it first sees it - one it hasn't is as every game rolls it.)
            if (owner && _sent.Add(key) && (IsFire(container) || Containers.HasBeenOpened(container)) && ContentsOf(container) is { } contents)
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

    private void Opened(OpenContainer open) => Opened(open.Container);

    private void Opened(Instance container)
    {
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
            else if (ContentsOf(container) is { } contents)
            {
                _closed.RemoveAt(i);
                Send(ChestKind.Closed, KeyOf(container), contents);
            }
        }
    }

    // A fire lit or put out (by us): how it is now, to the others at once.
    private void Lit(Instance fire)
    {
        if (_applying || _place == null || fire.IsNone || OurPlayer.State()?.Place != _place || !Shared(fire) || IsOpen(fire))
            return;
        if (ContentsOf(fire) is { } contents)
            Send(ChestKind.Contents, KeyOf(fire), contents);
    }

    // What's in a container, as the others are sent it (null: can't be read now - open). A fire's: its pot's contents,
    // whether the pot's there (which of its looks it has: 0, with it), and whether it's lit.
    private static string? ContentsOf(Instance container)
    {
        if (IsOpen(container) || Containers.ContentsJson(container) is not { } items)
            return null;
        if (!IsFire(container))
            return items;
        return new JsonObject
        {
            ["items"] = JsonNode.Parse(items),
            ["look"] = PotLook(container),
            ["lit"] = container.Get("image_speed").AsReal != 0,
        }.ToJsonString();
    }

    // Which of a fire's looks it has (sprite_array: 0 with its pot, 1 without; -1, none).
    private static int PotLook(Instance fire)
    {
        using GmArray? looks = fire.Get("sprite_array").AsArray;
        for (int i = 0; looks != null && i < looks.Length; i++)
            if (looks[i].AsReal == fire.Get("sprite_index").AsReal)
                return i;
        return -1;
    }

    private void Apply(Instance container, string json)
    {
        _applying = true;
        try
        {
            if (IsFire(container))
                ApplyFire(container, json);
            else if (!Containers.SetContents(container, json))
                _context.Log($"Containers here: couldn't make {KeyOf(container)} as the others have it");
        }
        finally { _applying = false; }
    }

    // A fire as another game has it: its pot's contents, its look (the pot there or not), lit or out.
    private void ApplyFire(Instance fire, string json)
    {
        if (JsonNode.Parse(json) is not JsonObject state || state["items"] is not JsonArray items
            || !Containers.SetContents(fire, items.ToJsonString()))
        {
            _context.Log($"Containers here: couldn't make {KeyOf(fire)} as the others have it");
            return;
        }
        int look = state["look"]?.GetValue<int>() ?? -1;
        using (GmArray? looks = fire.Get("sprite_array").AsArray)
            if (looks != null && look >= 0 && look < looks.Length && fire.Get("sprite_index").AsReal != looks[look].AsReal)
            {
                fire["sprite_index"] = looks[look];
                Game.CallScript("scr_set_hl", fire);
            }
        bool lit = state["lit"]?.GetValue<bool>() ?? false;
        if (lit != (fire.Get("image_speed").AsReal != 0))
        {
            // (Its step does the rest: light, sound, smoke - as lighting it does.)
            fire["image_speed"] = lit ? 0.8 : 0;
            if (!lit)
                fire["image_index"] = 0;
        }
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
        if (found.IsNone || IsOpen(found))
            _pending[packet.Key] = json;
        else
            Apply(found, json);
    }

    // The shared containers on screen here: both kinds, and fires to cook at.
    private static IEnumerable<Instance> All()
        => Instances.All(GameObjectId.c_container).Concat(Instances.All(GameObjectId.c_abstract_chest))
            .Concat(Instances.All(GameObjectId.o_campfire_parent).Where(f => !f.Get("persistent").AsBool)).Where(Shared);

    // A fire to cook at (its pot's contents its loot_list).
    private static bool IsFire(Instance container)
        => Gm.ObjectIsAncestor(container.Get("object_index").AsInt, (int)GameObjectId.o_campfire_parent)
            || container.Get("object_index").AsInt == (int)GameObjectId.o_campfire_parent;

    // Open here: its window (a container's), or its cooking window (a fire's: its pot's contents are in it till it closes).
    private static bool IsOpen(Instance container)
        => Containers.IsOpen(container)
            || (IsFire(container) && Instances.All(GameObjectId.o_craftingMenu).Any(m => Instance.Of(m.Get("parent")).Equals(container)));

    // A container in the world (its own loot list) - not a bag the player carries, nor a player's own stash (each
    // player's: o_player_chest).
    private static bool Shared(Instance container)
        => container.Get("loot_list").Kind == GmKind.Real && Gm.ObjectGetName(container.Get("object_index").AsInt) != "o_player_chest";

    // A container's key: its object and position - the same container in every game, which build the place alike.
    private static string KeyOf(Instance container)
        => $"{Gm.ObjectGetName(container.Get("object_index").AsInt)}_{Math.Floor(container.Get("x").AsReal)}_{Math.Floor(container.Get("y").AsReal)}";
}
