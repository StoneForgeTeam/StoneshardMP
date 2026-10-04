using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.World;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

// The game scripts joining replaces or skips (the patcher makes them hookable).
[assembly: HookScript(nameof(Scripts.scr_slotLoad))]
[assembly: HookScript(nameof(Scripts.scr_slotUpdate))]
[assembly: HookScript(nameof(Scripts.scr_characterMapInit))]
[assembly: HookScript(nameof(Scripts.scr_smoothSaveExit))]

namespace StoneshardMP.Features.Join;

// The host keeps everyone's save (legacy StoneshardMP's design): a client has no save data of its own.
// - A client that's in asks to join (JoinRequestPacket). The host answers once it's in a world - it pressed Continue
//   or Load Game - or has begun a new one (New Game: its world's seed is made before its own character is): until
//   then the client waits.
// - The host has a character for that player: it sends its world (its save data) with that character in it
//   (JoinSave.BuildSave), and the client loads it (in place of a save of its own: scr_slotLoad).
// - It hasn't: the client makes one - straight into the game's new game (Adventure: the class picked at Verren) on
//   the host's world map (its seed, given as it's made: scr_characterMapInit). Its first save sends the character to
//   the host and asks again - and it's let in as above. A host making its own new character too: the client makes
//   theirs alongside, its character kept until the host's world is ready.
// - The client's saves are never written here (scr_slotUpdate): each sends its character to the host, which keeps it
//   in its world's save data (JoinSave.StoreCharacter) - saved with the host's own saves. Every RefreshEvery a client in
//   the host's world sends it its character anyway (the game's save step alone: no fade), so the host's saves are fresh.
// - The host loading a save (scr_slotLoad): everyone reloads into it in place (WorldReloadPacket) - they stay in game,
//   ask again, and load the world the host sends once the save is up (a fade to black and back).
// - Leaving: the host's Save & Exit (scr_smoothSaveExit) waits for everyone in its world to save first
//   (SaveRequestPacket - each client's save sends its character), up to ExitWait, so its exit save holds everyone.
//   Back on the main menu the host tells everyone (HostLeftPacket): they go back to theirs, and join again when it
//   plays again. A host gone (stopped, connection lost) sends a client in its world back to the menu too.
public sealed class JoinManager
{
    private enum ClientState { Idle, Asked, MakingCharacter, Received, Loading, InWorld }
    // Whose Esc menu we have: the game's, a client's (Disconnect, no Load Game), the host's (Disconnect).
    private enum EscRole { Game, Client, Host }

    // (How long a client waits for a calm moment before loading the host's world: half a second of one.)
    private const int CalmFrames = 30;
    // (How long the host's Save & Exit waits for everyone's saves.)
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(15);
    // (How often a client in the host's world sends it its character, so the host's saves have where it is.)
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(20);

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Func<string> _playerName;
    // Host: who's asked to join (by slot), and whether they've been told to wait; characters sent before we were in a
    // world to keep them in.
    private readonly Dictionary<int, (string Name, bool Told)> _requests = new();
    private readonly Dictionary<string, string> _pendingCharacters = new();
    private ClientState _state;
    private double _hostSeed = -1;
    private int _calm;
    // (A new character's game is started from Tick, never inside the network handler.)
    private bool _startNew;
    // Host: we've begun a new world (New Game) - its seed is made, the players waiting can make their characters now.
    private bool _hostNewWorld;
    // Host: in a world last frame (to tell everyone when we've left it); Save & Exit holding for these players' saves
    // until then; letting it through.
    private bool _hostWasInWorld;
    private readonly HashSet<int> _exitWaiting = new();
    private DateTime _exitDeadline;
    private bool _exitGo;
    // Client: back to the main menu (the host left), and save now (the host asked) - both done from Tick.
    private bool _leave;
    private bool _saveNow;
    // The Esc menu has Disconnect in place of Save & Exit - a client's while it plays the host's world, the host's while it
    // hosts in its world; Disconnect confirmed, the session to leave (a host: to stop hosting) once back on the main menu.
    private EscRole _esc;
    // Host: loading a save - the players reload into it in place (WorldReloadPacket); characters from the world being
    // left are dropped, and nobody's let in until it's up (calm for CalmFrames).
    private bool _hostLoading;
    private int _hostLoadCalm;
    // Client: the host is loading a save - we stay in game and load its world in place once it comes (a fade to black and
    // back). No character of ours in it: back to the main menu, asking again there (_askOnMenu) to make one.
    private bool _reloading;
    private bool _askOnMenu;
    // Client: when our character next goes to the host (Refresh); the black screen held while the host loads.
    private DateTime _nextRefresh;
    private Instance _blackout;
    // Host: a save just written - the players in our world asked for their characters now, and that save written again
    // with them once they're in (or TopUpWait is up), so it has where everyone is, not where they were last sent.
    private readonly HashSet<int> _topUpWaiting = new();
    private DateTime _topUpDeadline;
    private bool _topUpGot;
    private string _topUpSlot = "", _topUpSave = "";
    private static readonly TimeSpan TopUpWait = TimeSpan.FromSeconds(5);
    private bool _disconnecting;

