using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using StoneForge;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

// The game script whose "dropped" log line marks the player's own drops (the patcher makes it hookable).
[assembly: HookScript(nameof(Scripts.scr_actionsLogItem))]

namespace StoneshardMP;

// Live ground loot where players are together (legacy StoneshardMP's loot sync). The host owns a place it shares
// with clients - its loot is the real one - and they follow:
// - The owner sends a snapshot of all its loot when a follower arrives (or asks), then every few frames what changed:
//   new loot once it has landed, loot that left (picked up by anyone).
// - A follower takes it: twins from the same save are bound, the rest made from the owner's data, anything else
//   removed. Its own pickups go to the owner, which removes them too. Its own drops go to the owner, which makes
//   them; they come back in its changes, and the follower's own copy becomes the one synced item.
// - Throws are shared: loot goes out the moment it appears, with its throw if it's in the air, and the other game flies
//   its copy along the same arc to the same tile (MpLootFlight / MpLootFly).
// Loot sync shares no state with a place nobody else is in: there, WorldSync shares it as the location's save.
public sealed class LootSync
{
    // (Every this many frames - often, so a throw is caught early in its arc; a follower asks again for a snapshot
    // after this many checks without one; the first milliseconds after one, loot still turning up is the area loading;
    // a drop window after the "dropped" line; a follower's drop the owner hasn't brought back by then is removed; at
    // most this many of a follower's drops a second - against a runaway loop.)
    private const int Interval = 4;
    private const int SnapshotWait = 15;
    private const long SettleMs = 3000;
    private const long DropWindowMs = 1500;
    private const int DropPendingMs = 5000;
    private const int DropsPerSecond = 10;

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    private int _frame;
    private long _dropUntil;
    // Our place, and our role there.
    private string? _place;
    private bool _owning, _following;
    // Owner: the followers here (a new one gets a snapshot), and a snapshot owed to everyone.
    private readonly HashSet<int> _followers = new();
    private bool _snapshotOwed;
    private long _dropWindowStart;
    private int _dropCount;
    // Follower: whether we have the owner's snapshot (and when), and checks without one.
    private bool _synced;
    private long _syncedAt;
    private int _waiting;

    public LootSync(ModContext context, Session session, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _inSharedWorld = inSharedWorld;
        session.On<LootPacket>(Receive);
        Scripts.scr_actionsLogItem.Before(context, call =>
        {
            if (call.Args.Length > 0 && call.Args[0].AsString == "playerDropItems")
                _dropUntil = Environment.TickCount64 + DropWindowMs;
            return false;
        });
    }

    public void Clear()
    {
        _place = null;
        _owning = _following = false;
        _followers.Clear();
        _synced = false;
    }

    // Each frame.
    public void Tick()
    {
        if (++_frame % Interval != 0)
            return;
        string? place = null;
        if (_session.Connected && Gm.InGame && _inSharedWorld() && !Gm.InstanceExists(GameObjectId.o_smoothRoomChanger))
            place = PlayerState.Parse(Gml.MpPlayerState())?.Place;
        // Who's here with us: the host owns, clients in its place follow.
        var here = place == null ? new List<RemotePlayer>() : _session.Players.Where(p => p.State?.Place == place).ToList();
        bool owning = _session.Mode == Session.SessionMode.Host && here.Count > 0;
        bool following = _session.Mode == Session.SessionMode.Client && here.Any(p => p.Slot == 0);
        if (place != _place || owning != _owning || following != _following)
        {
            // A new place or role: the tables start over (and an owner's snapshot goes out).
            _place = place;
            _owning = owning;
            _following = following;
            _followers.Clear();
            _synced = false;
            _waiting = 0;
            _snapshotOwed = true;
            if (place != null)
                Gml.MpLootReset();
        }
        if (_owning)
            OwnerTick(here);
        else if (_following)
            FollowerTick();
    }

    private void OwnerTick(List<RemotePlayer> here)
    {
        // Someone new here: everyone gets a snapshot (one list of ids for all).
        foreach (var player in here)
            if (_followers.Add(player.Slot))
                _snapshotOwed = true;
        _followers.IntersectWith(here.Select(p => p.Slot));
        if (_snapshotOwed)
        {
            _snapshotOwed = false;
            string snapshot = Gml.MpLootOwnerSnapshot();
            SendToFollowers(LootKind.Snapshot, snapshot);
            return;
        }
        string changes = Gml.MpLootOwnerDiff();
        if (changes.Length > 0)
            SendToFollowers(LootKind.Changes, changes);
    }

    private void FollowerTick()
    {
        if (!_synced)
        {
            // No snapshot for here yet (just arrived, or it was missed): after a while, ask for one.
            if (++_waiting >= SnapshotWait)
            {
                _waiting = 0;
                Send(LootKind.SnapshotRequest, "", 0);
            }
            return;
        }
        long now = Environment.TickCount64;
        string report = Gml.MpLootFollowerStep(now <= _dropUntil, now - _syncedAt < SettleMs, DropPendingMs);
        if (report.Length == 0)
            return;
        using var doc = JsonDocument.Parse(report);
        var taken = doc.RootElement.GetProperty("taken");
        if (taken.GetArrayLength() > 0)
            Send(LootKind.Taken, taken.GetRawText(), 0);
        foreach (var drop in doc.RootElement.GetProperty("drops").EnumerateArray())
            Send(LootKind.Dropped, drop.GetRawText(), 0);
    }

    private void SendToFollowers(LootKind kind, string json)
    {
        foreach (int slot in _followers)
            Send(kind, json, slot);
    }

    private void Send(LootKind kind, string json, int to)
        => _session.Send(new LootPacket(kind, _place ?? "", JoinCompression.Compress(json)), to);

    private void Receive(RemotePlayer sender, LootPacket packet)
    {
        // Only for the place we're in, in the role it's meant for (one sent just before either of us moved on is
        // dropped).
        if (packet.Place != _place)
            return;
        string json = JoinCompression.Decompress(packet.Data);
        switch (packet.Kind)
        {
            case LootKind.Snapshot when _following && sender.Slot == 0:
                string result = Gml.MpLootFollowerSnapshot(json);
                _synced = true;
                _syncedAt = Environment.TickCount64;
                _waiting = 0;
                _context.Log($"Ground loot here from {sender.Name}: {result}");
                break;
            case LootKind.Changes when _following && sender.Slot == 0 && _synced:
                Gml.MpLootFollowerDiff(json);
                break;
            case LootKind.SnapshotRequest when _owning:
                _snapshotOwed = true;
                break;
            case LootKind.Taken when _owning:
                Gml.MpLootOwnerTaken(json);
                break;
            case LootKind.Dropped when _owning:
                long now = Environment.TickCount64;
                if (now - _dropWindowStart >= 1000)
                {
                    _dropWindowStart = now;
                    _dropCount = 0;
                }
                if (++_dropCount <= DropsPerSecond)
                    Gml.MpLootOwnerDrop(json);
                else
                    _context.Log($"Ignored a drop from {sender.Name}: more than {DropsPerSecond} a second");
                break;
        }
    }
}
