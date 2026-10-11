using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace StoneshardMP.Net.Steam;

// A session's Steam side, on the game's thread (Session's Poll): its lobby and its tunnel.
// - Hosting: a friends-only lobby (its data: that it's StoneshardMP, the protocol, the host's name), the tunnel taking
//   its members to our LiteNetLib, and "Hosting StoneshardMP" as our Steam status. Friends see us in a lobby of
//   Stoneshard's - the game has none of its own - and join from the Multiplayer menu (Join a Friend), or are invited
//   (Invite Friends: Steam's own invite dialog).
// - Joining: the friend's lobby joined, its owner the host; a tunnel to them, and our LiteNetLib connected through it.
// Steam's answers (a lobby made, joined) are polled each frame - nothing here waits.
// Invites: hosting, our Steam "connect" is "+connect_lobby <lobby>", so every way Steam offers to join us carries the
// lobby. A friend accepting - a lobby invite, the overlay's Invite to Game, Join Game on the friends list - is one of
// Steam's callbacks in their running game (GameLobbyJoinRequested, GameRichPresenceJoinRequested: SteamCallback), or,
// with the game not running, Steam starts it with that on its command line. Either way it's kept (TakeInvite) till
// the Multiplayer menu can join it - on the main menu, not in a session.
internal sealed class SteamLink
{
    public const uint AppId = 625960;
    private const string KeyMod = "stoneshardmp", KeyName = "name";

    private enum State { Idle, Creating, Hosting, Joining, Joined }

    private readonly Action<string> _log;
    private State _state;
    private ulong _call, _lobby;
    private SteamTunnel? _tunnel;
    private int _hostPort;
    private string _name = "";
    private Action<int>? _connect;
    private Action<string>? _failed;
    private long _greetedAt;
    // Invites: Steam's two callbacks, heard once Steam's up; whether the command line's been read; the latest invite.
    private SteamCallback? _lobbyJoin, _presenceJoin;
    private bool _launchRead;
    private FriendLobby? _invite;

    public SteamLink(Action<string> log) => _log = log;

    public static bool Available => SteamApi.Available;

    /// <summary>Whether we're hosting a Steam lobby friends can join.</summary>
    public bool Hosting => _state == State.Hosting;

    public string Summary => _state switch
    {
        State.Idle => "off",
        State.Hosting => $"hosting lobby {_lobby}, {_tunnel?.Players ?? 0} by Steam, sent {_tunnel?.Sent ?? 0} B, received {_tunnel?.Received ?? 0} B{Links()}",
        State.Joined => $"joined lobby {_lobby}, sent {_tunnel?.Sent ?? 0} B, received {_tunnel?.Received ?? 0} B{Links()}",
        _ => _state.ToString().ToLowerInvariant(),
    };

    // (Each Steam player's link: who, Steam's ping to them, and whether it goes by Valve's relays or straight there.)
    private string Links()
    {
        if (_tunnel == null)
            return "";
        var links = _tunnel.Peers.Select(peer =>
        {
            var (ping, relayed) = SteamApi.Link(peer);
            string name = SteamApi.FriendName(peer);
            return $"{(name.Length > 0 ? name : peer.ToString())} {(ping >= 0 ? ping + " ms" : "?")} {(relayed ? "relayed" : "direct")}";
        }).ToList();
        return links.Count > 0 ? "; " + string.Join(", ", links) : "";
    }

    /// <summary>Hosting: a lobby for our LiteNetLib on <paramref name="port"/>.</summary>
    public void Host(int port, int limit, string name)
    {
        Stop();
        if (!Available)
            return;
        _hostPort = port;
        _name = name;
        _call = SteamApi.CreateLobby(SteamApi.LobbyFriendsOnly, limit);
        _state = State.Creating;
    }

    /// <summary>Joining a friend's lobby: <paramref name="connect"/> is given the port on 127.0.0.1 to connect to, once
    /// we're in it; <paramref name="failed"/> why, if we can't.</summary>
    public void Join(FriendLobby friend, Action<int> connect, Action<string> failed)
    {
        Stop();
        if (!Available)
        {
            failed("Steam isn't running");
            return;
        }
        _lobby = friend.Lobby;
        _connect = connect;
        _failed = failed;
        _call = SteamApi.JoinLobby(friend.Lobby);
        _state = State.Joining;
    }

    /// <summary>Hosting: Steam's invite dialog, for our lobby.</summary>
    public void Invite()
    {
        if (_state == State.Hosting)
            SteamApi.InviteDialog(_lobby);
    }

    /// <summary>The latest Steam invite accepted (or the game started with), taken: the menu joins it.</summary>
    public FriendLobby? TakeInvite()
    {
        var invite = _invite;
        _invite = null;
        return invite;
    }

    /// <summary>Whether there's an invite waiting to be joined.</summary>
    public bool HasInvite => _invite != null;

