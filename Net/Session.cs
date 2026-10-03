using System;
using System.Collections.Generic;
using System.Linq;
using LiteNetLib;
using LiteNetLib.Utils;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Net;

// The multiplayer session over LiteNetLib (UDP): a star - the host (slot 0) runs the server, clients (slots 1-7)
// connect to it, and it relays what they send to the others. LiteNetLib keeps the connection alive and times it
// out on its own thread (so a game frozen loading or saving doesn't drop anyone); its events are handled on the
// game's thread, in Poll (the mod's Tick).
//
// A packet: [type u8][from slot u8][to u8 - a slot, or Everyone] then its body. The host stamps the sender's slot
// on what clients send (they can't pose as someone else), passes it on - to everyone else, or the one it's for -
// and handles it itself if it's for everyone or for the host.
public sealed class Session : INetEventListener
{
    // What's sent between games: raise it whenever that changes, so mismatched games refuse each other.
    public const ushort Protocol = 17;
    public const byte Everyone = 255;
    public const int MaxPlayers = 8;
    // (The connection request's key: only StoneshardMP games answer each other.)
    private const string Key = "StoneshardMP";

    public enum SessionMode { Idle, Host, Client }

    private readonly string _version;
    private readonly Action<string> _log;
    private NetManager? _net;

    // Host: each client's connection by slot. Client: the host's (slot 0).
    private readonly Dictionary<int, NetPeer> _peers = new();
    private readonly Dictionary<int, RemotePlayer> _players = new();
    private readonly Dictionary<Type, Action<RemotePlayer, IPacket>> _handlers = new();
    private string _name = "";
    private int _limit = MaxPlayers;

    public Session(string version, Action<string> log)
    {
        _version = version;
        _log = log;
    }

    public SessionMode Mode { get; private set; }
    // Ours: 0 hosting, 1-7 as a client once the host has welcomed us, -1 not in a session (or not welcomed yet).
    public int Slot { get; private set; } = -1;
    public string Status { get; private set; } = "Not connected";
    public IReadOnlyCollection<RemotePlayer> Players => _players.Values;
    public bool Connected => Slot >= 0;
    public int Limit => _limit;

    public event Action<RemotePlayer>? PlayerJoined;
    public event Action<RemotePlayer>? PlayerLeft;
    // Whenever Status, Mode or the players change (the window refreshes).
    public event Action? Changed;

    // A feature's handler for a packet type: the player it's from (never us), and its body.
    public void On<T>(Action<RemotePlayer, T> handler) where T : struct, IPacket
        => _handlers.Add(typeof(T), (sender, packet) => handler(sender, (T)packet));

    public bool Host(int port, string name, int limit)
    {
        Stop("");
        _name = name;
        _limit = Math.Clamp(limit, 2, MaxPlayers);
        _net = NewManager();
        if (!_net.Start(port))
        {
            _net = null;
            SetStatus($"Couldn't host - is port {port} already in use?");
            return false;
        }
        Mode = SessionMode.Host;
        Slot = 0;
        SetStatus($"Hosting on port {port} - waiting for players");
        return true;
    }

    public void Join(string address, int port, string name)
    {
        Stop("");
        _name = name;
        _net = NewManager();
        _net.Start();
        var hello = new NetDataWriter();
        hello.Put(PacketCodec.Encode(new HelloPacket(Key, Protocol, _version, name)));
        Mode = SessionMode.Client;
        SetStatus($"Connecting to {address}:{port}...");
        _net.Connect(address, port, hello);
    }

    // Out of the session (status: why, if there's anything to say).
    public void Stop(string why)
    {
        if (_net != null)
        {
            var net = _net;
            // (Cleared first: Stop raises the disconnections, which mustn't come back here.)
            _net = null;
            net.DisconnectAll();
            net.Stop();
        }
        var gone = _players.Values.ToList();
        _players.Clear();
        _peers.Clear();
        Mode = SessionMode.Idle;
        Slot = -1;
        foreach (var player in gone)
            PlayerLeft?.Invoke(player);
        SetStatus(why.Length > 0 ? why : "Not connected");
    }

    // Each frame: LiteNetLib's events (connections, packets), handled here on the game's thread.
    public void Poll()
    {
        if (_net == null)
            return;
        _net.PollEvents();
        foreach (var (slot, peer) in _peers)
            if (_players.TryGetValue(slot, out var player))
                player.Ping = peer.RoundTripTime;
    }

    // Sends a packet: its body written by body, to everyone (or one slot), reliably and in order unless said.
    public void Send<T>(T packet, int to = Everyone, DeliveryMethod delivery = DeliveryMethod.ReliableOrdered) where T : struct, IPacket
    {
        if (_net == null || !Connected) return;
        byte[] data = PacketCodec.Encode(packet, Slot, to);
        if (Mode == SessionMode.Client)
        {
            if (_peers.TryGetValue(0, out var host)) host.Send(data, delivery);
        }
        else if (to == Everyone) _net.SendToAll(data, delivery);
        else if (_peers.TryGetValue(to, out var peer)) peer.Send(data, delivery);
    }

    private NetManager NewManager() => new(this)
    {
        // (Long enough for a game frozen a while - a big save loading - not to look gone.)
        DisconnectTimeout = 30000,
        UpdateTime = 15,
        AutoRecycle = true,
        IPv6Enabled = false,
    };

    private void SetStatus(string status)
    {
        Status = status;
        _log(status);
        Changed?.Invoke();
    }

    // ---- LiteNetLib's events (on the game's thread: PollEvents) ----

