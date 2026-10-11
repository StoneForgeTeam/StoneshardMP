using System;
using System.Linq;

namespace StoneshardMP.Net.Steam;

// A session's Steam side, on the game's thread (Session's Poll): its lobby and its tunnel.
// - Hosting: a friends-only lobby (its data: that it's StoneshardMP, the protocol, the host's name), the tunnel taking
//   its members to our LiteNetLib, and "Hosting StoneshardMP" as our Steam status. Friends see us in a lobby of
//   Stoneshard's - the game has none of its own - and join from the Multiplayer menu (Join a Friend), or are invited
//   (Invite Friends: Steam's own invite dialog).
// - Joining: the friend's lobby joined, its owner the host; a tunnel to them, and our LiteNetLib connected through it.
// Steam's answers (a lobby made, joined) are polled each frame - nothing here waits.
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

    public SteamLink(Action<string> log) => _log = log;

    public static bool Available => SteamApi.Available;

    /// <summary>Whether we're hosting a Steam lobby friends can join.</summary>
    public bool Hosting => _state == State.Hosting;

    public string Summary => _state switch
    {
        State.Idle => "off",
        State.Hosting => $"hosting lobby {_lobby}, {_tunnel?.Players ?? 0} by Steam, sent {_tunnel?.Sent ?? 0} B, received {_tunnel?.Received ?? 0} B",
        State.Joined => $"joined lobby {_lobby}, sent {_tunnel?.Sent ?? 0} B, received {_tunnel?.Received ?? 0} B",
        _ => _state.ToString().ToLowerInvariant(),
    };

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

    public void Tick()
    {
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
            SteamApi.SetRichPresence("status", "");
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

    /// <summary>Friends in a lobby of Stoneshard's: ones we can join.</summary>
    public static FriendLobby[] Friends() => Available ? SteamApi.FriendsInLobbies(AppId) : Array.Empty<FriendLobby>();
}
