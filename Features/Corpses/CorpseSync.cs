using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Corpses;

// Death corpses belong to the area owner, as in the original GML mod. Use the
// game's save format, including meat/pelts, decay and resurrection metadata.
public sealed class CorpseSync
{
    private readonly Session _session;
    private readonly AreaOwnership _ownership;
    private readonly Func<bool> _inSharedWorld;
    private string? _place;
    private int _owner = -1;
    private string _others = "";
    private string? _last;
    private JsonArray? _snapshot;
    private readonly Dictionary<Instance, string> _applied = new();
    private int _frame;
    private long _asked;
    private bool _owed;

    public CorpseSync(Session session, AreaOwnership ownership, Func<bool> inSharedWorld)
    {
        _session = session;
        _ownership = ownership;
        _inSharedWorld = inSharedWorld;
        session.On<CorpsesPacket>(Receive);
    }

    public void Clear()
    {
        _place = null;
        _owner = -1;
        _others = "";
        _last = null;
        _snapshot = null;
        _applied.Clear();
        _asked = 0;
        _owed = true;
    }

    private bool Ready => _session.Connected && _inSharedWorld() && Gm.InGame && !Rooms.IsChanging
        && _ownership.Place == OurPlayer.State()?.Place && _ownership.Role != AreaRole.Alone;

    private void Prepare()
    {
        if (_place == _ownership.Place && _owner == _ownership.Owner)
            return;
        Clear();
        _place = _ownership.Place;
        _owner = _ownership.Owner;
    }

    public void Tick()
    {
        if (!Ready)
        {
            Clear();
            return;
        }
        Prepare();
        if (++_frame % 20 != 0)
            return;
        if (_ownership.Role == AreaRole.Owner)
        {
            string others = string.Join(",", _ownership.Others.OrderBy(s => s));
            if (_others != others)
                _owed = true;
            _others = others;
            string json = Capture().ToJsonString();
            if (_owed || json != _last)
            {
                byte[] data = JoinCompression.Compress(json);
                foreach (int slot in _ownership.Others)
                    _session.Send(new CorpsesPacket(_place!, false, data), slot);
                _last = json;
                _owed = false;
            }
        }
        else if (_snapshot != null)
            Apply(_snapshot);
        else if (Environment.TickCount64 - _asked >= 1000)
        {
            _asked = Environment.TickCount64;
            _session.Send(new CorpsesPacket(_place!, true, Array.Empty<byte>()), _owner);
        }
    }

    private void Receive(RemotePlayer from, CorpsesPacket packet)
    {
        if (!Ready || packet.Place != _ownership.Place)
            return;
        Prepare();
        if (packet.Request)
        {
            if (_ownership.Role == AreaRole.Owner && _ownership.Others.Contains(from.Slot))
                _owed = true;
            return;
        }
        if (_ownership.Role != AreaRole.Follower || from.Slot != _owner)
            return;
        if (JsonNode.Parse(JoinCompression.Decompress(packet.Data)) is not JsonArray snapshot)
            return;
        _snapshot = snapshot;
        Apply(snapshot);
    }

    private static List<Instance> All()
        => Instances.All(GameObjectId.o_abstract_corpse, includeCulled: true)
            .Where(i => Awake(i, () => i.Get("roomEntityType").AsString == "dynamic")).ToList();

    private static string Key(Instance corpse)
        => $"{Gm.ObjectGetName(corpse.Get("object_index").AsInt)}|{Math.Floor(corpse.Get("x").AsReal)}|{Math.Floor(corpse.Get("y").AsReal)}";