    public void Tick()
    {
        Listen();
        switch (_state)
        {
            case State.Creating when SteamApi.LobbyCreated(_call) is { } lobby:
                if (lobby == 0)
                {
                    _log("Steam: couldn't make a lobby - only direct joining (by address) is open");
                    _state = State.Idle;
                    break;
                }
                _lobby = lobby;
                SteamApi.SetLobbyData(lobby, KeyMod, Session.Protocol.ToString());
                SteamApi.SetLobbyData(lobby, KeyName, _name);
                SteamApi.SetRichPresence("status", "Hosting StoneshardMP");
                SteamApi.SetRichPresence("connect", ConnectPrefix + lobby);
                _tunnel = SteamTunnel.ForHost(_hostPort);
                _state = State.Hosting;
                _log($"Steam: lobby {lobby} open to friends");
                break;
            case State.Hosting:
                // (Every second: who's in the lobby, greeted and let through.)
                long now = Environment.TickCount64;
                if (now - _greetedAt >= 1000)
                {
                    _greetedAt = now;
                    ulong me = SteamApi.MySteamId;
                    _tunnel!.Greet(SteamApi.LobbyMembers(_lobby).Where(m => m != me));
                }
                break;
            case State.Joining when SteamApi.LobbyEntered(_call) is { } entered:
                string protocol = entered ? SteamApi.LobbyData(_lobby, KeyMod) : "";
                ulong host = entered ? SteamApi.LobbyOwner(_lobby) : 0;
                if (!entered || host == 0 || protocol.Length == 0)
                    Fail("Couldn't join the friend's game on Steam - it may have closed");
                else if (protocol != Session.Protocol.ToString())
                    Fail($"The friend's StoneshardMP is a different version (protocol {protocol}, ours {Session.Protocol})");
                else
                {
                    _tunnel = SteamTunnel.ToHost(host);
                    _state = State.Joined;
                    SteamApi.SetRichPresence("status", "Playing StoneshardMP");
                    _connect?.Invoke(_tunnel.Port);
                }
                break;
        }
    }

    public void Stop()
    {
        _tunnel?.Dispose();
        _tunnel = null;
        if (_lobby != 0 && _state is State.Hosting or State.Joined or State.Joining)
            SteamApi.LeaveLobby(_lobby);
        if (_state is State.Hosting or State.Joined)
            SteamApi.ClearRichPresence();
        _lobby = 0;
        _state = State.Idle;
        _connect = null;
        _failed = null;
    }

    private void Fail(string why)
    {
        var failed = _failed;
        Stop();
        failed?.Invoke(why);
    }

    // ---- invites ----

    private const string ConnectPrefix = "+connect_lobby ";
    // (Steam's callback ids, and their structs' sizes: GameLobbyJoinRequested_t - the lobby, then the friend;
    // GameRichPresenceJoinRequested_t - the friend, then their connect string, 256 bytes.)
    private const int LobbyJoinRequested = 333, LobbyJoinSize = 16;
    private const int PresenceJoinRequested = 337, PresenceJoinSize = 8 + 256;

    private void Listen()
    {
        if (_lobbyJoin != null || !Available)
            return;
        _lobbyJoin = new SteamCallback(LobbyJoinRequested, LobbyJoinSize, param =>
            Invited(Marshal.ReadInt64(param, 8), (ulong)Marshal.ReadInt64(param, 0)));
        _presenceJoin = new SteamCallback(PresenceJoinRequested, PresenceJoinSize, param =>
        {
            if (LobbyIn(Marshal.PtrToStringUTF8(param + 8) ?? "") is { } lobby)
                Invited(Marshal.ReadInt64(param, 0), lobby);
        });
        // (Started by accepting an invite: Steam put the friend's connect on our command line.)
        if (!_launchRead)
        {
            _launchRead = true;
            if (LobbyIn(string.Join(" ", Environment.GetCommandLineArgs())) is { } launched)
                Invited(0, launched);
        }
    }

    private void Invited(long friend, ulong lobby)
    {
        if (lobby == 0)
            return;
        string name = friend != 0 ? SteamApi.FriendName((ulong)friend) : "";
        _invite = new FriendLobby((ulong)friend, name.Length > 0 ? name : "your friend", lobby);
        _log($"Steam: invited to {_invite.Value.Name}'s game (lobby {lobby})");
    }

    // The lobby in a connect string or command line ("+connect_lobby 1234"), if there's one.
    private static ulong? LobbyIn(string text)
        => Regex.Match(text, @"\+connect_lobby\s+(\d+)") is { Success: true } found && ulong.TryParse(found.Groups[1].Value, out ulong lobby) && lobby != 0
            ? lobby : null;

    /// <summary>Friends in a lobby of Stoneshard's: ones we can join.</summary>
    public static FriendLobby[] Friends() => Available ? SteamApi.FriendsInLobbies(AppId) : Array.Empty<FriendLobby>();
}
