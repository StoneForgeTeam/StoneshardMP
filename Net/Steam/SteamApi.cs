using System;
using System.Runtime.InteropServices;
using System.Text;

namespace StoneshardMP.Net.Steam;

// Steam, called directly: the game's own steam_api64.dll (Steamworks' flat API), which the game's Steamworks extension
// has already started (SteamAPI_Init) and runs the callbacks of each frame. We never start or stop it - only call it,
// and only what needs no callback: peer-to-peer packets (polled), lobbies and their results (Steam's call results,
// polled through ISteamUtils), friends, rich presence. Steam's calls are safe from any thread (the tunnel's).
// The interfaces are found again on each use: they're Steam's for the life of the process, but null without Steam.
internal static class SteamApi
{
    private const string Dll = "steam_api64";
    // (ISteamNetworking's send types: unreliable up to 1200 bytes; reliable up to 1 MB.)
    public const int SendUnreliable = 0, SendReliable = 2;
    public const int UnreliableMax = 1200;
    // (Lobby types.)
    public const int LobbyFriendsOnly = 1;
    // (Call results' callback ids: LobbyCreated_t, LobbyEnter_t.)
    private const int LobbyCreatedId = 513, LobbyEnterId = 504;
    private const int EResultOk = 1, EnterSuccess = 1;
    // (Friends we can see the games of: k_EFriendFlagImmediate.)
    private const int FriendFlagImmediate = 0x04;

    [DllImport(Dll)] private static extern IntPtr SteamAPI_SteamUser_v021();
    [DllImport(Dll)] private static extern IntPtr SteamAPI_SteamFriends_v017();
    [DllImport(Dll)] private static extern IntPtr SteamAPI_SteamMatchmaking_v009();
    [DllImport(Dll)] private static extern IntPtr SteamAPI_SteamNetworking_v006();
    [DllImport(Dll)] private static extern IntPtr SteamAPI_SteamUtils_v010();

