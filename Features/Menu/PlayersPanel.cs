using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Join;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Menu;

// Who's in the game, at the middle of the main menu's left edge while we're hosting or in someone's game: us, then everyone else by
// slot - their name, the host marked, and under it the world slot each plays (WorldSlots) and, for the host, that slot's
// character (SlotRoster - the host's, sent to everyone: its name and level, or a new character); their ping (the host's to each client; a
// client's to the host). The host can move a client to another world slot - Swap: the next, trading places with whoever
// has it - while they're not in the world yet, and kick one: Kick, then Sure? within a few seconds (the client's told it
// was removed). In the game's hover frame.
// (The settings it once had beside it - name, join address, port, name tags - are on StoneshardMP's page in the Mods
// window; the join address is also asked by Join Game.)
public sealed class PlayersPanel : UIElement
{
    private const double PanelWidth = 280, Pad = 8, HeaderHeight = 16, RowHeight = 32;
    private const double KickWidth = 44, KickHeight = 18, SwapWidth = 44;
    // (How long Kick waits for its Sure?.)
    private const long ConfirmMs = 3000;
    private static readonly int Gold = Draw.Rgb(214, 186, 120), Grey = Draw.Rgb(140, 132, 120),
        PingGood = Draw.Rgb(120, 200, 120), PingFair = Draw.Rgb(220, 190, 90), PingBad = Draw.Rgb(220, 90, 70);

    private readonly Session _session;
    private readonly MpSettings _settings;
    private readonly Func<string> _playerName;
    private readonly WorldSlots _slots;
    private readonly SlotRoster _roster;
    private readonly HostLobby _lobby;
    // Host on the main menu: the save picked, as a line under the header (worked out as it changes).
    private string? _pickedLine;
    private SaveFile? _pickedFor;
    private bool _pickedRead;
    // Host: each client's kick button, and when it was asked Sure? (0: not); and their swap buttons.
    private readonly Dictionary<int, (UIButton Button, long ArmedAt)> _kicks = new();
    private readonly Dictionary<int, UIButton> _swaps = new();
    private bool _stale = true;

    public PlayersPanel(Session session, MpSettings settings, Func<string> playerName, WorldSlots slots, SlotRoster roster, HostLobby lobby)
    {
        _session = session;
        _settings = settings;
        _playerName = playerName;
        _slots = slots;
        _roster = roster;
        _lobby = lobby;
        // (Middle of the left edge: clear of the game's title, the menu and the status under it.)
        Anchor = UIAnchor.Left;
        X = 12;
        Y = 0;
        Width = PanelWidth;
        HitTest = false;
        Visible = false;
        // (Someone came or went, or we started or stopped: the kick buttons made again.)
        _session.Changed += () => _stale = true;
    }

    // Us first, then the others by slot.
    private List<(int Slot, string Name, RemotePlayer? Player)> Rows()
    {
        var rows = new List<(int, string, RemotePlayer?)> { (_session.Slot, _playerName(), null) };
        rows.AddRange(_session.Players.OrderBy(p => p.Slot).Select(p => (p.Slot, p.Name, (RemotePlayer?)p)));
        return rows;
    }

    // Alone as the host: a line saying how to be joined.
    private bool Waiting => _session.Mode == Session.SessionMode.Host && _session.Players.Count == 0;

    protected override void OnUpdate(double deltaTime)
    {
        var rows = Rows();
        UpdatePickedLine();
        Height = Pad * 2 + HeaderHeight + SaveLineHeight + RowHeight * (rows.Count + (Waiting ? 1 : 0));
        if (_stale)
            MakeKickButtons(rows);
        // (Swap only for a client not in the world yet - and ours on the main menu: it's for the save we load next.)
        foreach (var (slot, swap) in _swaps)
            swap.Enabled = slot == _session.Slot ? Gm.InMainMenu : _slots.CanSwap(slot);
        // (A Sure? not taken up: back to Kick.)
        long now = Environment.TickCount64;
        foreach (var (slot, kick) in _kicks.ToList())
            if (kick.ArmedAt != 0 && now - kick.ArmedAt > ConfirmMs)
            {
                kick.Button.Text = "Kick";
                _kicks[slot] = (kick.Button, 0);
            }
    }

