using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Join;

/// <summary>A player's row in the lobby: the world slot they play, whether there's a save to say who that is, and its
/// character (null: a new character).</summary>
public sealed record SlotRow(int World, bool Known, SlotCharacter? Character);

// Who plays which of the world's player slots, and who that is, for every game in the session: the host works it out -
// each player's world slot (WorldSlots; its own, the one it'll play), and that slot's character (SlotCharacters) - and
// sends it to everyone when it changes (SlotsPacket); the clients keep what it last sent. For the main menu's lobby
// (PlayersPanel).
public sealed class SlotRoster
{
    private readonly Session _session;
    private readonly WorldSlots _slots;
    private readonly SlotCharacters _characters;
    // By session slot.
    private readonly Dictionary<int, SlotRow> _rows = new();
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

    /// <summary>Host: a world slot's character (null: none - a new character - or no save to say), for the lobby's
    /// choice of slot.</summary>
    public SlotCharacter? CharacterOf(int worldSlot) => _characters.Of(worldSlot);

    /// <summary>Host: whether there's a save to say who the slots are.</summary>
    public bool Known => _characters.Known;

    /// <summary>A player's row, by their session slot; null if it isn't known (yet).</summary>
    public SlotRow? Of(int sessionSlot) => _rows.TryGetValue(sessionSlot, out var row) ? row : null;

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
        _rows[_session.Slot] = new SlotRow(_slots.HostSlot, _characters.Known, _characters.Of(_slots.HostSlot));
        foreach (var player in _session.Players)
        {
            // (The slot whose character they'll play: 0 - the host's - when the host's taking theirs.)
            int world = _slots.Plays(player.Slot);
            _rows[player.Slot] = new SlotRow(world, _characters.Known, _characters.Of(world, world == 0 ? null : player.Name));
        }
        string rows = string.Join("\n", _rows.OrderBy(r => r.Key).Select(r => Line(r.Key, r.Value)));
        if (rows == _sent || !_session.Players.Any())
            return;
        _sent = rows;
        _session.Send(new SlotsPacket(rows));
    }

    private static string Line(int session, SlotRow row)
    {
        var c = row.Character;
        return string.Join("|", session, row.World, row.Known ? 1 : 0, Clean(c?.Name), c?.Level ?? 0, Clean(c?.Class), Clean(c?.Avatar));
    }

    // (No field may break the line.)
    private static string Clean(string? text) => (text ?? "").Replace("|", "/").Replace("\n", " ");

    private void Receive(RemotePlayer from, SlotsPacket packet)
    {
        if (_session.Mode != Session.SessionMode.Client || from.Slot != 0)
            return;
        _rows.Clear();
        foreach (string line in packet.Rows.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('|');
            if (p.Length != 7 || !int.TryParse(p[0], out int session) || !int.TryParse(p[1], out int world))
                continue;
            int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level);
            var character = p[3].Length > 0 ? new SlotCharacter(p[3], level, p[5], p[6]) : null;
            _rows[session] = new SlotRow(world, p[2] == "1", character);
        }
    }
}
