namespace StoneshardMP.Net;

// Another player in the session, by their slot: the host is 0, clients 1-7 (given by the host as they join).
// What the features know about them goes on here as they're ported (where they are, their look...).
public sealed class RemotePlayer
{
    public RemotePlayer(int slot, string name, string version)
    {
        Slot = slot;
        Name = name;
        Version = version;
    }

    public int Slot { get; }
    public string Name { get; }
    public string Version { get; }
    // Round trip to them in ms, as LiteNetLib measures it (-1: not connected to them directly - a client's
    // fellow clients).
    public int Ping { get; internal set; } = -1;

    // Where and how they're drawn (null: not in a game), and when that last came (Environment.TickCount64).
    public PlayerState? State { get; set; }
    public long StateAt { get; set; }
    // Their look (OurPlayer.Look's JSON), and a count of the times it's changed.
    public string Look { get; set; } = "";
    public int LookVersion { get; set; }
    // Values that describe the player in the standard inspection card (null until their first Profile packet).
    public PlayerProfile? Profile { get; set; }
}