    public JoinManager(ModContext context, Session session, Func<string> playerName)
    {
        _context = context;
        _session = session;
        _playerName = playerName;
        session.On<JoinRequestPacket>(OnRequest);
        session.On<JoinReplyPacket>(OnReply);
        session.On<JoinWorldPacket>(OnWorld);
        session.On<JoinCharacterPacket>(OnCharacter);
        session.On<SaveRequestPacket>(OnSaveRequest);
        session.On<HostLeftPacket>(OnHostLeft);
        session.On<WorldReloadPacket>(OnWorldReload);
        session.Changed += OnSessionChanged;
        session.PlayerLeft += player =>
        {
            _requests.Remove(player.Slot);
            _exitWaiting.Remove(player.Slot);
        };
        // The host's Save & Exit: everyone in our world saves first.
        Scripts.scr_smoothSaveExit.Before(context, call => HoldExit());

        // A save of ours - unless the host's world is waiting to load: that becomes the save data.
        Scripts.scr_slotLoad.Before(context, call =>
        {
            if (_session.Mode == Session.SessionMode.Host)
                HostLoading();
            return JoinSave.TakePending();
        });
        // A client's saves go to the host, not here.
        Scripts.scr_slotUpdate.Before(context, call => ClientSave());
        // The host's saves: topped up with everyone's characters as they are now.
        Scripts.scr_slotUpdate.After(context, call => HostSaved());
        // Disconnect, confirmed (the game's confirmation calls back an Esc menu button's user event 2 - Exit's): the game's
        // Save & Exit, and the session left on the main menu (Tick). A client's save sends the host its character
        // (ClientSave: nothing written here), so the host has where it got to; the host's is its own save, after waiting
        // for everyone in its world to save (HoldExit).
        context.OnCode("gml_Object_o_ingame_menu_button_Other_12", before: (_, _) =>
        {
            if (_esc == EscRole.Game || !Gm.InstanceExists(GameObjectId.o_exit_confirm_panel))
                return false;
            _disconnecting = true;
            Game.CallScript("scr_smoothSaveExit", default);
            return true;
        });
        // A new game's save data is being made (its seed just rolled, in scr_gameDataMapInit): a client's new character
        // takes the host's world map; a host's new world lets the waiting players start theirs.
        Scripts.scr_characterMapInit.Before(context, call =>
        {
            bool newGame = !Game.Global["is_load_game"].AsBool;
            if (_session.Mode == Session.SessionMode.Host)
                _hostNewWorld = newGame;
            else if (_state == ClientState.MakingCharacter && _hostSeed >= 0 && newGame)
                JoinSave.ApplySeed(_hostSeed);
            return false;
        });
    }

    /// <summary>What joining is doing, for the status line (null: nothing to say - not a client).</summary>
    public string? Status { get; private set; }

    /// <summary>Whether this client is in the host's world (or on its way there).</summary>
    public bool ClientPlaying => _reloading
        || _state is ClientState.MakingCharacter or ClientState.Received or ClientState.Loading or ClientState.InWorld;

    /// <summary>Whether this client is playing in the host's world now (not making its character on a copy of its
    /// map, nor on its way in).</summary>
    public bool ClientInWorld => _state == ClientState.InWorld;

