namespace StoneshardMP.Net.Steam;

/// <summary>A Steam friend in a lobby of the game: who, their name, the lobby.</summary>
public readonly record struct FriendLobby(ulong Friend, string Name, ulong Lobby);
