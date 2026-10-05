namespace StoneshardMP.Net.Packets;

/// <summary>What a ChestPacket says about a container.</summary>
public enum ChestKind : byte
{
    // The sender opened it: it's theirs till they close it.
    Opened = 0,
    // The sender closed it: these are its contents now.
    Closed = 1,
    // The place's owner's contents of it, as players come together.
    Contents = 2,
}