    /// <summary>Whether our game is the shared world: always for the host (or alone), and for a client only once it
    /// plays the host's world - making its character it's on a copy of the host's map, where the same room names are
    /// somewhere else.</summary>
    public bool InSharedWorld => _session.Mode != Session.SessionMode.Client || ClientInWorld;

    private string HostName => _session.Players.FirstOrDefault(p => p.Slot == 0)?.Name ?? "the host";

    public void Clear()
    {
        FollowEscMenu(EscRole.Game);
        _requests.Clear();
        _pendingCharacters.Clear();
        _state = ClientState.Idle;
        _hostSeed = -1;
        _calm = 0;
        _startNew = false;
        _hostNewWorld = false;
        _hostWasInWorld = false;
        _exitWaiting.Clear();
        _exitGo = false;
        _saveNow = false;
        _hostLoading = false;
        _reloading = false;
        _askOnMenu = false;
        _topUpWaiting.Clear();
        Unblack();
        Status = null;
    }

    // Each frame.
    public void Tick()
    {
        FollowEscMenu(_session.Mode switch
        {
            Session.SessionMode.Client when ClientPlaying => EscRole.Client,
            Session.SessionMode.Host when JoinSave.HostInWorld() => EscRole.Host,
            _ => EscRole.Game,
        });
        // (Disconnected: back on the main menu, saved - out of the session. A host stopping takes everyone with it: its
        // clients go back to their main menus.)
        if (_disconnecting && Gm.InMainMenu && !Rooms.IsChanging)
        {
            _disconnecting = false;
            if (_session.Mode == Session.SessionMode.Client)
                _session.Stop("Disconnected");
            else if (_session.Mode == Session.SessionMode.Host)
                _session.Stop("Stopped hosting");
        }
        // Back to the menu - the host left its world, or is gone - whatever our session's doing now. Tried each frame
        // until it's under way: the game refuses a room change mid-conversation or mid-cutscene (the new character's
        // intro, at Osbrook's tavern, is both).
        if (_leave)
        {
            if (Gm.InMainMenu)
                _leave = false;
            else if (!Gm.InstanceExists(GameObjectId.o_smoothRoomChanger) && JoinSave.LeaveToMenu())
                _leave = false;
        }
        // (Reloading with no character of ours in the host's save: on the main menu now - ask again, to make one.)
        if (_askOnMenu && !_leave && Gm.InMainMenu && !Rooms.IsChanging)
        {
            _askOnMenu = false;
            if (_session.Mode == Session.SessionMode.Client && _session.Connected)
                Ask();
        }
        if (_session.Mode == Session.SessionMode.Host)
            HostTick();
        else if (_session.Mode == Session.SessionMode.Client)
            ClientTick();
    }

    // ---- host ----

