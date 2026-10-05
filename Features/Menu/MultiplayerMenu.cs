using System;
using StoneForge;
using StoneshardMP.Features.Join;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Menu;

// The Multiplayer screens of the main menu, made of its own buttons (StoneForge's MainMenu), as the game's Play screen
// is - one for each state of the session, switched as it changes (the host leaving, a connection lost...):
// - Multiplayer (after Play): Host Game, Join Game (its dialog: JoinDialog), Back.
// - Hosting: Play (once a save's picked), Continue and Load Game - which pick a save, not go into it (HostLobby): the
//   players choose their slots first -, New Game (straight into the Adventure), and Stop Hosting.
// - Joined: Leave Game only - a client plays the host's world, launched into it (or into making a character for it)
//   when the host is in it (JoinManager).
// The session's status shows under the menu meanwhile, and who's in the game at its left (PlayersPanel - the host
// can kick from it). The multiplayer settings are on StoneshardMP's page in the Mods window.
public sealed class MultiplayerMenu
{
    private enum Screen { None, Multiplayer, Hosting, Joined }

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly MpSettings _settings;
    private readonly Func<string> _playerName;
    private readonly JoinDialog _join;
    private readonly PlayersPanel _players;
    private readonly UILabel _status;
    private readonly Func<string?> _joinStatus;
    private readonly HostLobby _lobby;
    private Screen _shown;
    // (The picked save changed: the Hosting screen made again - from Tick, not inside the game's save menu. And whether the
    // save menu was open last frame.)
    private bool _remake, _saveMenuWasOpen;

    public MultiplayerMenu(ModContext context, Session session, MpSettings settings, Func<string> playerName,
        JoinDialog join, Func<string?> joinStatus, WorldSlots slots, SlotRoster roster, HostLobby lobby)
    {
        _lobby = lobby;
        lobby.Changed += () => _remake = true;
        _joinStatus = joinStatus;
        _context = context;
        _session = session;
        _settings = settings;
        _playerName = playerName;
        _join = join;
        _players = context.UI.MainMenu.Add(new PlayersPanel(session, settings, playerName, slots, roster, lobby));
        // (Under the menu: the session's status, while the Multiplayer screens show or a session's on.)
        _status = context.UI.MainMenu.Add(new UILabel("", 0, 14) { Anchor = UIAnchor.Bottom, Width = 400, Align = Draw.AlignCenter, Visible = false });
        MainMenu.AddAfter(context, VanillaButton.Play, "Multiplayer", () => Show(ScreenFor(_session.Mode)));
        session.Changed += Follow;
    }

    // Each frame: the status line.
    public void Tick()
    {
        // The save menu shut while we host (a save picked, or Back): the game goes back to its own play screen (New Game,
        // Load Game, Back - the nav's user event 1, which mods' changes aren't on); back to the main list, our Hosting
        // screen, instead (its user event 0).
        bool saveMenuOpen = Gm.InMainMenu && Gm.InstanceExists(GameObjectId.o_saveMenu);
        if (_saveMenuWasOpen && !saveMenuOpen && Gm.InMainMenu && _session.Mode == Session.SessionMode.Host)
        {
            foreach (Instance nav in Instances.All(GameObjectId.o_mainMenuNavContainer))
                if (nav.Get("active").AsBool)
                    Game.CallBuiltinAs("event_user", nav, nav, 0);
            _remake = true;
        }
        _saveMenuWasOpen = saveMenuOpen;
        if (_remake && Gm.InMainMenu && !saveMenuOpen)
        {
            _remake = false;
            // (Hosting: its screen made again, Play on it now. Hosting but showing another: to it.)
            if (_session.Mode == Session.SessionMode.Host)
            {
                _context.Log($"Lobby: Hosting screen made again ({(_lobby.Picked is { } picked ? "Play for " + picked.Name : "nothing picked")}; it was {_shown})");
                Show(Screen.Hosting);
            }
        }
        _status.Visible = _shown != Screen.None || _session.Mode != Session.SessionMode.Idle;
        _status.Text = (_session.Mode == Session.SessionMode.Client ? _joinStatus() : null) ?? _session.Status;
        _status.Colour = _session.Connected ? Draw.Rgb(120, 200, 120) : Draw.Muted;
        _players.Visible = _session.Connected;
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
        // (One screen deep: what an earlier one did undone first - ours only: other mods' and the loader's buttons stay.)
        MainMenu.UndoChanges(_context);
        _shown = screen;
        MainMenu.ClearButtons(_context);
        switch (screen)
        {
            case Screen.Multiplayer:
                MainMenu.AddButton(_context, "Host Game", Host);
                MainMenu.AddButton(_context, "Join Game", _join.Open);
                MainMenu.AddButton(_context, "Back", Close);
                break;
            case Screen.Hosting:
                // (A save picked: Play goes into it, everyone's slots as chosen. Continue picks the last save played.)
                if (_lobby.Picked != null)
                    MainMenu.AddButton(_context, "Play", () => _lobby.Play());
                if (SaveSlots.CurrentSave != null)
                    MainMenu.AddButton(_context, "Continue", _lobby.PickLast);
                // (A shared world: straight into the Adventure, no permadeath - the prologue is a world of its own. The
                // players waiting make their characters alongside: JoinManager.)
                MainMenu.AddButton(_context, "New Game", JoinSave.StartNew);
                MainMenu.AddButton(_context, VanillaButton.LoadGame);
                MainMenu.AddButton(_context, "Stop Hosting", () => _session.Stop("Stopped hosting"));
                break;
            case Screen.Joined:
                MainMenu.AddButton(_context, "Leave Game", () => _session.Stop("Left the game"));
                break;
        }
    }

    // Back to the main list.
    private void Close()
    {
        _shown = Screen.None;
        MainMenu.UndoChanges(_context);
    }

    private void Host() => _session.Host((int)_settings.Port.Value, _playerName(), (int)_settings.MaxPlayers.Value);
}
