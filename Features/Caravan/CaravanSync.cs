using System;
using System.Linq;
using System.Text.Json.Nodes;
using StoneForge;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

// The caravan moving to another tile (its arrival: the host's tells the others), and its world-map travel button (a
// client's is refused: the host moves it).
[assembly: HookScript(nameof(Scripts.scr_caravanPositionSet))]

namespace StoneshardMP.Features.Caravan;

// One caravan for the whole party, the host's:
// - Its state - global.caravanDataMap: the tile it's on, its movement cooldown, its upgrades, followers (their loyalty,
//   perks, time spent), events, camp and appearance - is kept alike in every game. The host sends it as it changes (and
//   to anyone coming in); a client whose own action changed it (an upgrade, a recruit, an event) sends it to the host,
//   which takes it and sends it on. Taken in place (DsMap.AssignFrom), so the game's globals pointing into it
//   (global.caravanUpgradesList, global.caravanFollowersList...) stay good. Its camp is in it too (roomMap,
//   global.caravanRoomMap: what the camp had as it was last left - the foraging spots' harvest, the coop, the horses),
//   so what one player foraged there is gone for everyone; players at the camp together share its ground loot (LootSync).
// - Its storage - the four tabs, caravanStashDataList1-4, where its fodder is too - the same way, and one player in it
//   at a time: someone else's storage window open, ours closes, with who's in it said. Its contents go out as they
//   change - as the window closes, as fodder is used.
// - Only the host moves it (the world map's travel button, riding with it or sending it ahead by pigeon - a client's is
//   refused, with why). When it arrives at a new tile, players at its old camp - on that tile, outdoors on the surface -
//   come along: they travel to the new camp. Players elsewhere stay where they are. (Its travel time passes for
//   everyone as the host's clock: SleepSync.)
public sealed class CaravanSync
{
    private const int Interval = 30;
    private static readonly string[] StashLists = { "caravanStashDataList1", "caravanStashDataList2", "caravanStashDataList3", "caravanStashDataList4" };

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    // What we last had of each - sent, or taken - so a change of ours is told from one we were given.
    private string _state = "", _stash = "";
    private string _present = "";
    // Who's in the storage (a session slot; -1: nobody else), and whether we are.
    private int _stashHolder = -1;
    private bool _inStash;
    private int _frame;

    public CaravanSync(ModContext context, Session session, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _inSharedWorld = inSharedWorld;
        session.On<CaravanDataPacket>(ReceiveData);
        session.On<CaravanStashUsePacket>(ReceiveUse);
        session.On<CaravanMovedPacket>(ReceiveMoved);
        session.PlayerLeft += player =>
        {
            if (_stashHolder == player.Slot)
                _stashHolder = -1;
        };
        // (The world map's caravan travel - riding with it, or sending it by pigeon: only the host's.)
        context.OnCode("gml_Object_o_globalmapInteractiveCaravan_Other_22", before: (_, _) => Refused());
        // (It's arrived somewhere new - the host's: where from and to, for those at its old camp.)
        Scripts.scr_caravanPositionSet.Before(context, call =>
        {
            if (_session.Mode == Session.SessionMode.Host && Ready && call.Args.Length >= 2
                && Caravan() is { } caravan && caravan["gridX"] is { Kind: GmKind.Real } x && caravan["gridY"] is { Kind: GmKind.Real } y
                && x.AsInt >= 0)
                _session.Send(new CaravanMovedPacket(x.AsInt, y.AsInt, call.Args[0].AsInt, call.Args[1].AsInt));
            return false;
        });
    }

    public void Clear()
    {
        _state = _stash = _present = "";
        _stashHolder = -1;
        _inStash = false;
    }

    private bool Ready => _session.Connected && _inSharedWorld() && SaveData.Available;

    private static DsMap? Caravan() => Game.Global["caravanDataMap"].AsDsMap is { Exists: true } map ? map : null;

    private static DsList? Tab(int i) => Game.Global[StashLists[i]].AsDsList is { Exists: true } list ? list : null;

    private string HostName => _session.Players.FirstOrDefault(p => p.Slot == 0)?.Name ?? "the host";

    // A client at the world map's caravan travel: no.
    private bool Refused()
    {
        if (_session.Mode != Session.SessionMode.Client || !_inSharedWorld())
            return false;
        Gm.AudioPlaySound(Sound.snd_mouse_skill_denied, 4);
        Game.CallScript("scr_actionsLogAddMessage", default, $"Only {HostName} can move the caravan.");
        return true;
    }

    public void Tick()
    {
        if (!Ready)
        {
            if (!_session.Connected)
                Clear();
            return;
        }
        TickStashWindow();
        if (++_frame % Interval != 0)
            return;
        // (Someone's come into the world: the host's state and storage again, for them.)
        string present = string.Join(",", _session.Players.Where(p => p.State != null).Select(p => p.Slot).OrderBy(s => s));
        bool fresh = present != _present;
        _present = present;
        if (Caravan() is { } caravan)
            Share(false, caravan.ToJson(), ref _state, fresh);
        if (StashJson() is { } stash)
            Share(true, stash, ref _stash, fresh);
    }

