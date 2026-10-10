using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using StoneForge;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Trade;

// One stock per trader for the whole party. A trader's goods live in the world data of its settlement (npc_data, its
// entry by id_name): what it sells (trade_list - its gold is in there too, as a moneybag), the one-of-a-kind items
// already bought from it, never to come back (singular_stock), its free slots and when it last restocked. Each game had
// its own, so what one player bought was still for sale to the other - unique items too - and one player's selling
// didn't drain the trader's gold for the other.
// - A trade ending (the trade window closing: the game saves the window back into the trader's data) sends that trader's
//   state to everyone, and each game takes it in place of its own. One player trades with an NPC at a time (TalkSync),
//   so the last to close is the latest.
// - A trader not in our world yet (a town we haven't been to since): kept, and given to it as it's first made here, so
//   it doesn't roll a fresh stock of its own.
// - Restocks follow the shared clock: every game's trader restocks at the same hour, and whoever opens it first makes the
//   new stock - which goes to the others as they close.
public sealed class MerchantSync
{
    private static readonly string[] Lists = { "trade_list", "singular_stock" };
    private static readonly string[] Values =
        { "item_slots_count", "is_restock", "is_restock_gold", "is_settlement_restock", "restock_timestamp", "gold_restock_timestamp" };

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    // Traders we were sent that aren't in our world yet: (tile x, tile y, id_name) to their state's JSON.
    private readonly Dictionary<(int, int, string), string> _pending = new();

    public MerchantSync(ModContext context, Session session, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _inSharedWorld = inSharedWorld;
        session.On<TraderPacket>(Receive);
        // (The trade window saving its contents back into the trader's data, as it closes.)
        context.OnCode("gml_Object_o_trade_inventory_Other_13", after: (window, _) => Closed(window));
        // (An NPC set up in our world: one we were sent before it was here.)
        context.OnCode("gml_Object_o_NPC_Alarm_1", after: (npc, _) => Made(npc));
    }

    public void Clear() => _pending.Clear();

    private bool Ready => _session.Connected && _inSharedWorld() && SaveData.Available;

    // Where an NPC's data is: its settlement's tile, and its name there (none: not a trader with data).
    private static (int X, int Y, string Name)? Where(Instance npc)
    {
        if (npc.IsNone || !npc.Exists || npc.Get("id_name") is not { Kind: GmKind.String } name || name.AsString == "")
            return null;
        using GmArray? xy = npc.Get("village_xy").AsArray;
        if (xy == null || xy.Length < 2)
            return null;
        return (xy[0].AsInt, xy[1].AsInt, name.AsString);
    }

    // An NPC's data, in our world (null: not here yet).
    private static DsMap? Data(int x, int y, string name)
    {
        if (Game.CallScript("scr_globaltile_get", default, "npc_data", x, y).AsDsMap is not { Exists: true } npcs || !npcs.Has(name))
            return null;
        return npcs.GetMap(name) ?? (npcs[name].AsDsMap is { Exists: true } data ? data : null);
    }

    // A trade's ended: the trader's state, to everyone.
    private void Closed(Instance window)
    {
        if (!Ready || Where(Instance.Of(window.Get("owner"))) is not { } at || Data(at.X, at.Y, at.Name) is not { } data)
            return;
        var state = new JsonObject();
        foreach (string key in Lists)
            if (data.GetList(key) is { } list)
                state[key] = list.ToJsonNode();
        foreach (string key in Values)
        {
            GmValue value = data.Get(key, default);
            if (value.Kind is GmKind.Real or GmKind.Bool)
                state[key] = value.AsReal;
        }
        _session.Send(new TraderPacket((short)at.X, (short)at.Y, at.Name, JoinCompression.Compress(state.ToJsonString())));
    }

    private void Receive(RemotePlayer from, TraderPacket packet)
    {
        if (!Ready)
            return;
        string json = JoinCompression.Decompress(packet.Data);
        if (Data(packet.TileX, packet.TileY, packet.Npc) is not { } data)
        {
            _pending[(packet.TileX, packet.TileY, packet.Npc)] = json;
            return;
        }
        // (Trading with it ourselves: ours goes out as we close, and is the latest.)
        if (Instances.All(GameObjectId.o_trade_inventory).Any(w => Where(Instance.Of(w.Get("owner"))) == (packet.TileX, packet.TileY, packet.Npc)))
            return;
        Apply(data, json);
        _pending.Remove((packet.TileX, packet.TileY, packet.Npc));
    }

    private void Made(Instance npc)
    {
        if (_pending.Count == 0 || !Ready || Where(npc) is not { } at || !_pending.TryGetValue(at, out string? json)
            || Data(at.X, at.Y, at.Name) is not { } data)
            return;
        _pending.Remove(at);
        Apply(data, json);
        _context.Log($"Trader {at.Name} ({at.X},{at.Y}): given the party's stock as it came into our world");
    }

    // A trader's state, in place of ours: its lists in place (the trader's data keeps them), its values set.
    private static void Apply(DsMap data, string json)
    {
        if (JsonNode.Parse(json) is not JsonObject state)
            return;
        foreach (string key in Lists)
        {
            if (state[key] is not JsonArray array || DsList.FromJson(array.ToJsonString()) is not { } given)
                continue;
            if (data.GetList(key) is { } list)
            {
                list.AssignFrom(given);
                given.Destroy();
            }
            else
                data.AddList(key, given);
        }
        foreach (string key in Values)
            if (state[key] is JsonValue value && value.TryGetValue(out double number))
                data[key] = number;
    }
}
