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
//   in its world's save data (JoinSave.StoreCharacter) - saved with the host's own saves.
// - Leaving: the host's Save & Exit (scr_smoothSaveExit) waits for everyone in its world to save first
//   (SaveRequestPacket - each client's save sends its character), up to ExitWait, so its exit save holds everyone.
//   Back on the main menu the host tells everyone (HostLeftPacket): they go back to theirs, and join again when it
//   plays again. A host gone (stopped, connection lost) sends a client in its world back to the menu too.
public sealed class JoinManager
{
    private enum ClientState { Idle, Asked, MakingCharacter, Received, Loading, InWorld }

    // (How long a client waits for a calm moment before loading the host's world: half a second of one.)
    private const int CalmFrames = 30;
    // (How long the host's Save & Exit waits for everyone's saves.)
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(15);

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
        session.Changed += OnSessionChanged;
        session.PlayerLeft += player =>
        {
            _requests.Remove(player.Slot);
            _exitWaiting.Remove(player.Slot);
        };
        // The host's Save & Exit: everyone in our world saves first.
        Scripts.scr_smoothSaveExit.Before(context, call => HoldExit());

        // A save of ours - unless the host's world is waiting to load: that becomes the save data.
        Scripts.scr_slotLoad.Before(context, call => JoinSave.TakePending());
        // A client's saves go to the host, not here.
        Scripts.scr_slotUpdate.Before(context, call => ClientSave());
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
    public bool ClientPlaying => _state is ClientState.MakingCharacter or ClientState.Received or ClientState.Loading or ClientState.InWorld;

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
        Status = null;
    }

    // Each frame.
    public void Tick()
    {
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
        if (_session.Mode == Session.SessionMode.Host)
            HostTick();
        else if (_session.Mode == Session.SessionMode.Client)
            ClientTick();
    }

    // ---- host ----

    private void HostTick()
    {
        bool inWorld = JoinSave.HostInWorld();
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
        // (Back on the main menu: no new world begun any more.)
        if (Gm.InMainMenu && !Gm.InstanceExists(GameObjectId.o_smoothRoomChanger))
            _hostNewWorld = false;
        if (!JoinSave.HostInWorld())
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
        }
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
        string character = JoinCompression.Decompress(packet.Character);
        if (!JoinSave.StoreCharacter(packet.Name, character))
            _pendingCharacters[packet.Name] = character;
        // (A held Save & Exit: one more saved.)
        if (_exitWaiting.Remove(sender.Slot) && _exitWaiting.Count == 0)
            ExitNow();
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
                Status = $"Waiting for {HostName} to start or load a game...";
                break;
            case JoinReply.MakeCharacter when Gm.InMainMenu:
                _state = ClientState.MakingCharacter;
                Status = $"Making your character for {HostName}'s world";
                _startNew = true;
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
        // The host is saving to leave: our save sends it our character (ClientSave).
        if (_saveNow)
        {
            _saveNow = false;
            if (Gm.InGame)
                Game.CallScript("scr_smoothSaveAuto", default);
        }
        switch (_state)
        {
            case ClientState.Received:
                _calm = JoinSave.Calm() ? _calm + 1 : 0;
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
                    Status = $"In {HostName}'s world";
                }
                break;
        }
    }

    // scr_slotUpdate (every save): a client making its character, or in the host's world, sends its character to
    // the host instead of keeping the save - the first one (the end of the new character's intro) asking to join.
    private bool ClientSave()
    {
        // (On our way back to the menu: no save kept of the host's world here either.)
        if (_leave)
            return true;
        if (_session.Mode != Session.SessionMode.Client || !ClientPlaying)
            return false;
        string character = JoinSave.CharacterJson();
        if (character.Length > 0)
            _session.Send(new JoinCharacterPacket(_playerName(), JoinCompression.Compress(character)), to: 0);
        if (_state == ClientState.MakingCharacter)
        {
            Status = $"Character made - joining {HostName}'s world...";
            Ask();
        }
        _context.Log("Saved: our character went to the host (no save kept here)");
        return true;
    }
}
