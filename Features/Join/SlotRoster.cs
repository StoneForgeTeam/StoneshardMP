using System;
using System.Collections.Generic;
using System.Linq;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Join;

// Who plays which of the world's player slots, and who that is, for every game in the session: the host works it out -
// each player's world slot (WorldSlots; its own, the one it'll play), and that slot's character (SlotCharacters) - and
// sends it to everyone when it changes (SlotsPacket); the clients keep what it last sent. For the main menu's player
// list (PlayersPanel).
public sealed class SlotRoster
{
    private readonly Session _session;
    private readonly WorldSlots _slots;
    private readonly SlotCharacters _characters;
    // By session slot: the world slot played, and its character.
    private readonly Dictionary<int, (int World, string Character)> _rows = new();
    private string _sent = "";

    public SlotRoster(Session session, WorldSlots slots, HostLobby lobby)
    {
        _session = session;
        _slots = slots;
        _characters = new SlotCharacters(() => lobby.Picked);
        session.On<SlotsPacket>(Receive);
        // (Someone new: everyone gets it again, them too.)
        session.PlayerJoined += _ => _sent = "";
        session.Changed += () =>
        {
            if (!session.Connected)
                Clear();
        };
    }

    /// <summary>A player's world slot and its character, by their session slot; null if it isn't known (yet).</summary>
    public (int World, string Character)? Of(int sessionSlot) => _rows.TryGetValue(sessionSlot, out var row) ? row : null;

    public void Clear()
    {
        _rows.Clear();
        _sent = "";
    }

    // Each frame: the host's, worked out again - and sent if it's changed.
    public void Tick()
    {
        if (_session.Mode != Session.SessionMode.Host)
            return;
        _characters.Refresh();
        _rows.Clear();
        _rows[_session.Slot] = (_slots.HostSlot, _characters.Label(_slots.HostSlot));
        foreach (var player in _session.Players)
        {
            int world = _slots.Of(player.Slot);
            _rows[player.Slot] = (world, _characters.Label(world, player.Name));
        }
        string rows = string.Join("\n", _rows.OrderBy(r => r.Key).Select(r => $"{r.Key}|{r.Value.World}|{r.Value.Character}"));
        if (rows == _sent || !_session.Players.Any())
            return;
        _sent = rows;
        _session.Send(new SlotsPacket(rows));
    }

    private void Receive(RemotePlayer from, SlotsPacket packet)
    {
        if (_session.Mode != Session.SessionMode.Client || from.Slot != 0)
            return;
        _rows.Clear();
        foreach (string line in packet.Rows.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|', 3);
            if (parts.Length == 3 && int.TryParse(parts[0], out int session) && int.TryParse(parts[1], out int world))
                _rows[session] = (world, parts[2]);
        }
    }
}