    // Host: a kick button on each client's row.
    private void MakeKickButtons(List<(int Slot, string Name, RemotePlayer? Player)> rows)
    {
        _stale = false;
        foreach (var (button, _) in _kicks.Values)
            Remove(button);
        _kicks.Clear();
        foreach (var button in _swaps.Values)
            Remove(button);
        _swaps.Clear();
        if (_session.Mode != Session.SessionMode.Host)
            return;
        for (int i = 0; i < rows.Count; i++)
        {
            var (slot, name, player) = rows[i];
            if (player == null)
            {
                // Ours: which slot's character we'll play in the save we load.
                var mine = Add(new UIButton("Swap", Width - Pad - SwapWidth, RowTop(i) + (RowHeight - KickHeight) / 2, SwapWidth, KickHeight)
                {
                    Tooltip = "Play another player slot's character: the next save you load trades yours with that slot's "
                        + "(and your stashes), so whoever plays that slot gets yours. On the main menu, before you load.",
                });
                mine.Clicked += _ => _slots.SwapHost();
                _swaps[slot] = mine;
                continue;
            }
            var button = Add(new UIButton("Kick", Width - Pad - KickWidth, RowTop(i) + (RowHeight - KickHeight) / 2, KickWidth, KickHeight)
            {
                Tooltip = $"Remove {name} from the game",
            });
            button.Clicked += _ => Kick(slot, name);
            _kicks[slot] = (button, 0);
            var swap = Add(new UIButton("Swap", Width - Pad - KickWidth - 4 - SwapWidth, RowTop(i) + (RowHeight - KickHeight) / 2, SwapWidth, KickHeight)
            {
                Tooltip = $"Move {name} to the next player slot of the world - its character, and its stash - trading places "
                    + "with whoever has it. Only before they're in the world.",
            });
            swap.Clicked += _ => _slots.Swap(slot);
            _swaps[slot] = swap;
        }
    }

    // Kick, then Sure? to do it.
    private void Kick(int slot, string name)
    {
        if (!_kicks.TryGetValue(slot, out var kick))
            return;
        if (kick.ArmedAt == 0)
        {
            kick.Button.Text = "Sure?";
            _kicks[slot] = (kick.Button, Environment.TickCount64);
            return;
        }
        _session.Kick(slot);
    }

    private double RowTop(int row) => Pad + HeaderHeight + SaveLineHeight + row * RowHeight;

    // (The picked save's line: the host's, on the main menu.)
    private double SaveLineHeight => _pickedLine != null ? 16 : 0;

    private void UpdatePickedLine()
    {
        if (_session.Mode != Session.SessionMode.Host || !Gm.InMainMenu)
        {
            _pickedLine = null;
            return;
        }
        var picked = _lobby.Picked;
        if (_pickedRead && Equals(picked, _pickedFor))
            return;
        _pickedRead = true;
        _pickedFor = picked;
        _stale = true;
        _pickedLine = picked == null ? "Pick a save: Continue or Load Game, then Play"
            : "Save: " + (picked.Info?.CharacterName ?? picked.Slot.Name) + " - choose slots, then Play";
    }

    protected override void OnDraw(double x, double y)
    {
        Draw.Frame(x, y, Width, Height);
        var rows = Rows();
        int count = rows.Count;
        string limit = _session.Mode == Session.SessionMode.Host ? $"/{_session.Limit}" : "";
        Draw.Text(x + Pad, y + Pad, "Players", Gold);
        Draw.Text(x + Width - Pad, y + Pad, $"{count}{limit}", Grey, Draw.AlignRight);
        if (_pickedLine != null)
            Draw.Text(x + Pad, y + Pad + HeaderHeight, _pickedLine, Grey);
        for (int i = 0; i < count; i++)
        {
            var (slot, name, player) = rows[i];
            double top = y + RowTop(i), middle = top + 10, under = top + 23;
            if (i > 0)
                Draw.Rectangle(x + Pad, top, x + Width - Pad, top, Draw.Rgb(60, 54, 70), 0.8);
            string shown = name.Length > 16 ? name[..15] + "." : name;
            string tags = string.Join(", ", new[]
            {
                slot == 0 ? "host" : null,
                player == null ? "you" : null,
            }.Where(tag => tag != null));
            // (Under it: the slot it plays - and, for the host, who that is - clipped short of the buttons.)
            if (_roster.Of(slot) is var (world, who))
            {
                string character = who.Length > 0 ? $"slot {world}: {who}" : $"slot {world}";
                double room = Width - Pad * 2 - (_swaps.ContainsKey(slot) ? (_kicks.ContainsKey(slot) ? KickWidth + SwapWidth + 10 : SwapWidth + 6) : 0);
                while (character.Length > 4 && Draw.TextWidth(character) > room)
                    character = character[..^2] + ".";
                Draw.Text(x + Pad, under, character, Grey, Draw.AlignLeft, Draw.AlignMiddle);
            }
            Draw.Text(x + Pad, middle, shown, player == null ? Gold : Draw.White, Draw.AlignLeft, Draw.AlignMiddle);
            if (tags.Length > 0)
                Draw.Text(x + Pad + Draw.TextWidth(shown) + 5, middle, tags, Grey, Draw.AlignLeft, Draw.AlignMiddle);
            if (player == null)
                continue;
            // Their ping: left of the kick button for the host.
            double pingRight = x + Width - Pad - (_kicks.ContainsKey(slot) ? KickWidth + SwapWidth + 10 : 0);
            if (player.Ping >= 0)
                Draw.Text(pingRight, middle, $"{player.Ping} ms", player.Ping < 80 ? PingGood : player.Ping < 200 ? PingFair : PingBad,
                    Draw.AlignRight, Draw.AlignMiddle);
        }
        if (Waiting)
            Draw.Text(x + Pad, y + RowTop(count) + RowHeight / 2,
                $"Waiting for players - UDP port {_settings.Port.Value:0}", Grey, Draw.AlignLeft, Draw.AlignMiddle);
    }
}
