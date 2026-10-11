using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace StoneshardMP.Net.Steam;

// LiteNetLib over Steam: its UDP datagrams carried as Steam peer-to-peer packets (through NATs, by Valve's relays when
// there's no direct way), so LiteNetLib keeps doing all it does - reliability, order, pings, timeouts - and only the
// address it talks to changes: one on this machine.
// - A client's: a socket on 127.0.0.1 stands in for the host. Our LiteNetLib connects to it; what it sends goes to the
//   host's Steam id, and what comes from the host goes back to it.
// - The host's: a socket on 127.0.0.1 for each player who comes by Steam, so the host's LiteNetLib (on its usual port)
//   sees each as a player on this machine. What one sends is passed to LiteNetLib from their socket; what LiteNetLib
//   sends to that socket goes to them. Only the lobby's members get through (Members, from the game's thread).
// A Steam session opens when one side sends first; the host can't wait for that (accepting a stranger's is a callback,
// which only the game's extension gets), so it greets each member as they come into its lobby (Greet), which opens it.
// The packets are moved on a thread of the tunnel's own, as LiteNetLib's are: a frame's wait would slow every reply.
internal sealed class SteamTunnel : IDisposable
{
    private const int DataChannel = 5, GreetChannel = 6;
    // (A player gone quiet this long - LiteNetLib's timeout is 30 s - has their socket closed.)
    private const long QuietMs = 60000;

    private readonly ulong _host;
    private readonly int _hostPort;
    private readonly Socket? _client;
    private EndPoint? _liteNetLib;
    // Host: each player's socket, and when we last heard from them; who may come in.
    private readonly Dictionary<ulong, (Socket Socket, long Heard)> _remotes = new();
    private volatile HashSet<ulong> _members = new();
    private readonly HashSet<ulong> _greeted = new();
    private readonly Thread _thread;
    private volatile bool _running = true;
    private readonly byte[] _buffer = new byte[64 * 1024];

    private SteamTunnel(ulong host, int hostPort, bool client)
    {
        _host = host;
        _hostPort = hostPort;
        if (client)
        {
            _client = Local();
            Port = ((IPEndPoint)_client.LocalEndPoint!).Port;
        }
        SteamApi.AllowRelay();
        _thread = new Thread(Run) { IsBackground = true, Name = "StoneshardMP Steam tunnel" };
        _thread.Start();
    }

    /// <summary>A client's: the port on 127.0.0.1 our LiteNetLib connects to, for the host's Steam id.</summary>
    public static SteamTunnel ToHost(ulong host) => new(host, 0, client: true);

    /// <summary>The host's: players by Steam passed to our LiteNetLib on <paramref name="port"/>.</summary>
    public static SteamTunnel ForHost(int port) => new(0, port, client: false);

    /// <summary>A client's tunnel's port on 127.0.0.1 (the host's: 0).</summary>
    public int Port { get; }

    public int Players { get; private set; }
    public long Sent, Received;

    /// <summary>Host, on the game's thread: who's in our lobby - each greeted once, opening their session.</summary>
    public void Greet(IEnumerable<ulong> members)
    {
        var set = members.Where(m => m != 0).ToHashSet();
        _members = set;
        foreach (ulong member in set)
        {
            if (!_greeted.Add(member))
                continue;
            SteamApi.Accept(member);
            SteamApi.Send(member, new byte[] { 1 }, 1, GreetChannel);
        }
        _greeted.IntersectWith(set);
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join(500);
        _client?.Dispose();
        foreach (var (remote, entry) in _remotes)
        {
            entry.Socket.Dispose();
            SteamApi.Close(remote);
        }
        _remotes.Clear();
        if (_host != 0)
            SteamApi.Close(_host);
    }

    private void Run()
    {
        while (_running)
        {
            bool busy = false;
            try
            {
                busy |= FromSteam();
                busy |= _client != null ? FromClient() : FromHost();
            }
            catch (Exception)
            {
                // (A socket gone mid-shutdown, Steam gone with the game: the loop's end, or the next try.)
            }
            if (!busy)
                Thread.Sleep(1);
        }
    }

    // What's come by Steam: passed to our LiteNetLib.
    private bool FromSteam()
    {
        bool any = false;
        while (SteamApi.Receive(_buffer, GreetChannel, out _, out _))
            any = true;
        while (SteamApi.Receive(_buffer, DataChannel, out int size, out ulong remote))
        {
            any = true;
            Received += size;
            if (_client != null)
            {
                if (remote == _host && _liteNetLib != null)
                    _client.SendTo(_buffer, size, SocketFlags.None, _liteNetLib);
                continue;
            }
            if (!_members.Contains(remote))
                continue;
            if (!_remotes.TryGetValue(remote, out var entry))
            {
                entry = (Local(), 0);
                Players = _remotes.Count + 1;
            }
            _remotes[remote] = (entry.Socket, Environment.TickCount64);
            entry.Socket.SendTo(_buffer, size, SocketFlags.None, new IPEndPoint(IPAddress.Loopback, _hostPort));
        }
        return any;
    }

    // Client: what our LiteNetLib sends the host.
    private bool FromClient()
    {
        bool any = false;
        while (_client!.Available > 0)
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            int size = _client.ReceiveFrom(_buffer, ref from);
            _liteNetLib = from;
            SteamApi.Send(_host, _buffer, size, DataChannel);
            Sent += size;
            any = true;
        }
        return any;
    }

    // Host: what our LiteNetLib sends each player, to them; quiet ones closed.
    private bool FromHost()
    {
        bool any = false;
        long now = Environment.TickCount64;
        foreach (var (remote, entry) in _remotes.ToList())
        {
            while (entry.Socket.Available > 0)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int size = entry.Socket.ReceiveFrom(_buffer, ref from);
                SteamApi.Send(remote, _buffer, size, DataChannel);
                Sent += size;
                any = true;
            }
            if (now - entry.Heard > QuietMs || !_members.Contains(remote))
            {
                entry.Socket.Dispose();
                _remotes.Remove(remote);
                Players = _remotes.Count;
            }
        }
        return any;
    }

    // A UDP socket on 127.0.0.1, any port - Windows' "port unreachable" resets off (a closed peer mustn't break reads).
    private static Socket Local()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try { socket.IOControl(unchecked((int)0x9800000C), new byte[] { 0 }, null); }
        catch (Exception) { }
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return socket;
    }
}