    private void HostTick()
    {
        bool inWorld = JoinSave.HostInWorld();
        // (Loading a save: up once we're in it and it's been calm a moment - until then as if out of our world, so
        // nobody's sent the world being left.)
        if (_hostLoading)
        {
            _hostLoadCalm = inWorld && !Gm.InstanceExists(GameObjectId.o_smoothRoomChanger) && !Game.IsBusy
                ? _hostLoadCalm + 1 : 0;
            if (_hostLoadCalm >= CalmFrames)
            {
                _hostLoading = false;
                _context.Log("The loaded save is up: letting everyone back in");
            }
            else
                inWorld = false;
        }
        if (_hostWasInWorld && !inWorld && Gm.InMainMenu)
        {
            _hostWasInWorld = false;
            _session.Send(new HostLeftPacket());
            _context.Log("Left our world: everyone in it goes back to the main menu");
        }
        else if (inWorld)
            _hostWasInWorld = true;
        if (_exitWaiting.Count > 0 && DateTime.UtcNow >= _exitDeadline)
        {
            _context.Log($"Save & Exit: {_exitWaiting.Count} player(s) didn't save in time - leaving anyway");
            _exitWaiting.Clear();
            ExitNow();
        }
        if (_topUpWaiting.Count > 0 && DateTime.UtcNow >= _topUpDeadline)
        {
            _context.Log($"Save top-up: {_topUpWaiting.Count} player(s) didn't send their character in time");
            _topUpWaiting.Clear();
        }
        // (Back on the main menu: no new world begun any more.)
        if (Gm.InMainMenu && !Gm.InstanceExists(GameObjectId.o_smoothRoomChanger))
            _hostNewWorld = false;
        if (!inWorld)
        {
            foreach (var (slot, request) in _requests.ToList())
            {
                // A new world of ours has nobody's character in it: they make theirs now, alongside us (unless they
                // have - it's kept here until our world is ready, and they're let in then).
                if (_hostNewWorld && !_pendingCharacters.ContainsKey(request.Name))
                {
                    _requests.Remove(slot);
                    _session.Send(new JoinReplyPacket(JoinReply.MakeCharacter, SharedWorld.WorldSeed()), to: slot);
                    _context.Log($"{request.Name} is making a new character alongside ours");
                }
                else if (!request.Told)
                {
                    _session.Send(new JoinReplyPacket(JoinReply.Wait, -1), to: slot);
                    _requests[slot] = (request.Name, true);
                }
            }
            return;
        }
        // Characters sent while we weren't in a world (made alongside our new game, or saved as we left): into the world
        // we're now in - they're newer than what it has.
        foreach (var (name, character) in _pendingCharacters.ToList())
        {
            JoinSave.StoreCharacter(name, character);
            _pendingCharacters.Remove(name);
            _context.Log($"{name}'s character kept (held until we were back in our world): {JoinSave.Where(character)}");
        }
        // (A save being topped up: everyone's in - or the wait's up - and kept above: written again.)
        if (_topUpGot && _topUpWaiting.Count == 0)
            TopUpSave();
        foreach (var (slot, request) in _requests.ToList())
        {
            _requests.Remove(slot);
            string save = JoinSave.BuildSave(request.Name);
            if (save.Length == 0)
            {
                _session.Send(new JoinReplyPacket(JoinReply.MakeCharacter, SharedWorld.WorldSeed()), to: slot);
                _context.Log($"{request.Name} is making a new character for this world");
                continue;
            }
            byte[] data = JoinCompression.Compress(save);
            _session.Send(new JoinReplyPacket(JoinReply.WorldFollows, SharedWorld.WorldSeed()), to: slot);
            _session.Send(new JoinWorldPacket(data), to: slot);
            _context.Log($"{request.Name} is joining this world ({save.Length / 1024} KB of save, {data.Length / 1024} KB sent)");
        }
    }

    private void OnRequest(RemotePlayer sender, JoinRequestPacket packet)
    {
        if (_session.Mode == Session.SessionMode.Host)
            _requests[sender.Slot] = (packet.Name, false);
    }

    private void OnCharacter(RemotePlayer sender, JoinCharacterPacket packet)
    {
        if (_session.Mode != Session.SessionMode.Host)
            return;
        // (Loading a save: a character from the world we're leaving isn't the one the save has.)
        if (_hostLoading)
        {
            _context.Log($"{packet.Name}'s character from the world we left ignored: the save being loaded has theirs");
            return;
        }
        string character = JoinCompression.Decompress(packet.Character);
        // (Not in our world this moment - a save's room change under way hides the player for a few frames: held, and
        // kept as soon as we're back in it - HostTick.)
        if (!JoinSave.StoreCharacter(packet.Name, character))
            _pendingCharacters[packet.Name] = character;
        else
            _context.Log($"{packet.Name}'s character kept: {JoinSave.Where(character)}");
        // (A held Save & Exit: one more saved.)
        if (_exitWaiting.Remove(sender.Slot) && _exitWaiting.Count == 0)
            ExitNow();
        // (A save being topped up: one more in - it's written again once everyone's in and kept, from HostTick.)
        if (_topUpWaiting.Remove(sender.Slot))
            _topUpGot = true;
    }

    // scr_slotUpdate done, on the host: a save of our world was written (its folder: the slots map's last). The players
    // in it send their characters now (SaveRequestPacket), and the save's written again with them (TopUpSave).
    private void HostSaved()
    {
        // (Not HostInWorld: mid-save the game may have the player hidden. Saving means there's a world.)
        if (_session.Mode != Session.SessionMode.Host || _hostLoading || !StoneForge.SaveData.Available
            || Game.Global["slotsMap"].AsDsMap is not { } slots)
            return;
        _topUpWaiting.Clear();
        foreach (var player in _session.Players)
            if (player.State != null)
                _topUpWaiting.Add(player.Slot);
        if (_topUpWaiting.Count == 0)
            return;
        _topUpSlot = slots["lastCharacter"].AsString;
        _topUpSave = slots["lastSave"].AsString;
        _topUpGot = false;
        _topUpDeadline = DateTime.UtcNow + TopUpWait;
        _session.Send(new SaveRequestPacket());
        _context.Log($"Saved {_topUpSlot}/{_topUpSave}: asking {_topUpWaiting.Count} player(s) for their characters to top it up");
    }

