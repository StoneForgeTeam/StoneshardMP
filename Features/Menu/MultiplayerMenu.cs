using System;
using StoneForge;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Menu;

// The Multiplayer screens of the main menu, made of its own buttons (StoneForge's MainMenu), as the game's Play screen
// is - one for each state of the session, switched as it changes (the host leaving, a connection lost...):
// - Multiplayer (after Play): Host Game, Join Game (its dialog: JoinDialog), Players & Settings (MultiplayerWindow), Back.
// - Hosting: the game's Continue and Load Game, New Game (straight into the Adventure), and Stop Hosting.
// - Joined: Players & Settings and Leave Game only - a client plays the host's world, launched into it (or into making
//   a character for it) when the host is in it (JoinManager).
// The session's status shows under the menu meanwhile.
public sealed class MultiplayerMenu
{
    private enum Screen { None, Multiplayer, Hosting, Joined }

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly MpSettings _settings;
    private readonly Func<string> _playerName;
    private readonly JoinDialog _join;
    private readonly MultiplayerWindow _window;
    private readonly UILabel _status;
    private readonly Func<string?> _joinStatus;
    private Screen _shown;

    public MultiplayerMenu(ModContext context, Session session, MpSettings settings, Func<string> playerName,
        JoinDialog join, MultiplayerWindow window, Func<string?> joinStatus)
    {
        _joinStatus = joinStatus;
        _context = context;
        _session = session;
        _settings = settings;
        _playerName = playerName;
        _join = join;
        _window = window;
        // (Under the menu: the session's status, while the Multiplayer screens show or a session's on.)
        _status = context.UI.MainMenu.Add(new UILabel("", 0, 14) { Anchor = UIAnchor.Bottom, Width = 400, Align = Draw.AlignCenter, Visible = false });
        MainMenu.AddAfter(context, VanillaButton.Play, "Multiplayer", () => Show(ScreenFor(_session.Mode)));
        session.Changed += Follow;
    }

    // Each frame: the status line.
    public void Tick()
    {
        _status.Visible = _shown != Screen.None || _session.Mode != Session.SessionMode.Idle;
        _status.Text = (_session.Mode == Session.SessionMode.Client ? _joinStatus() : null) ?? _session.Status;
        _status.Colour = _session.Connected ? Draw.Rgb(120, 200, 120) : Draw.Muted;
    }

    // The session changed: the screen showing follows it (hosting started or stopped, joined, dropped).
    private void Follow()
    {
        if (_shown == Screen.None)
            return;
        var screen = ScreenFor(_session.Mode);
        if (screen != _shown)
            Show(screen);
    }

    // (Connecting counts as not joined yet: the Multiplayer screen, with the status.)
    private Screen ScreenFor(Session.SessionMode mode) => mode switch
    {
        Session.SessionMode.Host => Screen.Hosting,
        Session.SessionMode.Client when _session.Connected => Screen.Joined,
        _ => Screen.Multiplayer,
    };

    // A screen of the main menu, in place of the one showing (the main list under them: the game's Back, from the
    // Multiplayer screen, goes back to it).
    private void Show(Screen screen)
    {
        // (One screen deep: what an earlier one did undone first.)
        MainMenu.RestoreButtons(_context);
        _shown = screen;
        MainMenu.ClearButtons(_context);
        switch (screen)
        {
            case Screen.Multiplayer:
                MainMenu.AddButton(_context, "Host Game", Host);
                MainMenu.AddButton(_context, "Join Game", _join.Open);
                MainMenu.AddButton(_context, "Players & Settings", _window.Open);
                MainMenu.AddButton(_context, "Back", Close);
                break;
            case Screen.Hosting:
                MainMenu.AddButton(_context, VanillaButton.Continue);
                // (A shared world: straight into the Adventure, no permadeath - the prologue is a world of its own. The
                // players waiting make their characters alongside: JoinManager.)
                MainMenu.AddButton(_context, "New Game", Gml.MpJoinStartNew);
                MainMenu.AddButton(_context, VanillaButton.LoadGame);
                MainMenu.AddButton(_context, "Players & Settings", _window.Open);
                MainMenu.AddButton(_context, "Stop Hosting", () => _session.Stop("Stopped hosting"));
                break;
            case Screen.Joined:
                MainMenu.AddButton(_context, "Players & Settings", _window.Open);
                MainMenu.AddButton(_context, "Leave Game", () => _session.Stop("Left the game"));
                break;
        }
    }

    // Back to the main list.
    private void Close()
    {
        _shown = Screen.None;
        MainMenu.RestoreButtons(_context);
    }

    private void Host() => _session.Host((int)_settings.Port.Value, _playerName(), (int)_settings.MaxPlayers.Value);
}
