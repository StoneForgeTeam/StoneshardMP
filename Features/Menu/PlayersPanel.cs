using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Menu;

// Who's in the game, at the middle of the main menu's left edge while we're hosting or in someone's game: us, then everyone else by
// slot - their name, the host marked, their ping (the host's to each client; a client's to the host). The host can
// kick a client: Kick, then Sure? within a few seconds (the client's told it was removed). In the game's hover frame.
// (The settings it once had beside it - name, join address, port, name tags - are on StoneshardMP's page in the Mods
// window; the join address is also asked by Join Game.)
public sealed class PlayersPanel : UIElement
{
    private const double PanelWidth = 230, Pad = 8, HeaderHeight = 16, RowHeight = 20;
    private const double KickWidth = 44, KickHeight = 18;
    // (How long Kick waits for its Sure?.)
    private const long ConfirmMs = 3000;
    private static readonly int Gold = Draw.Rgb(214, 186, 120), Grey = Draw.Rgb(140, 132, 120),
        PingGood = Draw.Rgb(120, 200, 120), PingFair = Draw.Rgb(220, 190, 90), PingBad = Draw.Rgb(220, 90, 70);

    private readonly Session _session;
    private readonly MpSettings _settings;
    private readonly Func<string> _playerName;
    // Host: each client's kick button, and when it was asked Sure? (0: not).
    private readonly Dictionary<int, (UIButton Button, long ArmedAt)> _kicks = new();
    private bool _stale = true;

    public PlayersPanel(Session session, MpSettings settings, Func<string> playerName)
    {
        _session = session;
        _settings = settings;
        _playerName = playerName;
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
        Height = Pad * 2 + HeaderHeight + RowHeight * (rows.Count + (Waiting ? 1 : 0));
        if (_stale)
            MakeKickButtons(rows);
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
        if (_session.Mode != Session.SessionMode.Host)
            return;
        for (int i = 0; i < rows.Count; i++)
        {
            var (slot, name, player) = rows[i];
            if (player == null)
                continue;
            var button = Add(new UIButton("Kick", Width - Pad - KickWidth, RowTop(i) + (RowHeight - KickHeight) / 2, KickWidth, KickHeight)
            {
                Tooltip = $"Remove {name} from the game",
            });
            button.Clicked += _ => Kick(slot, name);
            _kicks[slot] = (button, 0);
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

    private static double RowTop(int row) => Pad + HeaderHeight + row * RowHeight;

    protected override void OnDraw(double x, double y)
    {
        Draw.Frame(x, y, Width, Height);
        var rows = Rows();
        int count = rows.Count;
        string limit = _session.Mode == Session.SessionMode.Host ? $"/{_session.Limit}" : "";
        Draw.Text(x + Pad, y + Pad, "Players", Gold);
        Draw.Text(x + Width - Pad, y + Pad, $"{count}{limit}", Grey, Draw.AlignRight);
        for (int i = 0; i < count; i++)
        {
            var (slot, name, player) = rows[i];
            double top = y + RowTop(i), middle = top + RowHeight / 2;
            if (i > 0)
                Draw.Rectangle(x + Pad, top, x + Width - Pad, top, Draw.Rgb(60, 54, 70), 0.8);
            string shown = name.Length > 16 ? name[..15] + "." : name;
            string tags = (slot == 0 ? "host" : "") + (player == null ? (slot == 0 ? ", you" : "you") : "");
            Draw.Text(x + Pad, middle, shown, player == null ? Gold : Draw.White, Draw.AlignLeft, Draw.AlignMiddle);
            if (tags.Length > 0)
                Draw.Text(x + Pad + Draw.TextWidth(shown) + 5, middle, tags, Grey, Draw.AlignLeft, Draw.AlignMiddle);
            if (player == null)
                continue;
            // Their ping: left of the kick button for the host.
            double pingRight = x + Width - Pad - (_kicks.ContainsKey(slot) ? KickWidth + 6 : 0);
            if (player.Ping >= 0)
                Draw.Text(pingRight, middle, $"{player.Ping} ms", player.Ping < 80 ? PingGood : player.Ping < 200 ? PingFair : PingBad,
                    Draw.AlignRight, Draw.AlignMiddle);
        }
        if (Waiting)
            Draw.Text(x + Pad, y + RowTop(count) + RowHeight / 2,
                $"Waiting for players - UDP port {_settings.Port.Value:0}", Grey, Draw.AlignLeft, Draw.AlignMiddle);
    }
}