    // The save just written (HostSaved), written again with the characters that came in since - the save data only
    // (scr_slotSaveDataMapSave, what the game's save writes it with), to the same folder. Not if we've left the world or
    // saved somewhere else since.
    private void TopUpSave()
    {
        if (!_topUpGot || !JoinSave.HostInWorld() || Game.Global["slotsMap"].AsDsMap is not { } slots
            || slots["lastCharacter"].AsString != _topUpSlot || slots["lastSave"].AsString != _topUpSave)
            return;
        _topUpGot = false;
        Game.CallScript("scr_slotSaveDataMapSave", default, _topUpSlot, _topUpSave, Game.Global["saveDataMap"]);
        _context.Log($"Save {_topUpSlot}/{_topUpSave} topped up with everyone's characters as they are now");
    }

    // The Esc menu: Disconnect in place of Save & Exit for a client playing the host's world (it keeps no saves of the
    // host's world) and for the host in its world (it saves, then stops hosting). Back as it was otherwise.
    private void FollowEscMenu(EscRole role)
    {
        if (role == _esc)
            return;
        _esc = role;
        EscMenu.UndoChanges(_context);
        if (role == EscRole.Game)
            return;
        EscMenu.RemoveButton(_context, EscButton.SaveAndExit);
        // (Only the host loads a save: everyone comes back into it - HostLoading. A client's own saves aren't the host's
        // world.)
        if (role == EscRole.Client)
            EscMenu.RemoveButton(_context, EscButton.LoadGame);
        EscMenu.AddButton(_context, "Disconnect", ConfirmDisconnect);
    }

    // Host: a save is loading (scr_slotLoad - from the Esc menu or the main menu). Everyone in the session reloads into it
    // (WorldReloadPacket): they stay in game and ask again, and once it's up get their world with their characters as the
    // save has them - loaded in place, a fade to black and back. What's still to come from the world being left is dropped.
    private void HostLoading()
    {
        _hostLoading = true;
        _hostLoadCalm = 0;
        _pendingCharacters.Clear();
        _exitWaiting.Clear();
        _topUpWaiting.Clear();
        _topUpGot = false;
        if (!_session.Players.Any())
            return;
        _session.Send(new WorldReloadPacket());
        _context.Log("Loading a save: everyone reloads into it, as it has them");
    }

    // Disconnect clicked: the game's own confirmation, as its Exit asks (o_exit_confirm_panel, the game's question) - a yes
    // calls back an Esc menu button's user event 2, which the Disconnect hook takes.
    private void ConfirmDisconnect()
    {
        if (Gm.InstanceExists(GameObjectId.o_exit_confirm_panel)
            || Instances.All(GameObjectId.o_ingame_menu_button).FirstOrDefault() is not { IsNone: false } owner)
            return;
        Instance panel = Game.CallScript("scr_guiCreateContainer", default, Game.Global["guiBaseContainerVisible"],
            (int)GameObjectId.o_exit_confirm_panel).AsInstance;
        if (panel.IsNone)
            return;
        panel["owner"] = owner;
        panel["event"] = 2;
        if (Game.Global["button_hover"].AsDsList is { } texts)
            panel["text"] = texts[46];
    }

    // scr_smoothSaveExit, on the host: with players in our world, ask them to save and hold the exit until they have
    // (OnCharacter) or ExitWait is up - then it runs for real (ExitNow).
    private bool HoldExit()
    {
        if (_session.Mode != Session.SessionMode.Host || _exitGo)
        {
            _exitGo = false;
            return false;
        }
        if (_exitWaiting.Count > 0)
            return true;
        foreach (var player in _session.Players)
            if (player.State != null)
                _exitWaiting.Add(player.Slot);
        if (_exitWaiting.Count == 0)
            return false;
        _exitDeadline = DateTime.UtcNow + ExitWait;
        _session.Send(new SaveRequestPacket());
        _context.Log($"Save & Exit: waiting for {_exitWaiting.Count} player(s) to save");
        return true;
    }

