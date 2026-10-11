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
    // (Lobby types.)
    public const int LobbyFriendsOnly = 1;
    // (Call results' callback ids: LobbyCreated_t, LobbyEnter_t.)
    private const int LobbyCreatedId = 513, LobbyEnterId = 504;
    private const int EResultOk = 1, EnterSuccess = 1;
    // (Friends we can see the games of: k_EFriendFlagImmediate.)
    private const int FriendFlagImmediate = 0x04;

    [DllImport(Dll)] private static extern void SteamAPI_RegisterCallback(IntPtr callback, int id);
    [DllImport(Dll)] private static extern void SteamAPI_UnregisterCallback(IntPtr callback);
    [DllImport(Dll)] private static extern IntPtr SteamAPI_SteamUser_v021();
    [DllImport(Dll)] private static extern IntPtr SteamAPI_SteamFriends_v017();
    [DllImport(Dll)] private static extern IntPtr SteamAPI_SteamMatchmaking_v009();
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
    [DllImport(Dll)] private static extern void SteamAPI_ISteamFriends_ClearRichPresence(IntPtr self);

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
            try { return SteamAPI_SteamUser_v021() != IntPtr.Zero && SteamAPI_SteamNetworkingMessages_SteamAPI_v002() != IntPtr.Zero; }
            catch (Exception) { return false; }
        }
    }

    public static ulong MySteamId => SteamAPI_ISteamUser_GetSteamID(SteamAPI_SteamUser_v021());

    // ---- peer-to-peer: ISteamNetworkingMessages (Valve's relay network, SDR) ----
    // (A player as Steam's messages know one: SteamNetworkingIdentity - its type, 16 a Steam id; its size, 8; the id.
    // 136 bytes in all. A message: SteamNetworkingMessage_t - its data at 0, size at 8, sender's identity at 16.)
    private const int IdentitySize = 136, IdentitySteamId = 16;
    // (Send flags: unreliable without Nagle's wait, and a broken session started again by itself - LiteNetLib inside
    // does its own reliability; the greeting, reliable.)
    private const int SendFast = 1 | 32, SendSure = 8 | 32;

    [DllImport(Dll)] private static extern IntPtr SteamAPI_SteamNetworkingMessages_SteamAPI_v002();
    [DllImport(Dll)] private static extern IntPtr SteamAPI_SteamNetworkingUtils_SteamAPI_v004();
    [DllImport(Dll)] private static extern int SteamAPI_ISteamNetworkingMessages_SendMessageToUser(IntPtr self, byte[] identity, byte[] data, uint size, int flags, int channel);
    [DllImport(Dll)] private static extern int SteamAPI_ISteamNetworkingMessages_ReceiveMessagesOnChannel(IntPtr self, int channel, IntPtr[] messages, int max);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamNetworkingMessages_AcceptSessionWithUser(IntPtr self, byte[] identity);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SteamAPI_ISteamNetworkingMessages_CloseSessionWithUser(IntPtr self, byte[] identity);
    [DllImport(Dll)] private static extern int SteamAPI_ISteamNetworkingMessages_GetSessionConnectionInfo(IntPtr self, byte[] identity, byte[] info, byte[] status);
    [DllImport(Dll)] private static extern void SteamAPI_SteamNetworkingMessage_t_Release(IntPtr message);
    [DllImport(Dll)] private static extern void SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess(IntPtr self);

    private static IntPtr Messages => SteamAPI_SteamNetworkingMessages_SteamAPI_v002();

    private static byte[] Identity(ulong steamId)
    {
        var identity = new byte[IdentitySize];
        BitConverter.GetBytes(IdentitySteamId).CopyTo(identity, 0);
        BitConverter.GetBytes(8).CopyTo(identity, 4);
        BitConverter.GetBytes(steamId).CopyTo(identity, 8);
        return identity;
    }

    public static bool Send(ulong remote, byte[] data, int size, int channel, bool sure = false)
        => SteamAPI_ISteamNetworkingMessages_SendMessageToUser(Messages, Identity(remote), data, (uint)size, sure ? SendSure : SendFast, channel) == 1;

    /// <summary>The next message on a channel into <paramref name="buffer"/> (its size and sender); false if there's none.
    /// One too big for it is dropped.</summary>
    public static bool Receive(byte[] buffer, int channel, out int size, out ulong remote)
    {
        size = 0;
        remote = 0;
        var got = new IntPtr[1];
        while (SteamAPI_ISteamNetworkingMessages_ReceiveMessagesOnChannel(Messages, channel, got, 1) > 0)
        {
            IntPtr message = got[0];
            try
            {
                int length = Marshal.ReadInt32(message, 8);
                if (length < 0 || length > buffer.Length || Marshal.ReadInt32(message, 16) != IdentitySteamId)
                    continue;
                Marshal.Copy(Marshal.ReadIntPtr(message, 0), buffer, 0, length);
                size = length;
                remote = (ulong)Marshal.ReadInt64(message, 24);
                return true;
            }
            finally { SteamAPI_SteamNetworkingMessage_t_Release(message); }
        }
        return false;
    }

    public static void Accept(ulong remote) => SteamAPI_ISteamNetworkingMessages_AcceptSessionWithUser(Messages, Identity(remote));
    public static void Close(ulong remote) => SteamAPI_ISteamNetworkingMessages_CloseSessionWithUser(Messages, Identity(remote));

    /// <summary>Steam's relay network got ready ahead of the first message (it takes a few seconds).</summary>
    public static void AllowRelay() => SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess(SteamAPI_SteamNetworkingUtils_SteamAPI_v004());

    /// <summary>How the session with a player is: its ping (ms; -1 unknown) and whether it goes through Valve's relays.
    /// (SteamNetConnectionRealTimeStatus_t: the ping at 4. SteamNetConnectionInfo_t: the relay's POP at 172.)</summary>
    public static (int Ping, bool Relayed) Link(ulong remote)
    {
        var info = new byte[1024];
        var status = new byte[256];
        int state = SteamAPI_ISteamNetworkingMessages_GetSessionConnectionInfo(Messages, Identity(remote), info, status);
        if (state <= 0)
            return (-1, false);
        return (BitConverter.ToInt32(status, 4), BitConverter.ToUInt32(info, 172) != 0);
    }

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

    public static string FriendName(ulong friend) => Text(SteamAPI_ISteamFriends_GetFriendPersonaName(SteamAPI_SteamFriends_v017(), friend));

    public static void ClearRichPresence() => SteamAPI_ISteamFriends_ClearRichPresence(SteamAPI_SteamFriends_v017());

    // ---- callbacks (SteamCallback) ----

    public static void RegisterCallback(IntPtr callback, int id) => SteamAPI_RegisterCallback(callback, id);

    public static void UnregisterCallback(IntPtr callback) => SteamAPI_UnregisterCallback(callback);

    public static void InviteDialog(ulong lobby) => SteamAPI_ISteamFriends_ActivateGameOverlayInviteDialog(SteamAPI_SteamFriends_v017(), lobby);

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");

    private static string Text(IntPtr utf8) => utf8 == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(utf8) ?? "";
}
