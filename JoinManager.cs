using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

// The game scripts joining replaces or skips (the patcher makes them hookable).
[assembly: HookScript(nameof(Scripts.scr_slotLoad))]
[assembly: HookScript(nameof(Scripts.scr_slotUpdate))]
[assembly: HookScript(nameof(Scripts.scr_characterMapInit))]

namespace StoneshardMP;

// The host keeps everyone's save (legacy StoneshardMP's design): a client has no save data of its own.
// - A client that's in asks to join (JoinRequestPacket). The host answers once it's in a world - it pressed Continue
//   or Load Game - or has begun a new one (New Game: its world's seed is made before its own character is): until
//   then the client waits.
// - The host has a character for that player: it sends its world (its save data) with that character in it
//   (MpJoinBuildSave), and the client loads it (in place of a save of its own: scr_slotLoad).
// - It hasn't: the client makes one - straight into the game's new game (Adventure: the class picked at Verren) on
//   the host's world map (its seed, given as it's made: scr_characterMapInit). Its first save sends the character to
//   the host and asks again - and it's let in as above. A host making its own new character too: the client makes
//   theirs alongside, its character kept until the host's world is ready.
// - The client's saves are never written here (scr_slotUpdate): each sends its character to the host, which keeps it
//   in its world's save data (MpJoinStoreCharacter) - saved with the host's own saves.
public sealed class JoinManager
{
    private enum ClientState { Idle, Asked, MakingCharacter, Received, Loading, InWorld }

    // (How long a client waits for a calm moment before loading the host's world: half a second of one.)
    private const int CalmFrames = 30;

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

    public JoinManager(ModContext context, Session session, Func<string> playerName)
    {
        _context = context;
        _session = session;
        _playerName = playerName;
        session.On<JoinRequestPacket>(OnRequest);
        session.On<JoinReplyPacket>(OnReply);
        session.On<JoinWorldPacket>(OnWorld);
        session.On<JoinCharacterPacket>(OnCharacter);
        session.Changed += OnSessionChanged;
        session.PlayerLeft += player => _requests.Remove(player.Slot);

        // A save of ours - unless the host's world is waiting to load: that becomes the save data.
        Scripts.scr_slotLoad.Before(context, call => Gml.MpJoinTakePending());
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
                Gml.MpJoinApplySeed(_hostSeed);
            return false;
        });
    }

    /// <summary>What joining is doing, for the status line (null: nothing to say - not a client).</summary>
    public string? Status { get; private set; }

    /// <summary>Whether this client is in the host's world (or on its way there).</summary>
    public bool ClientPlaying => _state is ClientState.MakingCharacter or ClientState.Received or ClientState.Loading or ClientState.InWorld;

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
        Status = null;
    }

    // Each frame.
    public void Tick()
    {
        if (_session.Mode == Session.SessionMode.Host)
            HostTick();
        else if (_session.Mode == Session.SessionMode.Client)
            ClientTick();
    }

    // ---- host ----

    private void HostTick()
    {
        // (Back on the main menu: no new world begun any more.)
        if (Gm.InMainMenu && !Gm.InstanceExists(GameObjectId.o_smoothRoomChanger))
            _hostNewWorld = false;
        if (!Gml.MpHostInWorld())
        {
            foreach (var (slot, request) in _requests.ToList())
            {
                // A new world of ours has nobody's character in it: they make theirs now, alongside us (unless they
                // have - it's kept here until our world is ready, and they're let in then).
                if (_hostNewWorld && !_pendingCharacters.ContainsKey(request.Name))
                {
                    _requests.Remove(slot);
                    _session.Send(new JoinReplyPacket(JoinReply.MakeCharacter, Gml.MpWorldSeed()), to: slot);
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
        // Characters made while we were in the menu: into the world we're now in (one it has already wins).
        foreach (var (name, character) in _pendingCharacters.ToList())
        {
            if (!Gml.MpJoinHasCharacter(name))
                Gml.MpJoinStoreCharacter(name, character);
            _pendingCharacters.Remove(name);
        }
        foreach (var (slot, request) in _requests.ToList())
        {
            _requests.Remove(slot);
            string save = Gml.MpJoinBuildSave(request.Name);
            if (save.Length == 0)
            {
                _session.Send(new JoinReplyPacket(JoinReply.MakeCharacter, Gml.MpWorldSeed()), to: slot);
                _context.Log($"{request.Name} is making a new character for this world");
                continue;
            }
            byte[] data = JoinCompression.Compress(save);
            _session.Send(new JoinReplyPacket(JoinReply.WorldFollows, Gml.MpWorldSeed()), to: slot);
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
        if (!Gml.MpJoinStoreCharacter(packet.Name, character))
            _pendingCharacters[packet.Name] = character;
    }

    // ---- client ----

    private void OnSessionChanged()
    {
        if (_session.Mode != Session.SessionMode.Client)
        {
            if (_state != ClientState.Idle)
                Clear();
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

    private void OnWorld(RemotePlayer sender, JoinWorldPacket packet)
    {
        if (_session.Mode != Session.SessionMode.Client || sender.Slot != 0)
            return;
        if (!Gml.MpJoinSetPending(JoinCompression.Decompress(packet.Save), HostName))
        {
            Status = $"{HostName}'s world arrived damaged - leave and join again";
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
            Gml.MpJoinStartNew();
        }
        switch (_state)
        {
            case ClientState.Received:
                _calm = Gml.MpJoinCalm() ? _calm + 1 : 0;
                if (_calm >= CalmFrames && Gml.MpJoinStartLoad())
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
        if (_session.Mode != Session.SessionMode.Client || !ClientPlaying)
            return false;
        string character = Gml.MpJoinCharacterSections();
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