    [DllImport(Dll)] private static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr self);

    [DllImport(Dll)] private static extern int SteamAPI_ISteamFriends_GetFriendCount(IntPtr self, int flags);
    [DllImport(Dll)] private static extern ulong SteamAPI_ISteamFriends_GetFriendByIndex(IntPtr self, int index, int flags);
    [DllImport(Dll)] private static extern IntPtr SteamAPI_ISteamFriends_GetFriendPersonaName(IntPtr self, ulong friend);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamFriends_GetFriendGamePlayed(IntPtr self, ulong friend, out FriendGameInfo info);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamFriends_SetRichPresence(IntPtr self, byte[] key, byte[] value);
    [DllImport(Dll)] private static extern void SteamAPI_ISteamFriends_ActivateGameOverlayInviteDialog(IntPtr self, ulong lobby);

    [DllImport(Dll)] private static extern ulong SteamAPI_ISteamMatchmaking_CreateLobby(IntPtr self, int type, int maxMembers);
    [DllImport(Dll)] private static extern ulong SteamAPI_ISteamMatchmaking_JoinLobby(IntPtr self, ulong lobby);
    [DllImport(Dll)] private static extern void SteamAPI_ISteamMatchmaking_LeaveLobby(IntPtr self, ulong lobby);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamMatchmaking_SetLobbyData(IntPtr self, ulong lobby, byte[] key, byte[] value);
    [DllImport(Dll)] private static extern IntPtr SteamAPI_ISteamMatchmaking_GetLobbyData(IntPtr self, ulong lobby, byte[] key);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamMatchmaking_SetLobbyJoinable(IntPtr self, ulong lobby, [MarshalAs(UnmanagedType.U1)] bool joinable);
    [DllImport(Dll)] private static extern ulong SteamAPI_ISteamMatchmaking_GetLobbyOwner(IntPtr self, ulong lobby);
    [DllImport(Dll)] private static extern int SteamAPI_ISteamMatchmaking_GetNumLobbyMembers(IntPtr self, ulong lobby);
    [DllImport(Dll)] private static extern ulong SteamAPI_ISteamMatchmaking_GetLobbyMemberByIndex(IntPtr self, ulong lobby, int index);

    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamNetworking_SendP2PPacket(IntPtr self, ulong remote, byte[] data, uint size, int type, int channel);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamNetworking_IsP2PPacketAvailable(IntPtr self, out uint size, int channel);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamNetworking_ReadP2PPacket(IntPtr self, byte[] dest, uint capacity, out uint size, out ulong remote, int channel);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser(IntPtr self, ulong remote);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamNetworking_CloseP2PSessionWithUser(IntPtr self, ulong remote);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamNetworking_AllowP2PPacketRelay(IntPtr self, [MarshalAs(UnmanagedType.U1)] bool allow);

    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamUtils_IsAPICallCompleted(IntPtr self, ulong call, [MarshalAs(UnmanagedType.U1)] out bool failed);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamUtils_GetAPICallResult(IntPtr self, ulong call, byte[] result, int size, int callbackId,
        [MarshalAs(UnmanagedType.U1)] out bool failed);

    // (FriendGameInfo_t: the game a friend is in, and its lobby.)
    [StructLayout(LayoutKind.Sequential)]
    private struct FriendGameInfo
    {
        public ulong GameId;
        public uint GameIp;
        public ushort GamePort;
        public ushort QueryPort;
        public ulong Lobby;
    }

    /// <summary>Whether Steam's there to call (the game started it).</summary>
    public static bool Available
    {
        get
        {
            try { return SteamAPI_SteamUser_v021() != IntPtr.Zero && SteamAPI_SteamNetworking_v006() != IntPtr.Zero; }
            catch (Exception) { return false; }
        }
    }

    public static ulong MySteamId => SteamAPI_ISteamUser_GetSteamID(SteamAPI_SteamUser_v021());

    // ---- peer-to-peer ----

    public static bool Send(ulong remote, byte[] data, int size, int channel)
        => SteamAPI_ISteamNetworking_SendP2PPacket(SteamAPI_SteamNetworking_v006(), remote, data, (uint)size,
            size <= UnreliableMax ? SendUnreliable : SendReliable, channel);

    /// <summary>The next packet on a channel into <paramref name="buffer"/> (its size and sender); false if there's none.</summary>
    public static bool Receive(byte[] buffer, int channel, out int size, out ulong remote)
    {
        IntPtr net = SteamAPI_SteamNetworking_v006();
        size = 0;
        remote = 0;
        if (!SteamAPI_ISteamNetworking_IsP2PPacketAvailable(net, out uint waiting, channel) || waiting > buffer.Length)
        {
            // (Too big for us: read and dropped, so it doesn't block the channel.)
            if (waiting > buffer.Length)
                SteamAPI_ISteamNetworking_ReadP2PPacket(net, new byte[waiting], waiting, out _, out _, channel);
            return false;
        }
        if (!SteamAPI_ISteamNetworking_ReadP2PPacket(net, buffer, (uint)buffer.Length, out uint read, out remote, channel))
            return false;
        size = (int)read;
        return true;
    }

    public static void Accept(ulong remote) => SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser(SteamAPI_SteamNetworking_v006(), remote);
    public static void Close(ulong remote) => SteamAPI_ISteamNetworking_CloseP2PSessionWithUser(SteamAPI_SteamNetworking_v006(), remote);
    public static void AllowRelay() => SteamAPI_ISteamNetworking_AllowP2PPacketRelay(SteamAPI_SteamNetworking_v006(), true);

    // ---- lobbies ----

    /// <summary>A lobby made: its call, for <see cref="LobbyCreated"/>.</summary>
    public static ulong CreateLobby(int type, int maxMembers) => SteamAPI_ISteamMatchmaking_CreateLobby(SteamAPI_SteamMatchmaking_v009(), type, maxMembers);

    /// <summary>A lobby joined: its call, for <see cref="LobbyEntered"/>.</summary>
    public static ulong JoinLobby(ulong lobby) => SteamAPI_ISteamMatchmaking_JoinLobby(SteamAPI_SteamMatchmaking_v009(), lobby);

    public static void LeaveLobby(ulong lobby) => SteamAPI_ISteamMatchmaking_LeaveLobby(SteamAPI_SteamMatchmaking_v009(), lobby);

    /// <summary>A CreateLobby call's result: null while it's not done; 0 if it failed, else the lobby.</summary>
    public static ulong? LobbyCreated(ulong call)
    {
        var result = new byte[16];
        if (Result(call, result, LobbyCreatedId) is not { } done)
            return null;
        return done && BitConverter.ToInt32(result, 0) == EResultOk ? BitConverter.ToUInt64(result, 8) : 0;
    }

    /// <summary>A JoinLobby call's result: null while it's not done; whether we're in.</summary>
    public static bool? LobbyEntered(ulong call)
    {
        var result = new byte[24];
        if (Result(call, result, LobbyEnterId) is not { } done)
            return null;
        return done && BitConverter.ToInt32(result, 16) == EnterSuccess;
    }

    // (null: not done; false: done, failed; true: done, result read.)
    private static bool? Result(ulong call, byte[] result, int callbackId)
    {
        IntPtr utils = SteamAPI_SteamUtils_v010();
        if (!SteamAPI_ISteamUtils_IsAPICallCompleted(utils, call, out bool failed))
            return failed ? false : null;
        return !failed && SteamAPI_ISteamUtils_GetAPICallResult(utils, call, result, result.Length, callbackId, out failed) && !failed;
    }

    public static void SetLobbyData(ulong lobby, string key, string value)
        => SteamAPI_ISteamMatchmaking_SetLobbyData(SteamAPI_SteamMatchmaking_v009(), lobby, Utf8(key), Utf8(value));

    public static string LobbyData(ulong lobby, string key)
        => Text(SteamAPI_ISteamMatchmaking_GetLobbyData(SteamAPI_SteamMatchmaking_v009(), lobby, Utf8(key)));

    public static void SetLobbyJoinable(ulong lobby, bool joinable)
        => SteamAPI_ISteamMatchmaking_SetLobbyJoinable(SteamAPI_SteamMatchmaking_v009(), lobby, joinable);

    public static ulong LobbyOwner(ulong lobby) => SteamAPI_ISteamMatchmaking_GetLobbyOwner(SteamAPI_SteamMatchmaking_v009(), lobby);

    public static ulong[] LobbyMembers(ulong lobby)
    {
        IntPtr matchmaking = SteamAPI_SteamMatchmaking_v009();
        int count = SteamAPI_ISteamMatchmaking_GetNumLobbyMembers(matchmaking, lobby);
        var members = new ulong[Math.Max(0, count)];
        for (int i = 0; i < members.Length; i++)
            members[i] = SteamAPI_ISteamMatchmaking_GetLobbyMemberByIndex(matchmaking, lobby, i);
        return members;
    }

    // ---- friends ----

    /// <summary>Friends in a lobby of this game (Stoneshard has none of its own: ours), with their lobby.</summary>
    public static FriendLobby[] FriendsInLobbies(uint appId)
    {
        IntPtr friends = SteamAPI_SteamFriends_v017();
        int count = SteamAPI_ISteamFriends_GetFriendCount(friends, FriendFlagImmediate);
        var found = new System.Collections.Generic.List<FriendLobby>();
        for (int i = 0; i < count; i++)
        {
            ulong friend = SteamAPI_ISteamFriends_GetFriendByIndex(friends, i, FriendFlagImmediate);
            // (A game id's low 24 bits are its app id.)
            if (!SteamAPI_ISteamFriends_GetFriendGamePlayed(friends, friend, out var game) || (uint)(game.GameId & 0xFFFFFF) != appId || game.Lobby == 0)
                continue;
            found.Add(new FriendLobby(friend, Text(SteamAPI_ISteamFriends_GetFriendPersonaName(friends, friend)), game.Lobby));
        }
        return found.ToArray();
    }

    public static void SetRichPresence(string key, string value)
        => SteamAPI_ISteamFriends_SetRichPresence(SteamAPI_SteamFriends_v017(), Utf8(key), Utf8(value));

    public static void InviteDialog(ulong lobby) => SteamAPI_ISteamFriends_ActivateGameOverlayInviteDialog(SteamAPI_SteamFriends_v017(), lobby);

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");

    private static string Text(IntPtr utf8) => utf8 == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(utf8) ?? "";
}
