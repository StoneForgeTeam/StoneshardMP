using System;
using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Death;

/// <summary>Host to a client: the items of its checkpoint (the character it comes back as when it dies) - compressed JSON
/// of the save's inventoryDataList, bag and worn - and where it is, so that what the client picked up since can be dropped
/// where it dies. Sent as the client joins, and each time the host saves.</summary>
public readonly record struct CheckpointPacket(string Where, byte[] Items) : IPacket
{
    public const byte PacketId = 53;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Where);
        writer.Write(Items.Length);
        writer.Write((ReadOnlySpan<byte>)Items);
    }

    public static CheckpointPacket Read(ref SpanReadWrite reader) => new(reader.ReadString(), JoinCompression.ReadBlob(ref reader));
}