    // Ours to send - if it's changed since we last had it (our action changed it), or, the host, to someone new.
    private void Share(bool stash, string json, ref string last, bool fresh)
    {
        bool host = _session.Mode == Session.SessionMode.Host;
        if (json == last && !(host && fresh && _session.Players.Any()))
            return;
        // (A client in the middle of the host's: the host's goes on - ours is only what we were given.)
        if (stash && !host && _stashHolder >= 0 && !_inStash)
            return;
        last = json;
        _session.Send(new CaravanDataPacket(stash, JoinCompression.Compress(json)), host ? Session.Everyone : 0);
    }

    // The storage's four tabs, as one JSON array.
    private static string? StashJson()
    {
        var tabs = new JsonArray();
        for (int i = 0; i < StashLists.Length; i++)
        {
            if (Tab(i) is not { } tab)
                return null;
            tabs.Add(tab.ToJsonNode());
        }
        return tabs.ToJsonString();
    }

    private void ReceiveData(RemotePlayer from, CaravanDataPacket packet)
    {
        bool host = _session.Mode == Session.SessionMode.Host;
        // (The host takes a client's; a client takes only the host's.)
        if (!Ready || (!host && from.Slot != 0))
            return;
        string json = JoinCompression.Decompress(packet.Data);
        if (packet.Stash)
        {
            // (Ours open: what we have is the latest - it goes out as we close.)
            if (_inStash)
                return;
            if (JsonNode.Parse(json) is not JsonArray tabs || tabs.Count != StashLists.Length)
                return;
            for (int i = 0; i < StashLists.Length; i++)
            {
                if (Tab(i) is not { } tab || DsList.FromJson(tabs[i]!.ToJsonString()) is not { } given)
                    continue;
                tab.AssignFrom(given);
                given.Destroy();
            }
            _stash = StashJson() ?? json;
        }
        else
        {
            if (Caravan() is not { } caravan || DsMap.FromJson(json) is not { } given)
                return;
            caravan.AssignFrom(given);
            given.Destroy();
            _state = caravan.ToJson();
        }
        // (The host: what a client changed goes on to the others at its next look, now that it's ours.)
        if (host)
        {
            if (packet.Stash)
                _stash = "";
            else
                _state = "";
        }
    }

    // ---- the storage, one at a time ----

    private void TickStashWindow()
    {
        bool open = Gm.InstanceExists(GameObjectId.o_stash_inventory);
        if (open && !_inStash)
        {
            // Someone else is in it: ours closes.
            if (_stashHolder >= 0)
            {
                foreach (Instance window in Instances.All(GameObjectId.o_stash_inventory))
                    window.Destroy();
                string who = _session.Players.FirstOrDefault(p => p.Slot == _stashHolder)?.Name ?? "Someone";
                Game.CallScript("scr_actionsLogAddMessage", default, $"{who} is using the caravan's storage.");
                return;
            }
            _inStash = true;
            _session.Send(new CaravanStashUsePacket(true));
        }
        else if (!open && _inStash)
        {
            _inStash = false;
            // (What we left in it, to everyone now - then that we're out.)
            if (StashJson() is { } stash)
            {
                _stash = stash;
                _session.Send(new CaravanDataPacket(true, JoinCompression.Compress(stash)),
                    _session.Mode == Session.SessionMode.Host ? Session.Everyone : 0);
            }
            _session.Send(new CaravanStashUsePacket(false));
        }
    }

    private void ReceiveUse(RemotePlayer from, CaravanStashUsePacket packet)
    {
        if (packet.Open)
            _stashHolder = from.Slot;
        else if (_stashHolder == from.Slot)
            _stashHolder = -1;
    }

    // ---- moving ----

    // The caravan's moved: if we're at its old camp - on that tile, outdoors, on the surface - we go with it, to the new
    // camp (the game puts a player arriving "by caravan" at it).
    private void ReceiveMoved(RemotePlayer from, CaravanMovedPacket packet)
    {
        if (_session.Mode != Session.SessionMode.Client || from.Slot != 0 || !Ready || Caravan() is not { } caravan)
            return;
        // (Where it is now, at once: the new tile's camp is built from it as we arrive.)
        caravan["gridX"] = packet.ToX;
        caravan["gridY"] = packet.ToY;
        bool atCamp = Gm.InGame && !Rooms.IsChanging
            && Game.Global["playerGridX"].AsInt == packet.FromX && Game.Global["playerGridY"].AsInt == packet.FromY
            && Game.Global["floor_counter"] is { Kind: GmKind.Real } floor && floor.AsReal == 0
            && Rooms.Current == Game.CallScript("scr_globaltile_get_room", default, packet.FromX, packet.FromY).AsInt;
        if (!atCamp)
            return;
        Game.Global["playerGridX"] = packet.ToX;
        Game.Global["playerGridY"] = packet.ToY;
        Game.Global["position_tag"] = "caravan";
        using GmArray events = GmArray.From(new GmValue[] { 4 });
        Game.CallScript("scr_smoothRoomChange", default, Game.CallScript("scr_globaltile_get_room", default, packet.ToX, packet.ToY), events);
        Game.CallScript("scr_actionsLogAddMessage", default, $"You travel with the caravan.");
        _context.Log($"Caravan moved {packet.FromX},{packet.FromY} -> {packet.ToX},{packet.ToY}: we were at its camp, and go with it");
    }
}
