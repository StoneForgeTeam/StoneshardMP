using System;
using System.Linq;
using StoneForge;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Menu;

// Players & Settings (the Multiplayer screens' button): who's in the game, and our multiplayer settings - our name,
// the join address, the port and player limit we host with, name tags (also on the mod's page in the Mods window).
// Hosting and joining are the Multiplayer screens' (MultiplayerMenu). In the game's journal frame, laid out as the
// journal is: tabs in its left pane, the page and buttons in its right.
public sealed class MultiplayerWindow : UIWindow
{
    private static readonly int Good = Draw.Rgb(120, 200, 120);
    // The journal's panes, from its frame's corner (o_journal: left 7,29 135x277; right 153,29 409x277).
    private const double PaneTop = 29, LeftX = 7, LeftWidth = 135, RightX = 153, RightWidth = 409, PaneHeight = 277;

    private readonly Session _session;
    private readonly MpSettings _settings;
    private readonly Func<string> _defaultName;
    private UITabStrip _tabs = null!;
    private UIScrollArea _page = null!;
    private UILabel? _status;

    public MultiplayerWindow(Session session, MpSettings settings, Func<string> defaultName) : base("Multiplayer")
    {
        _session = session;
        _settings = settings;
        _defaultName = defaultName;
        _session.Changed += Refresh;
    }

    // Our name to the others: the setting, or the Steam name.
    public string PlayerName => _settings.Name.Value.Trim() is { Length: > 0 } name ? name : _defaultName();

    // Content: from the left pane's corner to the right pane's far corner.
    protected override void OnFit()
        => ContentInsets = new UIInsets(LeftX, PaneTop, Math.Max(0, Frame.Width - RightX - RightWidth), Math.Max(0, Frame.Height - PaneTop - PaneHeight));

    protected override void OnOpen()
    {
        double right = RightX - LeftX;
        _tabs = Content.Add(new UITabStrip(0, 0, LeftWidth, PaneHeight));
        _tabs.TabOpened += ShowTab;
        _page = Content.Add(new UIScrollArea(right, 0, RightWidth, PaneHeight - 32));
        var buttons = Content.Add(new UIButtonRow(right, PaneHeight - 26, RightWidth) { Align = Draw.AlignRight });
        buttons.Add("Close", Close);
        _tabs.SetTabs("Players", "Settings");
        _tabs.Tabs[0].Open();
    }

    private void ShowTab(UITab tab)
    {
        _page.Clear();
        _page.AddHeader(tab.Text);
        _status = _page.AddText(_session.Status, _session.Connected ? Good : Draw.Muted);
        if (tab.Index == 0)
        {
            if (!_session.Connected)
                _page.AddText("Not in a game: host or join one from the Multiplayer menu.", Draw.Muted);
            else
            {
                _page.AddText($"You: {PlayerName} (player {_session.Slot + 1}{(_session.Mode == Session.SessionMode.Host ? ", host" : "")})");
                foreach (var player in _session.Players.OrderBy(p => p.Slot))
                    _page.AddText($"{player.Name} - player {player.Slot + 1}{(player.Slot == 0 ? ", host" : "")}, StoneshardMP {player.Version}"
                        + (player.Ping >= 0 ? $", ping {player.Ping} ms" : ""));
            }
            return;
        }
        _page.AddText("Name (empty: your Steam name)", Draw.Muted);
        var name = _page.Add(new UITextBox(5, 0, 200, _settings.Name.Value, _defaultName()) { MaxLength = 32 });
        name.TextChanged += text => _settings.Name.Value = text;
        _page.AddText("Host's address, to join", Draw.Muted);
        var address = _page.Add(new UITextBox(5, 0, 200, _settings.JoinAddress.Value, "127.0.0.1") { MaxLength = 64 });
        address.TextChanged += text => _settings.JoinAddress.Value = text;
        var names = _page.AddCheckbox(_settings.ShowNames.Label, _settings.ShowNames.Value, _settings.ShowNames.Tooltip);
        names.Changed += on => _settings.ShowNames.Value = on;
        _page.AddText($"Port {_settings.Port.Value:0} (UDP), up to {_settings.MaxPlayers.Value:0} players when hosting - on StoneshardMP's page in the Mods window.", Draw.Muted);
    }

    // The session changed: the status line, and the players list if it's showing (the Settings page keeps its text
    // boxes as they are).
    private void Refresh()
    {
        if (!IsOpen)
            return;
        if (_tabs.Selected is { Index: 0 } players)
            ShowTab(players);
        else if (_status != null)
        {
            _status.Text = _session.Status;
            _status.Colour = _session.Connected ? Good : Draw.Muted;
        }
    }
}
