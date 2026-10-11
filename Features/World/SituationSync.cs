using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using StoneForge;
using StoneshardMP.Features.Join;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

// The daily roll of every village's situations (the patcher makes it hookable).
[assembly: HookScript(nameof(Scripts.scr_globaltile_situations_daily))]

namespace StoneshardMP.Features.World;

// A settlement's situations - a fair, a pilgrimage, a rut, rats, reinforcements, its economy - are the host's. The game
// rolls them every day for every village (scr_globaltile_situations_daily, from the hourly clock), at random, into the
// village's world-map tile (situationsDataMap); entering the settlement, its room builds the props and NPCs of the ones
// on (its Situation_<key> layers: the fair's stalls, traders, music, the lantern by the inn) and takes away the rest.
// Rolled in every game, they parted ways after the first day - a fair in one game and none in the other.
// - A client in the host's world rolls none (the daily roll skipped); it has the host's.
// - The host sends every village's as they change, checked every few seconds, and again to someone new in the world.
//   A client puts each in place in its tile (the same map, its contents replaced: what refers to it sees them). As in
//   one game, a settlement shows a change when it's next entered.
public sealed class SituationSync
{
    // (Every few seconds: they change once a day, and by a few quest steps.)
    private const int Interval = 180;

    private readonly Session _session;
    private readonly JoinManager _join;
    private string _sent = "", _present = "";
    private int _frame, _applied;

    public SituationSync(ModContext context, Session session, JoinManager join)
    {
        _session = session;
        _join = join;
        session.On<SituationsPacket>(Receive);
        Scripts.scr_globaltile_situations_daily.Before(context, _ => _session.Mode == Session.SessionMode.Client && _join.ClientInWorld);
    }

    /// <summary>Dev tools: a line on where it's got to.</summary>
    public string DevSummary => _session.Mode == Session.SessionMode.Host
        ? $"sent {(_sent.Length == 0 ? "none yet" : _sent.Length + " chars")}"
        : $"villages applied {_applied}";

    public void Clear()
    {
        _sent = _present = "";
        _frame = 0;
    }

    // Host: each frame.
    public void Tick()
    {
        if (_session.Mode != Session.SessionMode.Host || !JoinSave.HostInWorld() || !_session.Players.Any())
        {
            _sent = _present = "";
            return;
        }
        if (++_frame % Interval != 0)
            return;
        // (Someone's come into the world: everything again, for them.)
        string present = string.Join(",", _session.Players.Where(p => p.State != null).Select(p => p.Slot).OrderBy(s => s));
        string json = Read().ToJsonString();
        if (json == _sent && present == _present)
            return;
        _sent = json;
        _present = present;
        _session.Send(new SituationsPacket(JoinCompression.Compress(json)));
    }

    // Every village's situations: "x_y" to its tile's situationsDataMap.
    private static JsonObject Read()
    {
        var all = new JsonObject();
        foreach (var (x, y) in Villages())
            if (Situations(x, y) is { } map)
                all[$"{x}_{y}"] = map.ToJsonNode();
        return all;
    }

    private static List<(int X, int Y)> Villages()
    {
        var result = new List<(int, int)>();
        using GmArray? villages = Game.CallScript("scr_glmap_getLocationByClass", default, "Village").AsArray;
        if (villages == null)
            return result;
        for (int i = 0; i < villages.Length; i++)
        {
            if (villages[i].AsStruct is not { } village)
                continue;
            using (village)
                result.Add((village["x"].AsInt, village["y"].AsInt));
        }
        return result;
    }

    private static DsMap? Situations(int x, int y)
        => DsMap.From(Game.CallScript("scr_globaltile_get", default, "situationsDataMap", x, y, -1)) is { Exists: true } map ? map : null;

    // Client: the host's, each village's put in place.
    private void Receive(RemotePlayer from, SituationsPacket packet)
    {
        if (_session.Mode != Session.SessionMode.Client || from.Slot != 0 || !_join.ClientInWorld)
            return;
        if (JsonNode.Parse(JoinCompression.Decompress(packet.Data)) is not JsonObject all)
            return;
        foreach (var (key, node) in all)
        {
            string[] at = key.Split('_');
            if (node == null || at.Length != 2 || !int.TryParse(at[0], out int x) || !int.TryParse(at[1], out int y))
                continue;
            if (Situations(x, y) is not { } mine || DsMap.FromJson(node.ToJsonString()) is not { } given)
                continue;
            mine.AssignFrom(given);
            given.Destroy();
            _applied++;
        }
    }
}