    private void ExitNow()
    {
        if (!Gm.InGame)
            return;
        _exitGo = true;
        Game.CallScript("scr_smoothSaveExit", default);
    }

    // ---- client ----

    private void OnSessionChanged()
    {
        if (_session.Mode != Session.SessionMode.Client)
        {
            if (_state != ClientState.Idle)
            {
                // The host's gone: out of its world too.
                _leave = ClientPlaying;
                Clear();
            }
            return;
        }
        // In: ask to join the host's world.
        if (_session.Connected && _state == ClientState.Idle)
            Ask();
    }

    private void Ask()
    {
        _state = ClientState.Asked;
        Status = $"Asking to join {HostName}'s world...";
        _session.Send(new JoinRequestPacket(_playerName()), to: 0);
    }

    private void OnReply(RemotePlayer sender, JoinReplyPacket packet)
    {
        if (_session.Mode != Session.SessionMode.Client || sender.Slot != 0)
            return;
        _hostSeed = packet.Seed;
        switch (packet.Reply)
        {
            case JoinReply.Wait:
                Status = _reloading
                    ? $"{HostName} is loading a save..."
                    : $"Waiting for {HostName} to start or load a game...";
                break;
            case JoinReply.MakeCharacter when Gm.InMainMenu:
                _state = ClientState.MakingCharacter;
                Status = $"Making your character for {HostName}'s world";
                _startNew = true;
                break;
            case JoinReply.MakeCharacter when _reloading:
                // (The host's save has no character of ours: to the main menu, to make one there.)
                _reloading = false;
                Unblack();
                _state = ClientState.Idle;
                _leave = true;
                _askOnMenu = true;
                Status = $"No character of yours in {HostName}'s save - back to the main menu to make one";
                break;
            case JoinReply.MakeCharacter:
                Status = $"New to {HostName}'s world: go back to the main menu to make your character";
                break;
            case JoinReply.WorldFollows:
                Status = $"Receiving {HostName}'s world...";
                break;
        }
    }

    private void OnSaveRequest(RemotePlayer sender, SaveRequestPacket packet)
    {
        if (_session.Mode == Session.SessionMode.Client && sender.Slot == 0 && ClientPlaying)
            _saveNow = true;
    }

    // The host left its world: back to the main menu, and asking to join again (it answers when it plays again).
    private void OnHostLeft(RemotePlayer sender, HostLeftPacket packet)
    {
        if (_session.Mode != Session.SessionMode.Client || sender.Slot != 0 || _state == ClientState.Idle)
            return;
        _leave = ClientPlaying;
        _state = ClientState.Idle;
        _calm = 0;
        Ask();
        Status = $"{HostName} left their world - waiting for them to play again...";
    }

    // The host is loading a save: stay in game and ask again - its world, once it's up, loads in place (Received, then
    // StartLoad: a fade to black and back), with our character as that save has it.
    private void OnWorldReload(RemotePlayer sender, WorldReloadPacket packet)
    {
        if (_session.Mode != Session.SessionMode.Client || sender.Slot != 0 || _state == ClientState.Idle)
            return;
        _reloading = ClientPlaying && Gm.InGame && !_leave;
        _calm = 0;
        _saveNow = false;
        if (_reloading)
            Blackout();
        Ask();
        Status = $"{HostName} is loading a save...";
    }

    // Fade to black and hold it while the host loads (the game's black overlay: it fades in and stays, and the room
    // change that loads the host's world fades it out with its own on the new room's start).
    private void Blackout()
    {
        if (_blackout.Exists)
            return;
        _blackout = Game.CallScript("scr_guiCreateSimple", default, Game.Global["guiBaseContainerVisible"],
            (int)GameObjectId.o_black_overlay).AsInstance.Persist();
    }

    // The reload's off: the black fades out (the overlay destroys itself once clear).
    private void Unblack()
    {
        if (!_blackout.IsNone && _blackout.Exists)
            _blackout["maxAlpha"] = 0;
        _blackout = default;
    }