    void INetEventListener.OnConnectionRequest(ConnectionRequest request)
    {
        if (Mode != SessionMode.Host)
        {
            request.Reject();
            return;
        }
        string Reject(string why)
        {
            var reason = new NetDataWriter();
            reason.Put(PacketCodec.Encode(new RejectedPacket(why)));
            request.Reject(reason);
            return why;
        }
        try
        {
            var data = (HelloPacket)PacketCodec.Decode(request.Data.GetRemainingBytes(), out _, out _);
            if (data.Key != Key)
            {
                request.Reject();
                return;
            }
            ushort protocol = data.Protocol;
            string version = data.Version;
            string name = data.Name;
            if (protocol != Protocol)
            {
                _log(Reject($"Version mismatch: {name} has StoneshardMP {version} (protocol {protocol}), the host has {_version} (protocol {Protocol})"));
                return;
            }
            int slot = Enumerable.Range(1, _limit - 1).FirstOrDefault(s => !_players.ContainsKey(s));
            if (slot == 0)
            {
                Reject($"The game is full ({_limit} players)");
                _log($"{name} was turned away: the game is full");
                return;
            }
            var peer = request.Accept();
            peer.Tag = slot;
            _peers[slot] = peer;
            // Them: their slot, and everyone here, us first.
            var roster = new List<JoinedPacket> { new(0, _name, _version) };
            roster.AddRange(_players.Values.Select(p => new JoinedPacket((byte)p.Slot, p.Name, p.Version)));
            peer.Send(PacketCodec.Encode(new WelcomePacket((byte)slot, roster.ToArray()), 0, slot), DeliveryMethod.ReliableOrdered);
            _net!.SendToAll(PacketCodec.Encode(new JoinedPacket((byte)slot, name, version)), DeliveryMethod.ReliableOrdered, peer);
            Add(new RemotePlayer(slot, name, version));
            SetStatus($"{name} joined ({_players.Count + 1}/{_limit} players)");
        }
        catch (Exception e)
        {
            _log("Bad connection request: " + e.Message);
            request.Reject();
        }
    }

    void INetEventListener.OnPeerConnected(NetPeer peer)
    {
        // (Client: connected to the host - in once it welcomes us.)
        if (Mode == SessionMode.Client)
        {
            _peers[0] = peer;
            SetStatus("Connected - waiting for the host...");
        }
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if (_net == null)
            return;
        if (Mode == SessionMode.Host)
        {
            if (peer.Tag is not int slot || !_peers.Remove(slot) || !_players.Remove(slot, out var player))
                return;
            Send(new LeftPacket((byte)slot));
            PlayerLeft?.Invoke(player);
            SetStatus($"{player.Name} left ({_players.Count + 1}/{_limit} players)");
            return;
        }
        if (Mode != SessionMode.Client)
            return;
        string why = info.Reason switch
        {
            DisconnectReason.ConnectionRejected when info.AdditionalData.AvailableBytes > 0 => ((RejectedPacket)PacketCodec.Decode(info.AdditionalData.GetRemainingBytes(), out _, out _)).Reason,
            DisconnectReason.ConnectionRejected => "The host turned us away",
            DisconnectReason.ConnectionFailed => "No host answered",
            DisconnectReason.Timeout => "Lost the connection to the host",
            DisconnectReason.RemoteConnectionClose => "The host closed the game",
            _ => "Disconnected: " + info.Reason,
        };
        Stop(why);
    }

    void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod delivery)
    {
        try
        {
            byte[] bytes = reader.GetRemainingBytes();
            IPacket packet = PacketCodec.Decode(bytes, out byte from, out byte to);
            if (Mode == SessionMode.Host)
            {
                if (peer.Tag is not int slot) return;
                if (packet is WelcomePacket or JoinedPacket or LeftPacket or HelloPacket or RejectedPacket) return;
                from = (byte)slot;
                bytes[1] = from;
                if (to == Everyone) _net!.SendToAll(bytes, delivery, peer);
                else if (to != 0 && _peers.TryGetValue(to, out var target)) target.Send(bytes, delivery);
                if (to == 0 || to == Everyone) Handle(packet, from);
            }
            else if (Mode == SessionMode.Client && (to == Everyone || to == Slot || packet is WelcomePacket))
                Handle(packet, from);
        }
        catch (Exception e) { _log($"Packet receive failed: {e}"); }
    }

    private void Handle(IPacket packet, int from)
    {
        switch (packet)
        {
            case WelcomePacket welcome when Mode == SessionMode.Client && from == 0 && Slot < 0:
                Slot = welcome.Slot;
                foreach (var p in welcome.Players) Add(new RemotePlayer(p.Slot, p.Name, p.Version));
                SetStatus($"In {_players.GetValueOrDefault(0)?.Name ?? "the host"}'s game as player {Slot + 1}");
                return;
            case JoinedPacket joined when Mode == SessionMode.Client && from == 0:
                Add(new RemotePlayer(joined.Slot, joined.Name, joined.Version));
                SetStatus($"{joined.Name} joined");
                return;
            case LeftPacket left when Mode == SessionMode.Client && from == 0:
                if (_players.Remove(left.Slot, out var player)) { PlayerLeft?.Invoke(player); SetStatus($"{player.Name} left"); }
                return;
        }
        if (_players.TryGetValue(from, out var sender) && _handlers.TryGetValue(packet.GetType(), out var handler))
            handler(sender, packet);
    }

    private void Add(RemotePlayer player)
    {
        if (player.Slot == Slot)
            return;
        _players[player.Slot] = player;
        PlayerJoined?.Invoke(player);
        Changed?.Invoke();
    }

    void INetEventListener.OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
        => _log($"Network error ({endPoint}): {socketError}");

    void INetEventListener.OnNetworkReceiveUnconnected(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }

    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
}