    private static JsonArray Capture()
    {
        var list = new JsonArray();
        foreach (Instance corpse in All())
        {
            Awake(corpse, () =>
            {
                if (Game.CallScript("scr_locationRoomEntityCorpsesSaveDataGet", default, corpse).AsDsMap is not { } saved)
                    throw new InvalidOperationException("Could not save corpse");
                try
                {
                    list.Add(new JsonObject
                    {
                        ["key"] = Key(corpse),
                        ["tag"] = corpse.Get("roomEntityTag").AsString,
                        ["preset"] = corpse.Get("roomEntityPresetTag").AsString,
                        ["data"] = saved.ToJson(),
                    });
                }
                finally { saved.Destroy(); }
                return true;
            });
        }
        // Stable ordering avoids sending identical snapshots when culling reorders instances.
        return new JsonArray(list.OrderBy(n => n!["key"]!.GetValue<string>()).Select(n => n!.DeepClone()).ToArray());
    }

    private void Apply(JsonArray snapshot)
    {
        var wanted = snapshot.ToDictionary(n => n!["key"]!.GetValue<string>(), n => n!);
        var present = new HashSet<Instance>();
        foreach (Instance corpse in All())
        {
            string key = Awake(corpse, () => Key(corpse));
            if (!wanted.Remove(key, out JsonNode? entry))
            {
                corpse.Destroy();
                _applied.Remove(corpse);
                continue;
            }
            present.Add(corpse);
            string json = entry["data"]!.GetValue<string>();
            if (_applied.GetValueOrDefault(corpse) != json)
            {
                Awake(corpse, () => { Restore(corpse, json); return true; });
                _applied[corpse] = json;
            }
        }
        foreach (JsonNode entry in wanted.Values)
        {
            string json = entry["data"]!.GetValue<string>();
            Instance made = Create(entry, json);
            if (!made.IsNone && made.Exists)
            {
                present.Add(made);
                _applied[made] = json;
            }
        }
        foreach (Instance gone in _applied.Keys.Where(i => !present.Contains(i)).ToList())
            _applied.Remove(gone);
    }

    private static void Restore(Instance corpse, string json)
    {
        if (DsMap.FromJson(json) is not { } saved)
            throw new InvalidOperationException("Invalid corpse save data");
        try { Game.CallScript("scr_locationRoomEntityCorpsesSaveDataSet", default, corpse, saved.Id); }
        finally { saved.Destroy(); }
    }

    private static Instance Create(JsonNode entry, string json)
    {
        if (DsMap.FromJson(json) is not { } saved)
            return default;
        string[] globals = { "locationRoomEntityType", "locationRoomEntityTag", "locationRoomEntityPresetTag",
            "locationRoomEntityTypePrevious", "locationRoomEntityTagPrevious", "locationRoomEntityPresetTagPrevious" };
        var before = globals.Select(n => Game.Global[n]).ToArray();
        try
        {
            int obj = Game.CallBuiltin("asset_get_index", saved.Get("object_name", "N/A")).AsInt;
            if (obj < 0 || !Game.CallBuiltin("object_exists", obj).AsBool
                || (obj != (int)GameObjectId.o_abstract_corpse
                    && !Game.CallBuiltin("object_is_ancestor", obj, (int)GameObjectId.o_abstract_corpse).AsBool))
                return default;
            Game.CallScript("scr_locationRoomEntityInitDataSet", default, "dynamic",
                entry["tag"]!.GetValue<string>(), entry["preset"]!.GetValue<string>());
            Instance corpse = Instance.Of(Game.CallScript("scr_locationRoomEntityCorpsesInstanceCreate", default, saved.Id));
            if (!corpse.IsNone && corpse.Exists)
                Game.CallScript("scr_locationRoomEntityCorpsesSaveDataSet", default, corpse, saved.Id);
            return corpse.Persist();
        }
        finally
        {
            for (int i = 0; i < globals.Length; i++)
                Game.Global[globals[i]] = before[i];
            saved.Destroy();
        }
    }

    // Temporarily wake off-screen corpses so the game's with() can save/restore
    // them, then return them to the culling list's original deactivated state.
    private static T Awake<T>(Instance corpse, Func<T> action)
    {
        bool culled = corpse.IsCulled;
        if (culled)
            Game.CallBuiltin("instance_activate_object", corpse);
        try { return action(); }
        finally
        {
            if (culled)
                Game.CallBuiltin("instance_deactivate_object", corpse);
        }
    }
}