    private void OnWorld(RemotePlayer sender, JoinWorldPacket packet)
    {
        if (_session.Mode != Session.SessionMode.Client || sender.Slot != 0)
            return;
        string save = JoinCompression.Decompress(packet.Save);
        if (!JoinSave.SetPending(save, HostName))
        {
            Status = $"{HostName}'s world arrived damaged - leave and join again";
            _context.Log($"{HostName}'s world didn't read ({save.Length} characters): {JoinSave.WhyUnreadable(save)}"
                + $" - kept as {JoinSave.KeepUnreadable(save)}");
            // (Reloading behind the black screen: not left there - back to the main menu.)
            if (_reloading)
            {
                _reloading = false;
                Unblack();
                _state = ClientState.Idle;
                _leave = true;
            }
            return;
        }
        _state = ClientState.Received;
        _calm = 0;
        Status = $"Received {HostName}'s world - loading...";
    }

    private void ClientTick()
    {
        if (_startNew)
        {
            _startNew = false;
            JoinSave.StartNew();
        }
        // (Behind the black screen: what's going on, in the middle of it - the overlay draws its text while it's up.)
        if (!_blackout.IsNone && Status is { } status && _blackout.Exists && _blackout["text"].AsString != status)
        {
            _blackout["drawText"] = true;
            _blackout["text"] = status;
            _blackout["textAlpha"] = 1;
        }
        // The host is saving to leave: our character, now.
        if (_saveNow)
        {
            _saveNow = false;
            Refresh();
        }
        // And every so often anyway, so the host's own saves have where we are.
        else if (_state == ClientState.InWorld && DateTime.UtcNow >= _nextRefresh && !Game.IsBusy)
            Refresh();
        switch (_state)
        {
            case ClientState.Received:
                // (Reloading behind our black screen - which the game counts as busy - only a room change to wait out.)
                _calm = (_reloading ? Gm.InGame && !Rooms.IsChanging : JoinSave.Calm()) ? _calm + 1 : 0;
                if (_calm >= CalmFrames && JoinSave.StartLoad())
                {
                    _state = ClientState.Loading;
                    Status = $"Joining {HostName}'s world";
                }
                break;
            case ClientState.Loading:
                if (Gm.InGame && !Gm.InstanceExists(GameObjectId.o_smoothRoomChanger))
                {
                    _state = ClientState.InWorld;
                    _reloading = false;
                    _blackout = default;
                    _nextRefresh = DateTime.UtcNow + RefreshEvery;
                    Status = $"In {HostName}'s world";
                }
                break;
        }
    }

    // Our character to the host without the game's save (no fade, nothing written): the game's own save step
    // (scr_savegame - what its saves run inside their fade), which ends in scr_slotUpdate, where ClientSave sends it.
    private void Refresh()
    {
        _nextRefresh = DateTime.UtcNow + RefreshEvery;
        if (_state != ClientState.InWorld || !Gm.InstanceExists(GameObjectId.o_player))
            return;
        Game.CallScript("scr_savegame", default, Rooms.CurrentName, 1);
    }

    // scr_slotUpdate (every save): a client making its character, or in the host's world, sends its character to
    // the host instead of keeping the save - the first one (the end of the new character's intro) asking to join.
    private bool ClientSave()
    {
        // (On our way back to the menu: no save kept of the host's world here either.)
        if (_leave || _reloading)
            return true;
        if (_session.Mode != Session.SessionMode.Client || !ClientPlaying)
            return false;
        // (The save data's sections the game's live ones, so what goes is where we are.)
        string relinked = JoinSave.LinkLive();
        if (relinked.Length > 0)
            _context.Log($"Save data's character sections weren't the game's live ones - relinked: {relinked}");
        string character = JoinSave.CharacterJson();
        if (character.Length > 0)
            _session.Send(new JoinCharacterPacket(_playerName(), JoinCompression.Compress(character)), to: 0);
        if (_state == ClientState.MakingCharacter)
        {
            Status = $"Character made - joining {HostName}'s world...";
            Ask();
        }
        if (_state != ClientState.InWorld)
            _context.Log("Saved: our character went to the host (no save kept here)");
        else
            _context.Log($"Character sent to the host: {JoinSave.Where(character)} - we're in {JoinSave.PlayerWhere()}");
        return true;
    }
}
